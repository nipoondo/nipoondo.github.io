using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AutoSpriteCreator
{
    public static class ColorUtils
    {
        public static Rgba32 RandomColorHarmonious()
        {
            int hue = RNG.Rand.Next(360);
            return ColorFromHSV(hue, 0.6, 0.65);
        }

        public static Rgba32 RandomAccent(Rgba32 baseColor)
        {
            double h, s, v;
            RGBtoHSV(baseColor, out h, out s, out v);

            h = (h + RNG.Rand.NextDouble() * 60 - 30 + 360) % 360;
            return ColorFromHSV((int)h, Math.Min(1, s + 0.12), Math.Min(1, v + 0.12));
        }

        public static Rgba32 Darken(Rgba32 c, float amount)
        {
            int r = (int)(c.R * amount);
            int g = (int)(c.G * amount);
            int b = (int)(c.B * amount);

            return new Rgba32((byte)Clamp(r), (byte)Clamp(g), (byte)Clamp(b), 255);
        }

        public static Rgba32 Lighten(Rgba32 c, float amount)
        {
            int r = (int)Math.Min(255, c.R + (255 - c.R) * amount);
            int g = (int)Math.Min(255, c.G + (255 - c.G) * amount);
            int b = (int)Math.Min(255, c.B + (255 - c.B) * amount);

            return new Rgba32((byte)Clamp(r), (byte)Clamp(g), (byte)Clamp(b), 255);
        }

        static int Clamp(int v) => Math.Max(0, Math.Min(255, v));

        public static Rgba32 ColorFromHSV(double hue, double sat, double val)
        {
            double C = val * sat;
            double X = C * (1 - Math.Abs(((hue / 60.0) % 2) - 1));
            double m = val - C;
            double r = 0, g = 0, b = 0;

            if (hue < 60) { r = C; g = X; b = 0; }
            else if (hue < 120) { r = X; g = C; b = 0; }
            else if (hue < 180) { r = 0; g = C; b = X; }
            else if (hue < 240) { r = 0; g = X; b = C; }
            else if (hue < 300) { r = X; g = 0; b = C; }
            else { r = C; g = 0; b = X; }

            return new Rgba32(
                (byte)Clamp((int)((r + m) * 255)),
                (byte)Clamp((int)((g + m) * 255)),
                (byte)Clamp((int)((b + m) * 255)),
                255
            );
        }

        public static void RGBtoHSV(Rgba32 c, out double h, out double s, out double v)
        {
            double rd = c.R / 255.0;
            double gd = c.G / 255.0;
            double bd = c.B / 255.0;

            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));

            v = max;
            double d = max - min;
            s = max == 0 ? 0 : d / max;

            if (d == 0) h = 0;
            else if (max == rd) h = 60 * (((gd - bd) / d) % 6);
            else if (max == gd) h = 60 * (((bd - rd) / d) + 2);
            else h = 60 * (((rd - gd) / d) + 4);

            if (h < 0) h += 360;
        }
    }
}
