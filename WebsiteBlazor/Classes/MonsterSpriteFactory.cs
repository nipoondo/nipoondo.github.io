using AutoSpriteCreator;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace WebsiteBlazor.Classes
{
    public class MonsterSpriteParts
    {
        public Image<Rgba32> Body { get; set; }
        public Image<Rgba32> Head { get; set; }
        public Image<Rgba32> Limbs { get; set; }
        public Image<Rgba32> Appendages { get; set; }

        // anchors in pixel coordinates (System.Drawing.Point)
        public Dictionary<string, SixLabors.ImageSharp.Point> Anchors { get; set; } = new Dictionary<string, SixLabors.ImageSharp.Point>();

        // optional: expose masks for debugging / advanced rebuild
        public bool[,] BodyMask { get; set; }
        public bool[,] HeadMask { get; set; }
    }

    public static class MonsterSpriteFactory
    {
        /// <summary>
        /// Generate layered sprite parts (body, head, limbs, appendages) and anchor points.
        /// </summary>
        public static MonsterSpriteParts GenerateParts(Settings settings)
        {
            int width = settings.Dimension;
            int height = settings.Dimension;
            int margin = settings.Margin;

            // get processed masks (head/body split)
            MaskBuilder.BuildProcessedMasks(settings, out bool[,] bodyMask, out bool[,] headMask);

            // colors (same palette logic as your existing GenerateMonster)
            Rgba32 baseColor = ColorUtils.RandomColorHarmonious();
            Rgba32 accentColor = settings.ColorStyle == ColorStyle.Harmonious ? ColorUtils.RandomAccent(baseColor) : ColorUtils.RandomColorHarmonious();
            Rgba32 patternColor = (settings.ColorStyle == ColorStyle.Harmonious || settings.ColorStyle == ColorStyle.RandomAccent) ? ColorUtils.Darken(baseColor, 0.55f) : ColorUtils.RandomColorHarmonious();
            Rgba32 outlineColor = (settings.ColorStyle == ColorStyle.Harmonious || settings.ColorStyle == ColorStyle.RandomAccent || settings.ColorStyle == ColorStyle.RandomDarken) ? ColorUtils.Darken(baseColor, 0.34f) : ColorUtils.RandomColorHarmonious();

            // body image (transparent background)
            var bodyImg = new Image<Rgba32>(width, height);
            bodyImg.Mutate(ctx => ctx.BackgroundColor(new Rgba32(0, 0, 0, 0)));
            FeatureDrawer.ApplyNoisePalette(bodyImg, bodyMask, baseColor, accentColor, nColors: 6, outline: false);
            if (settings.DrawPatterns)
            {
                FeatureDrawer.AddInternalPatterns(bodyImg, bodyMask, patternColor);
            }
            FeatureDrawer.DrawMaskOutline(bodyImg, bodyMask, outlineColor);

            // head image (so head can be animated independently)
            var headImg = new Image<Rgba32>(width, height);
            headImg.Mutate(ctx => ctx.BackgroundColor(new Rgba32(0, 0, 0, 0)));
            // You may want slightly fewer palette colors for head to read better
            FeatureDrawer.ApplyNoisePalette(headImg, headMask, baseColor, accentColor, nColors: 4, outline: false);
            FeatureDrawer.AddEyes(headImg, headMask, accentColor);
            FeatureDrawer.AddMouth(headImg, headMask);
            FeatureDrawer.DrawMaskOutline(headImg, headMask, outlineColor);

            // limbs (draw only limb pixels onto transparent image)
            var limbImg = new Image<Rgba32>(width, height);
            limbImg.Mutate(ctx => ctx.BackgroundColor(new Rgba32(0, 0, 0, 0)));
            FeatureDrawer.AddAnchoredLimbs(limbImg, bodyMask, accentColor, margin);

            // appendages (tails, tentacles, etc.)
            var appendImg = new Image<Rgba32>(width, height);
            appendImg.Mutate(ctx => ctx.BackgroundColor(new Rgba32(0, 0, 0, 0)));
            FeatureDrawer.AddAppendages(appendImg, bodyMask, accentColor, margin);

            // anchors (simple, robust heuristics)
            var anchors = new Dictionary<string, SixLabors.ImageSharp.Point>();
            anchors["head"] = Centroid(headMask);      // pivot for head animations
            anchors["body"] = Centroid(bodyMask);      // general body centroid
            anchors["leftHip"] = FindAttachmentPoint(bodyMask, left: true, bodyStartY: height / 3);
            anchors["rightHip"] = FindAttachmentPoint(bodyMask, left: false, bodyStartY: height / 3);
            anchors["feetCenter"] = FindLowestCenter(bodyMask);

            return new MonsterSpriteParts
            {
                Body = bodyImg,
                Head = headImg,
                Limbs = limbImg,
                Appendages = appendImg,
                Anchors = anchors,
                BodyMask = bodyMask,
                HeadMask = headMask
            };
        }

        /// <summary>
        /// Cheap, fast layer-based animation generator.
        /// Produces 'frames' images where layers are composed with small translations to simulate walk/bob.
        /// </summary>
        public static List<Image<Rgba32>> GenerateSimpleAnimation(MonsterSpriteParts parts, int frameCount, float intensity = 1f)
        {
            int w = parts.Body.Width;
            int h = parts.Body.Height;
            var frames = new List<Image<Rgba32>>(frameCount);

            // scale amplitudes with width so animations feel proportional
            int headBobPx = Math.Max(1, w / 32);
            int bodyWobblePx = Math.Max(1, w / 48);
            int limbSwayPx = Math.Max(1, w / 40);

            for (int f = 0; f < frameCount; f++)
            {
                float t = f / (float)frameCount;
                double twoPi = Math.PI * 2.0;

                int headOffsetY = (int)Math.Round(Math.Sin(twoPi * t * 1.0) * headBobPx * intensity);
                int bodyOffsetX = (int)Math.Round(Math.Sin(twoPi * t * 0.6) * bodyWobblePx * intensity);
                int limbOffsetX = (int)Math.Round(Math.Sin(twoPi * t * 1.2) * limbSwayPx * intensity);

                var frame = new Image<Rgba32>(w, h);
                frame.Mutate(ctx => ctx.BackgroundColor(new Rgba32(0, 0, 0, 0)));

                // draw order: body -> appendages -> limbs -> head (head sits above limbs)
                frame.Mutate(ctx =>
                {
                    ctx.DrawImage(parts.Body, new SixLabors.ImageSharp.Point(bodyOffsetX, 0), 1f);
                    ctx.DrawImage(parts.Appendages, new SixLabors.ImageSharp.Point(bodyOffsetX, 0), 1f);
                    ctx.DrawImage(parts.Limbs, new SixLabors.ImageSharp.Point(limbOffsetX, 0), 1f);
                    ctx.DrawImage(parts.Head, new SixLabors.ImageSharp.Point(0, headOffsetY), 1f);
                });

                frames.Add(frame);
            }

            return frames;
        }

        /// <summary>
        /// Save frames to disk as PNGs (convenience).
        /// </summary>
        public static void SaveFramesPng(List<Image<Rgba32>> frames, string pathPrefix)
        {
            for (int i = 0; i < frames.Count; i++)
            {
                string p = $"{pathPrefix}_{i:D2}.png";
                using (var fs = System.IO.File.Create(p))
                {
                    frames[i].SaveAsPng(fs);
                }
            }
        }

        // -------------------- helpers --------------------

        static SixLabors.ImageSharp.Point Centroid(bool[,] mask)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            double sx = 0, sy = 0;
            int count = 0;
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (mask[x, y]) { sx += x; sy += y; count++; }

            if (count == 0) return new SixLabors.ImageSharp.Point(w / 2, h / 2);
            return new SixLabors.ImageSharp.Point((int)Math.Round(sx / count), (int)Math.Round(sy / count));
        }

        // find a robust left/right attachment point near the lower half of the body
        static SixLabors.ImageSharp.Point FindAttachmentPoint(bool[,] mask, bool left, int bodyStartY)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);

            // search bottom-up from the lowest third of the body to find first occupied pixel on left/right
            int startY = Math.Max(0, bodyStartY + (h - bodyStartY) / 4);
            for (int y = h - 1; y >= startY; y--)
            {
                if (left)
                {
                    for (int x = 0; x < w; x++) if (mask[x, y]) return new SixLabors.ImageSharp.Point(x, y);
                }
                else
                {
                    for (int x = w - 1; x >= 0; x--) if (mask[x, y]) return new SixLabors.ImageSharp.Point(x, y);
                }
            }

            // fallback to centroid
            return Centroid(mask);
        }

        static SixLabors.ImageSharp.Point FindLowestCenter(bool[,] mask)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            for (int y = h - 1; y >= 0; y--)
            {
                int sumX = 0, cnt = 0;
                for (int x = 0; x < w; x++) if (mask[x, y]) { sumX += x; cnt++; }
                if (cnt > 0) return new SixLabors.ImageSharp.Point(sumX / cnt, y);
            }
            return Centroid(mask);
        }
    }
}
