using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AutoSpriteCreator
{
    // Put near the top of FeatureDrawer
    public enum PaletteMode
    {
        Monochrome,
        Analogous,
        Complementary,
        SplitComplementary,
        Triadic,
        TwoToneRandom,   // two strong hues, softer shading
        SoftStripes,      // produces an alternating-hue palette (for stripe-like bands)
        Random
    }

    public static class FeatureDrawer
    {
        public static void ApplyNoisePalette(Image<Rgba32> bmp, bool[,] mask,
    Rgba32 baseColor, Rgba32 accentColor, int nColors = 6, bool outline = true,
    PaletteMode mode = PaletteMode.Monochrome, bool[,] headMask = null, PaletteMode? headMode = null,
    int? paletteSeed = null)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);

            // if mode is Random, choose any other mode per random
            if(mode == PaletteMode.Random)
                mode = (PaletteMode)(RNG.Rand.Next() % 7);

            // Build main palette and optional head palette
            Rgba32[] palette = BuildPaletteFromMode(baseColor, mode, nColors, paletteSeed);
            Rgba32[] headPalette = null;
            if (headMask != null)
            {
                var hm = headMode ?? mode;
                // small seed difference so palettes are related but not identical
                headPalette = BuildPaletteFromMode(baseColor, hm, nColors, (paletteSeed ?? 0) + 12345);
            }

            var noise = new ValueNoise(RNG.Rand.Next())
            {
                Octaves = 5,
                Period = 30.0,
                Persistence = 0.4,
                Lacunarity = 3.0
            };
            var noise2 = new ValueNoise(RNG.Rand.Next())
            {
                Octaves = 3,
                Period = 40.0,
                Persistence = 0.4,
                Lacunarity = 3.0
            };

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    if (!mask[x, y]) continue;

                    double colX = Math.Ceiling(Math.Abs(x - (w - 1) * 0.5));
                    double n = Math.Pow(Math.Abs(noise.GetNoise2D(colX, y)), 1.5) * 3.0;
                    double n2 = Math.Pow(Math.Abs(noise2.GetNoise2D(colX, y)), 1.5) * 3.0;

                    // edge nudges (preserve outline behavior)
                    bool right = PixelUtils.IsPointInMask(mask, x + 1, y);
                    bool left = PixelUtils.IsPointInMask(mask, x - 1, y);
                    bool down = PixelUtils.IsPointInMask(mask, x, y + 1);
                    bool up = PixelUtils.IsPointInMask(mask, x, y - 1);

                    if (!down) { n -= 0.45; n *= 0.8; if (outline) PixelUtils.SafeSetPixel(bmp, x, y + 1, Rgba32.ParseHex("#00000000")); }
                    if (!right) { n += 0.2; n *= 1.1; if (outline) PixelUtils.SafeSetPixel(bmp, x + 1, y, Rgba32.ParseHex("#00000000")); }
                    if (!up) { n += 0.45; n *= 1.2; if (outline) PixelUtils.SafeSetPixel(bmp, x, y - 1, Rgba32.ParseHex("#00000000")); }
                    if (!left) { n += 0.2; n *= 1.1; if (outline) PixelUtils.SafeSetPixel(bmp, x - 1, y, Rgba32.ParseHex("#00000000")); }

                    // normalized noise value in 0..1
                    n = Math.Max(0.0, Math.Min(1.0, n));
                    n2 = Math.Max(0.0, Math.Min(1.0, n2));

                    // choose which palette to use: head or body
                    var useHead = headMask != null && headMask[x, y];
                    Rgba32[] usePal = useHead ? headPalette ?? palette : palette;

                    // accent placement: use a high-threshold on the second noise channel for sparing accents
                    if (n2 > 0.9)
                    {
                        // mix accent with palette color for variety (keeps accents from being pure neon)
                        var baseCol = GetPaletteColor(usePal, n);
                        var mixed = LerpColor(baseCol, accentColor, 0.75); // 75% accent
                        PixelUtils.SafeSetPixel(bmp, x, y, mixed);
                    }
                    else
                    {
                        // Smooth palette lookup (interpolated) to avoid abrupt jumps
                        var col = GetPaletteColor(usePal, n);
                        PixelUtils.SafeSetPixel(bmp, x, y, col);
                    }
                }
            }
        }

        static Rgba32 LerpColor(Rgba32 a, Rgba32 b, double t)
        {
            double r = a.R + (b.R - a.R) * t;
            double g = a.G + (b.G - a.G) * t;
            double bl = a.B + (b.B - a.B) * t;
            return new Rgba32((byte)Math.Clamp((int)Math.Round(r), 0, 255),
                              (byte)Math.Clamp((int)Math.Round(g), 0, 255),
                              (byte)Math.Clamp((int)Math.Round(bl), 0, 255),
                              255);
        }

        static Rgba32 GetPaletteColor(Rgba32[] pal, double t)
        {
            if (pal == null || pal.Length == 0) return Rgba32.ParseHex("#FF00FF"); // debug fallback
            if (pal.Length == 1) return pal[0];
            t = Math.Max(0.0, Math.Min(1.0, t));
            double idx = t * (pal.Length - 1);
            int i0 = (int)Math.Floor(idx);
            int i1 = Math.Min(pal.Length - 1, i0 + 1);
            double ft = idx - i0;
            return LerpColor(pal[i0], pal[i1], ft);
        }

        static Rgba32[] BuildPaletteFromMode(Rgba32 baseColor, PaletteMode mode, int nColors, int? seed = null)
        {
            double h, s, v;
            ColorUtils.RGBtoHSV(baseColor, out h, out s, out v);

            var rng = seed.HasValue ? new Random(seed.Value) : RNG.Rand;
            double clampSMin = 0.18;
            double clampSMax = 0.95;
            double clampVMin = 0.15;
            double clampVMax = 0.98;

            Rgba32[] pal = new Rgba32[nColors];
            // helper to normalize hue angle
            double Wrap(double a) { a %= 360; if (a < 0) a += 360; return a; }

            switch (mode)
            {
                case PaletteMode.Monochrome:
                    {
                        // like your original palette but keep subtle sat adjustments
                        double vMin = Math.Max(clampVMin, v * 0.35);
                        double vMax = Math.Min(clampVMax, v * 1.15 + 0.04);
                        for (int i = 0; i < nColors; i++)
                        {
                            double t = nColors == 1 ? 0.5 : (double)i / (nColors - 1);
                            double vv = vMin + (vMax - vMin) * t;
                            double ss = Math.Max(clampSMin, Math.Min(clampSMax, s * (1.0 - 0.12 * (t - 0.5))));
                            pal[i] = ColorUtils.ColorFromHSV(h, ss, vv);
                        }
                    }
                    break;

                case PaletteMode.Analogous:
                    {
                        double spread = 40.0 + rng.NextDouble() * 20.0; // 40..60 degrees total
                        double start = Wrap(h - spread * 0.5);
                        for (int i = 0; i < nColors; i++)
                        {
                            double t = nColors == 1 ? 0.5 : (double)i / (nColors - 1);
                            double hh = Wrap(start + t * spread);
                            double ss = Math.Max(clampSMin, Math.Min(clampSMax, s * (0.9 + rng.NextDouble() * 0.2)));
                            double vv = Math.Max(clampVMin, Math.Min(clampVMax, v * (0.85 + (t - 0.5) * 0.2)));
                            pal[i] = ColorUtils.ColorFromHSV(hh, ss, vv);
                        }
                    }
                    break;

                case PaletteMode.Complementary:
                    {
                        // blend from base toward hue+180 with shades
                        double comp = Wrap(h + 180.0);
                        for (int i = 0; i < nColors; i++)
                        {
                            double t = nColors == 1 ? 0.5 : (double)i / (nColors - 1);
                            // bias toward base in center and complement at edges
                            double hh = (t < 0.5) ? Wrap(h + (t * 2.0 - 1.0) * 25.0) : Wrap(comp + ((t - 0.5) * 2.0 - 1.0) * 25.0);
                            double ss = Math.Max(clampSMin, Math.Min(clampSMax, 0.6 + 0.4 * (1 - Math.Abs(0.5 - t))));
                            double vv = Math.Max(clampVMin, Math.Min(clampVMax, v * (0.7 + 0.6 * t)));
                            pal[i] = ColorUtils.ColorFromHSV(hh, ss, vv);
                        }
                    }
                    break;

                case PaletteMode.SplitComplementary:
                    {
                        double comp = Wrap(h + 180.0);
                        double off = 22 + rng.NextDouble() * 8; // 22..30 deg from complement
                        double c1 = Wrap(comp - off), c2 = Wrap(comp + off);
                        // distribute base + the two split complement colors into palette
                        for (int i = 0; i < nColors; i++)
                        {
                            double t = (double)i / Math.Max(1, nColors - 1);
                            double hh = t < 0.5 ? Wrap(h + (t - 0.25) * 40) : (t < 0.75 ? c1 : c2);
                            pal[i] = ColorUtils.ColorFromHSV(hh, Math.Max(clampSMin, s * 0.9), Math.Min(clampVMax, v * (0.85 + 0.2 * t)));
                        }
                    }
                    break;

                case PaletteMode.Triadic:
                    {
                        // 3 roots at h, h+120, h+240; interpolate shades per root
                        double[] roots = new[] { h, Wrap(h + 120), Wrap(h + 240) };
                        for (int i = 0; i < nColors; i++)
                        {
                            double t = (double)i / Math.Max(1, nColors - 1);
                            // pick which root proportional to t
                            double which = t * roots.Length;
                            int r0 = Math.Min(roots.Length - 1, (int)Math.Floor(which));
                            double localT = which - r0;
                            double hh = Wrap(roots[r0] + (localT - 0.5) * 12.0);
                            double ss = Math.Max(clampSMin, Math.Min(clampSMax, s * (0.9 + 0.2 * (r0 % 2))));
                            double vv = Math.Max(clampVMin, Math.Min(clampVMax, v * (0.8 + 0.25 * (r0))));
                            pal[i] = ColorUtils.ColorFromHSV(hh, ss, vv);
                        }
                    }
                    break;

                case PaletteMode.TwoToneRandom:
                    {
                        // pick a second hue at moderate distance (60..140 deg) for variety
                        double hue2 = Wrap(h + (rng.NextDouble() * 80 + 60) * (rng.NextDouble() < 0.5 ? 1 : -1));
                        for (int i = 0; i < nColors; i++)
                        {
                            double t = (double)i / Math.Max(1, nColors - 1);
                            double hh = Wrap((1 - t) * h + t * hue2);
                            double ss = Math.Max(clampSMin, Math.Min(clampSMax, s * (0.7 + 0.6 * (1 - Math.Abs(0.5 - t)))));
                            double vv = Math.Max(clampVMin, Math.Min(clampVMax, v * (0.6 + 0.8 * t)));
                            pal[i] = ColorUtils.ColorFromHSV(hh, ss, vv);
                        }
                    }
                    break;

                case PaletteMode.SoftStripes:
                    {
                        // alternating small hue shifts for stripe-like bands
                        double stripeSpread = 22.0;
                        for (int i = 0; i < nColors; i++)
                        {
                            double t = (double)i / Math.Max(1, nColors - 1);
                            double hh = Wrap(h + Math.Sin(i * 2.2) * stripeSpread);
                            pal[i] = ColorUtils.ColorFromHSV(hh, Math.Max(clampSMin, s * 0.85), Math.Max(clampVMin, v * (0.7 + 0.3 * t)));
                        }
                    }
                    break;

                default:
                    // fallback: monochrome
                    return BuildPaletteFromMode(baseColor, PaletteMode.Monochrome, nColors, seed);
            }

            return pal;
        }

        public static void AddInternalPatterns(Image<Rgba32> bmp, bool[,] mask, Rgba32 patternColor)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            if (RNG.Rand.NextDouble() < 0.5)
            {
                int spots = RNG.Rand.Next(2, 6);
                for (int s = 0; s < spots; s++)
                {
                    int cx = RNG.Rand.Next(w);
                    int cy = RNG.Rand.Next(h);
                    int r = RNG.Rand.Next(1, 3);
                    if (PixelUtils.IsPointInMask(mask, cx, cy))
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, r, patternColor);
                }
            }
            else
            {
                bool horizontal = RNG.Rand.NextDouble() < 0.5;
                int lines = RNG.Rand.Next(2, 5);
                for (int l = 0; l < lines; l++)
                {
                    int off = RNG.Rand.Next(h);
                    for (int x = 0; x < w; x++)
                    {
                        int y = horizontal ? off + l * 2 : (off + x / 3 + l * 2) % h;
                        if (y >= 0 && y < h && mask[x, y])
                            PixelUtils.SafeSetPixel(bmp, x, y, patternColor);
                    }
                }
            }
        }

        public static void AddEyes(Image<Rgba32> bmp, bool[,] mask, Rgba32 accent)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            Point headCenter = EstimateHeadCenter(mask, h / 3);
            if (headCenter.IsEmpty) return;

            int eyeCount = RNG.Rand.Next(1, 4);
            int spacing = Math.Max(2, w / 10);
            int startX = headCenter.X - spacing * (eyeCount - 1) / 2;
            int eyeY = headCenter.Y;

            int style = RNG.Rand.Next(0, 6);

            for (int i = 0; i < eyeCount; i++)
            {
                int ex = startX + i * spacing;
                Point place = PixelUtils.FindNearestMaskPoint(mask, ex, eyeY, 6);
                if (place.IsEmpty) continue;
                ex = place.X; eyeY = place.Y;

                int r = Math.Max(1, w / 24);

                switch (style)
                {
                    case 0:
                        PixelUtils.FillCircle(bmp, ex, eyeY, r + 1, Rgba32.ParseHex("#FFFFFFFF"));
                        PixelUtils.SafeSetPixel(bmp, ex, eyeY, Rgba32.ParseHex("#00000000"));
                        break;
                    case 1:
                        PixelUtils.FillCircle(bmp, ex, eyeY, r + 1, accent);
                        PixelUtils.SafeSetPixel(bmp, ex, eyeY, Rgba32.ParseHex("#00000000"));
                        PixelUtils.SafeSetPixel(bmp, ex - 1, eyeY - 1, Rgba32.ParseHex("#FFFFFFFF"));
                        break;
                    case 2:
                        PixelUtils.FillCircle(bmp, ex, eyeY, r + 1, accent);
                        for (int dy = -r; dy <= r; dy++)
                            PixelUtils.SafeSetPixel(bmp, ex, eyeY + dy, Rgba32.ParseHex("#00000000"));
                        break;
                    case 3:
                        PixelUtils.FillCircle(bmp, ex, eyeY, r + 1, accent);
                        for (int dx = -r; dx <= r; dx++)
                            PixelUtils.SafeSetPixel(bmp, ex + dx, eyeY, Rgba32.ParseHex("#00000000"));
                        break;
                    case 4:
                        PixelUtils.FillCircle(bmp, ex, eyeY, r + 1, Rgba32.ParseHex("#FFFFFFFF"));
                        PixelUtils.SafeSetPixel(bmp, ex, eyeY, Rgba32.ParseHex("#00000000"));
                        PixelUtils.SafeSetPixel(bmp, ex - 1, eyeY - 2, ColorUtils.Darken(Rgba32.ParseHex("#FFFFFFFF"), 0.6f));
                        PixelUtils.SafeSetPixel(bmp, ex, eyeY - 2, ColorUtils.Darken(Rgba32.ParseHex("#FFFFFFFF"), 0.6f));
                        break;
                    case 5:
                        PixelUtils.FillCircle(bmp, ex, eyeY, r + 1, accent);
                        PixelUtils.SafeSetPixel(bmp, ex, eyeY, Rgba32.ParseHex("#00000000"));
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dy = -1; dy <= 1; dy++)
                                if (Math.Abs(dx) + Math.Abs(dy) == 1 && PixelUtils.IsPointInMask(mask, ex + dx, eyeY + dy))
                                    PixelUtils.SafeSetPixel(bmp, ex + dx, eyeY + dy, ColorUtils.Lighten(accent, 0.5f));
                        break;
                    default:
                        PixelUtils.FillCircle(bmp, ex, eyeY, r + 1, Rgba32.ParseHex("#FFFFFFFF"));
                        PixelUtils.SafeSetPixel(bmp, ex, eyeY, Rgba32.ParseHex("#00000000"));
                        break;
                }
            }
        }

        public static void AddMouth(Image<Rgba32> bmp, bool[,] mask)
        {
            Point headCenter = EstimateHeadCenter(mask, mask.GetLength(1) / 3);
            if (headCenter.IsEmpty) return;
            int mx = headCenter.X, my = headCenter.Y + 3;
            PixelUtils.SafeSetPixel(bmp, mx - 1, my, Rgba32.ParseHex("#00000000"));
            PixelUtils.SafeSetPixel(bmp, mx, my, Rgba32.ParseHex("#00000000"));
            PixelUtils.SafeSetPixel(bmp, mx + 1, my, Rgba32.ParseHex("#00000000"));
        }

        public static void AddAnchoredLimbs(Image<Rgba32> bmp, bool[,] mask, Rgba32 limbColor, int margin)
        {
            var edges = PixelUtils.GetMaskEdgePoints(mask);
            if (edges.Count == 0) return;
            int w = mask.GetLength(0), h = mask.GetLength(1);
            Point leftArm = PixelUtils.FindEdgeNear(edges, w / 4, h / 3);
            Point rightArm = PixelUtils.FindEdgeNear(edges, 3 * w / 4, h / 3);
            Point leftLeg = PixelUtils.FindEdgeNear(edges, w / 4, (int)(h * 0.85));
            Point rightLeg = PixelUtils.FindEdgeNear(edges, 3 * w / 4, (int)(h * 0.85));

            if (!leftArm.IsEmpty) DrawLimbWithMask(bmp, mask, leftArm.X, leftArm.Y, -1, 1, RNG.Rand.Next(3, 6), limbColor, margin);
            if (!rightArm.IsEmpty) DrawLimbWithMask(bmp, mask, rightArm.X, rightArm.Y, 1, 1, RNG.Rand.Next(3, 6), limbColor, margin);
            if (!leftLeg.IsEmpty) DrawLimbWithMask(bmp, mask, leftLeg.X, leftLeg.Y, -1, 2, RNG.Rand.Next(3, 6), limbColor, margin);
            if (!rightLeg.IsEmpty) DrawLimbWithMask(bmp, mask, rightLeg.X, rightLeg.Y, 1, 2, RNG.Rand.Next(3, 6), limbColor, margin);
        }

        static void DrawLimbWithMask(Image<Rgba32> bmp, bool[,] mask, int sx, int sy, int dirX, int dirY, int length, Rgba32 color, int margin)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            int x = sx, y = sy;
            for (int i = 0; i < length; i++)
            {
                x += dirX; y += dirY;
                if (x < margin || x >= w - margin || y < margin || y >= h - margin) break;
                if (x >= 0 && y >= 0 && x < w && y < h) mask[x, y] = true;
                PixelUtils.SafeSetPixel(bmp, x, y, color);
                PixelUtils.SafeSetPixel(bmp, x + (dirX == 0 ? 1 : 0), y, color);
                PixelUtils.SafeSetPixel(bmp, x, y + 1, color);
            }
        }

        public static void DrawMaskOutline(Image<Rgba32> bmp, bool[,] mask, Rgba32 outlineColor)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            var outer = new List<Point>();
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (mask[x, y] && IsEdgeMask(mask, x, y))
                        outer.Add(new Point(x, y));

            foreach (var p in outer)
            {
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int nx = p.X + dx, ny = p.Y + dy;
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h && !mask[nx, ny])
                            PixelUtils.SafeSetPixel(bmp, nx, ny, outlineColor);
                    }
                Rgba32 cur = bmp[p.X, p.Y];
                PixelUtils.SafeSetPixel(bmp, p.X, p.Y, ColorUtils.Darken(cur, 0.75f));
            }
        }

        static bool IsEdgeMask(bool[,] mask, int x, int y)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            if (!mask[x, y]) return false;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) return true;
                    if (!mask[nx, ny]) return true;
                }
            return false;
        }

        static Point EstimateHeadCenter(bool[,] mask, int maxY)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            int sumX = 0, sumY = 0, count = 0;

            for (int x = 0; x < w; x++)
                for (int y = 0; y < Math.Min(h, maxY); y++)
                    if (mask[x, y])
                    {
                        sumX += x;
                        sumY += y;
                        count++;
                    }

            if (count == 0) return Point.Empty;
            return new Point(sumX / count, sumY / count);
        }
    }
}
