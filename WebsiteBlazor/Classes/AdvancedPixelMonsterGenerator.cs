using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using static System.Net.Mime.MediaTypeNames;
using SixLabors.ImageSharp.Processing;
using WebsiteBlazor.Classes;
using System.Runtime;
using System.Threading;
using SixLabors.ImageSharp.Formats.Gif;

namespace AutoSpriteCreator
{
    public static class RNG
    {
        public static Random Rand = new Random();

        public static void SetSeed(int seed) => Rand = new(seed);
    }

    public class AdvancedPixelMonsterGenerator
    {
        public static string MonsterMain(Settings settings)
        {
            if (settings.Generator == 0)
            {
                Image<Rgba32> monster = GenerateMonster(settings.Dimension, settings.Dimension, settings);

                using (var ms = new MemoryStream())
                {
                    monster.SaveAsPng(ms);
                    var bytes = ms.ToArray();
                    return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
                }
            }
            else
            {
                // Build layered parts then frames
                var parts = MonsterSpriteFactory.GenerateParts(settings);

                // build animation frames (layer-composed images)
                var frames = MonsterSpriteFactory.GenerateSimpleAnimation(parts, frameCount: settings.FrameCount, intensity: settings.AnimationIntesity);

                // If only one frame, return it as PNG (transparent)
                if (frames.Count == 1)
                {
                    using (var ms = new MemoryStream())
                    {
                        frames[0].SaveAsPng(ms);
                        var bytes = ms.ToArray();

                        // Dispose frames
                        foreach (var f in frames) f.Dispose();

                        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
                    }
                }

                using (var animated = frames[0].Clone())
                {
                    // infinite loop
                    var rootMeta = animated.Metadata.GetGifMetadata();
                    rootMeta.RepeatCount = 0;

                    // convert ms delay to GIF hundredths of seconds (min 1)
                    ushort gifDelay = (ushort)Math.Max(1, settings.FrameDelayInMs / 10);

                    // set for first frame
                    animated.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = gifDelay;
                    animated.Frames.RootFrame.Metadata.GetGifMetadata().DisposalMethod = GifDisposalMethod.RestoreToBackground;

                    // add remaining frames and set metadata for each
                    for (int i = 1; i < frames.Count; i++)
                    {
                        animated.Frames.AddFrame(frames[i].Frames.RootFrame);
                        var meta = animated.Frames[i].Metadata.GetGifMetadata();
                        meta.FrameDelay = gifDelay;
                        meta.DisposalMethod = GifDisposalMethod.RestoreToBackground;
                    }

                    // dispose per-frame images
                    foreach (var f in frames) f.Dispose();

                    using (var ms = new MemoryStream())
                    {
                        animated.SaveAsGif(ms);
                        var bytes = ms.ToArray();
                        return $"data:image/gif;base64,{Convert.ToBase64String(bytes)}";
                    }
                }
            }
        }


        public static Image<Rgba32> GenerateMonster(int width, int height, Settings settings)
        {
            //int margin = Math.Max(3, width / 12);

            // 1) Build mask (all geometry lives here)
            bool[,] mask = MaskBuilder.BuildMask(settings);

            // 2) Create Image<Rgba32> and palette
            Image<Rgba32> bmp = new Image<Rgba32>(width, height);
            bmp.Mutate(ctx => ctx.BackgroundColor(Rgba32.ParseHex("#00000000")));

            Rgba32 baseColor = ColorUtils.RandomColorHarmonious();
            Rgba32 accentColor = ColorUtils.RandomAccent(baseColor);
            Rgba32 patternColor = ColorUtils.Darken(baseColor, 0.55f);
            Rgba32 outlineColor = ColorUtils.Darken(baseColor, 0.34f);

            // 3) Paint base body from mask using noise-driven palette (new)
            FeatureDrawer.ApplyNoisePalette(bmp, mask, baseColor, accentColor, nColors: 6, outline: true);


            // 4) Draw internal patterns BEFORE eyes & mouth so they appear behind those features
            if (settings.DrawPatterns)
            {
                FeatureDrawer.AddInternalPatterns(bmp, mask, patternColor);
            }

            // 5) Draw features (eyes, mouth, limbs) on top of patterns
            FeatureDrawer.AddEyes(bmp, mask, accentColor);
            FeatureDrawer.AddMouth(bmp, mask);
            FeatureDrawer.AddAnchoredLimbs(bmp, mask, accentColor, settings.Margin);

            // 6) Outline & shading (topmost)
            FeatureDrawer.DrawMaskOutline(bmp, mask, outlineColor);

            //FeatureDrawer.ApplySimpleShading(bmp, mask);

            return bmp;
        }
    }
}
