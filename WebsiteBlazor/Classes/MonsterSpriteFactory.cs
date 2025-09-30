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
            FeatureDrawer.ApplyNoisePalette(bodyImg, bodyMask, baseColor, accentColor, nColors: settings.numberOfColors, outline: false, mode: settings.paletteMode);
            if (settings.DrawPatterns)
            {
                FeatureDrawer.AddInternalPatterns(bodyImg, bodyMask, patternColor);
            }
            FeatureDrawer.DrawMaskOutline(bodyImg, bodyMask, outlineColor);

            // head image (so head can be animated independently)
            var headImg = new Image<Rgba32>(width, height);
            headImg.Mutate(ctx => ctx.BackgroundColor(new Rgba32(0, 0, 0, 0)));
            // You may want slightly fewer palette colors for head to read better
            FeatureDrawer.ApplyNoisePalette(headImg, headMask, baseColor, accentColor, nColors: settings.numberOfColors, outline: false, mode: settings.paletteMode);
            FeatureDrawer.AddEyes(headImg, headMask, accentColor);
            FeatureDrawer.AddMouth(headImg, headMask);
            FeatureDrawer.DrawMaskOutline(headImg, headMask, outlineColor);

            // limbs (draw only limb pixels onto transparent image)
            var limbImg = new Image<Rgba32>(width, height);
            limbImg.Mutate(ctx => ctx.BackgroundColor(new Rgba32(0, 0, 0, 0)));
            FeatureDrawer.AddAnchoredLimbs(limbImg, bodyMask, accentColor, margin);

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
                Anchors = anchors,
                BodyMask = bodyMask,
                HeadMask = headMask
            };
        }



        public static List<Image<Rgba32>> GenerateSimpleAnimation(MonsterSpriteParts parts, int frameCount, float intensity = 1f)
        {
            int w = parts.Body.Width;
            int h = parts.Body.Height;
            var frames = new List<Image<Rgba32>>(frameCount);

            // pixel-art tuned amplitudes
            int headBobPx = Math.Max(1, w / 40);       // head vertical motion
            int limbBobPx = Math.Max(1, w / 60);       // limb vertical motion
            float maxVerticalStretch = 0.06f * intensity; // ~6% change at peak

            // find visible vertical content bounds of the body by scanning pixels (compatible across ImageSharp versions)
            int contentTop = h, contentBottom = -1;
            for (int y = 0; y < h; y++)
            {
                bool rowHasOpaque = false;
                for (int x = 0; x < w; x++)
                {
                    if (parts.Body[x, y].A != 0)
                    {
                        rowHasOpaque = true;
                        break;
                    }
                }
                if (rowHasOpaque)
                {
                    if (y < contentTop) contentTop = y;
                    if (y > contentBottom) contentBottom = y;
                }
            }
            if (contentBottom < contentTop) { contentTop = 0; contentBottom = h - 1; } // fallback
            int contentHeight = contentBottom - contentTop + 1;

            double twoPi = Math.PI * 2.0;
            double breatheCyclesPerLoop = 1.0; // one breath per full loop

            for (int f = 0; f < frameCount; f++)
            {
                float t = f / (float)frameCount;

                // slightly asymmetric breathing curve (more natural)
                double fund = Math.Sin(twoPi * t * breatheCyclesPerLoop);
                double second = 0.35 * Math.Sin(twoPi * t * breatheCyclesPerLoop * 2.0);
                double breath = fund * 0.75 + second * 0.25;
                breath = Math.Max(-1.0, Math.Min(1.0, breath));

                // BODY: compute vertical scale (squash/stretch) around bottom anchor (feet fixed)
                float scaleY = 1f + (float)(breath * maxVerticalStretch);
                int scaledContentHeight = Math.Max(1, (int)Math.Round(contentHeight * scaleY));

                // destination Y so bottom of scaled content stays at the original bottom (bottom-anchored)
                int destY = contentBottom - scaledContentHeight + 1;
                destY = Math.Max(0, Math.Min(destY, h - scaledContentHeight)); // clamp

                // how much the body top moved relative to original contentTop
                int bodyTopShift = destY - contentTop;

                // LIMBS: small vertical motion in-phase with breath, anchored to body
                int limbOffsetY = bodyTopShift + (int)Math.Round(breath * limbBobPx);

                // HEAD: slightly different amplitude + small phase offset, anchored to body baseline
                double headPhaseOffset = 0.10;
                double headFund = Math.Sin(twoPi * (t + headPhaseOffset) * breatheCyclesPerLoop);
                double headSecond = 0.25 * Math.Sin(twoPi * (t + headPhaseOffset) * breatheCyclesPerLoop * 2.0);
                double headBreath = headFund * 0.75 + headSecond * 0.25;
                int headOffsetY = bodyTopShift + (int)Math.Round(headBreath * headBobPx);

                // Build frame
                var frame = new Image<Rgba32>(w, h);

                // Clear to transparent using BackgroundColor (compatible)
                frame.Mutate(ctx => ctx.BackgroundColor(new Rgba32(0, 0, 0, 0)));

                // Draw body: either original or vertically resized visible content (nearest-neighbor)
                if (scaledContentHeight == contentHeight)
                {
                    frame.Mutate(ctx => ctx.DrawImage(parts.Body, new SixLabors.ImageSharp.Point(0, 0), 1f));
                }
                else
                {
                    // crop visible content then resize vertically with NearestNeighbor
                    using (var crop = parts.Body.Clone(x => x.Crop(new SixLabors.ImageSharp.Rectangle(0, contentTop, w, contentHeight))))
                    using (var scaled = crop.Clone(x => x.Resize(new SixLabors.ImageSharp.Processing.ResizeOptions
                    {
                        Size = new SixLabors.ImageSharp.Size(w, scaledContentHeight),
                        Sampler = SixLabors.ImageSharp.Processing.KnownResamplers.NearestNeighbor,
                        Mode = SixLabors.ImageSharp.Processing.ResizeMode.Stretch
                    })))
                    {
                        frame.Mutate(ctx => ctx.DrawImage(scaled, new SixLabors.ImageSharp.Point(0, destY), 1f));
                    }
                }

                // Draw limbs (vertical only, anchored to body)
                frame.Mutate(ctx => ctx.DrawImage(parts.Limbs, new SixLabors.ImageSharp.Point(0, limbOffsetY), 1f));

                // Draw head (vertical only, anchored to body baseline + head offset)
                frame.Mutate(ctx => ctx.DrawImage(parts.Head, new SixLabors.ImageSharp.Point(0, headOffsetY), 1f));

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
