using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AutoSpriteCreator
{
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

    public enum LimbStyle
    {
        Simple,     // thicker single stroke with slight jitter
        Thick,      // chunky tapering limb
        Segmented,  // series of overlapping rounded segments (like armor/sausages)
        Tentacle,   // wavy/tapering limb
        Jointed,    // two-segment limb with elbow/knee
        Paw,         // short stubby leg with rounded foot (good for legs)
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
            if (mode == PaletteMode.Random)
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

        // ---------- Backwards-compatible overload (keeps existing call sites working) ----------
        // old signature: (Image limbImg, bool[,] bodyMask, Rgba32 limbColor, int margin)
        // This overload infers a palette and forwards to the rich overload.
        public static void AddAnchoredLimbs(Image<Rgba32> limbImg, bool[,] bodyMask, Rgba32 limbAccentColor, int margin, LimbStyle? forcedStyle = null)
        {
            // derive reasonable palette pieces when caller only provided a single accent color
            var baseColor = ColorUtils.Darken(limbAccentColor, 0.10f);    // base slightly darker than accent
            var patternColor = ColorUtils.Darken(baseColor, 0.55f);
            var outlineColor = ColorUtils.Darken(baseColor, 0.34f);
            int nColors = 3;
            PaletteMode mode = PaletteMode.TwoToneRandom;

            AddAnchoredLimbs(limbImg, bodyMask, baseColor, limbAccentColor, patternColor, outlineColor, nColors, margin, mode, drawPatterns: true, forcedStyle: forcedStyle);
        }

        // ---------- Rich overload (use this from GenerateParts to match body palette exactly) ----------
        public static void AddAnchoredLimbs(
            Image<Rgba32> limbImg,
            bool[,] bodyMask,
            Rgba32 baseColor,
            Rgba32 accentColor,
            Rgba32 patternColor,
            Rgba32 outlineColor,
            int nColors,
            int margin,
            PaletteMode paletteMode,
            bool drawPatterns = true,
            LimbStyle? forcedStyle = null)
        {
            // build limbMask first (limb-only pixels)
            int w = bodyMask.GetLength(0), h = bodyMask.GetLength(1);
            bool[,] limbMask = new bool[w, h];

            // find reasonable anchor points on the body edges
            var edges = PixelUtils.GetMaskEdgePoints(bodyMask);
            if (edges.Count == 0) return;

            Point leftArm = PixelUtils.FindEdgeNear(edges, w / 4, h / 3);
            Point rightArm = PixelUtils.FindEdgeNear(edges, 3 * w / 4, h / 3);
            Point leftLeg = PixelUtils.FindEdgeNear(edges, w / 4, (int)(h * 0.85));
            Point rightLeg = PixelUtils.FindEdgeNear(edges, 3 * w / 4, (int)(h * 0.85));

            // scale lengths with sprite size
            int baseLen = Math.Max(3, Math.Max(w, h) / 6);
            int armLenMin = Math.Max(2, baseLen - 1);
            int armLenMax = Math.Max(3, baseLen + 2);
            int legLenMin = Math.Max(3, baseLen);
            int legLenMax = Math.Max(4, baseLen + 4);

            if (forcedStyle == LimbStyle.Random)
                forcedStyle = (LimbStyle)(RNG.Rand.Next() % (Enum.GetValues(typeof(LimbStyle)).Length - 1));

            // draw limb shapes into limbMask (no coloring yet)
            if (!leftArm.IsEmpty) DrawAdvancedLimbMask(limbMask, leftArm.X, leftArm.Y, -1, 1, RNG.Rand.Next(armLenMin, armLenMax + 1), margin, forcedStyle ?? RandomLimbStyle(isLeg: false), isLeft: true, isLeg: false);
            if (!rightArm.IsEmpty) DrawAdvancedLimbMask(limbMask, rightArm.X, rightArm.Y, 1, 1, RNG.Rand.Next(armLenMin, armLenMax + 1), margin, forcedStyle ?? RandomLimbStyle(isLeg: false), isLeft: false, isLeg: false);
            if (!leftLeg.IsEmpty) DrawAdvancedLimbMask(limbMask, leftLeg.X, leftLeg.Y, -1, 2, RNG.Rand.Next(legLenMin, legLenMax + 1), margin, forcedStyle ?? RandomLimbStyle(isLeg: true), isLeft: true, isLeg: true);
            if (!rightLeg.IsEmpty) DrawAdvancedLimbMask(limbMask, rightLeg.X, rightLeg.Y, 1, 2, RNG.Rand.Next(legLenMin, legLenMax + 1), margin, forcedStyle ?? RandomLimbStyle(isLeg: true), isLeft: false, isLeg: true);

            // If no limb pixels were drawn, exit
            bool any = false;
            for (int x = 0; x < w && !any; x++)
                for (int y = 0; y < h && !any; y++)
                    if (limbMask[x, y]) any = true;
            if (!any) return;

            // Now color the limb image with the same noise/palette code used by body/head
            // Outline = false for palette step: we'll draw outline after adding patterns
            ApplyNoisePalette(limbImg, limbMask, baseColor, accentColor, nColors: nColors, outline: false, mode: paletteMode);

            if (drawPatterns)
            {
                AddInternalPatterns(limbImg, limbMask, patternColor);
            }

            // Finally draw the outline so limbs have the same edge treatment as the body
            DrawMaskOutline(limbImg, limbMask, outlineColor);

            // Done — limbImg now contains properly-colored + outlined limb pixels on transparent background.
        }

        // ---------- Helpers: mask-painting primitives (operates on boolean masks only) ----------
        static void SetMaskCircle(bool[,] mask, int cx, int cy, int radius, int margin)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            if (radius <= 0)
            {
                if (cx >= margin && cx < w - margin && cy >= margin && cy < h - margin)
                    mask[cx, cy] = true;
                return;
            }

            int xmin = Math.Max(cx - radius, margin);
            int xmax = Math.Min(cx + radius, w - 1 - margin);
            int ymin = Math.Max(cy - radius, margin);
            int ymax = Math.Min(cy + radius, h - 1 - margin);
            int r2 = radius * radius;

            for (int x = xmin; x <= xmax; x++)
                for (int y = ymin; y <= ymax; y++)
                {
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy <= r2) mask[x, y] = true;
                }
        }

        static void SetMaskLine(bool[,] mask, int x0, int y0, int x1, int y1, int margin)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                if (x0 >= margin && x0 < w - margin && y0 >= margin && y0 < h - margin) mask[x0, y0] = true;
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        // ---------- Draw limb shapes into mask (no coloring) ----------
        static void DrawAdvancedLimbMask(bool[,] mask, int sx, int sy, int dirX, int dirY, int length, int margin, LimbStyle style, bool isLeft, bool isLeg)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            if (length <= 0) return;

            switch (style)
            {
                case LimbStyle.Simple:
                    {
                        int thickness = RNG.Rand.Next(1, 3);
                        for (int i = 1; i <= length; i++)
                        {
                            int jitterX = RNG.Rand.Next(-1, 2);
                            int jitterY = RNG.Rand.Next(0, 2);
                            int x = sx + dirX * i + jitterX;
                            int y = sy + dirY * i + jitterY;
                            if (x < margin || x >= w - margin || y < margin || y >= h - margin) break;
                            SetMaskCircle(mask, x, y, thickness, margin);
                        }
                    }
                    break;

                case LimbStyle.Thick:
                    {
                        for (int i = 1; i <= length; i++)
                        {
                            double t = i / (double)length;
                            int radius = Math.Max(1, 2 - (int)Math.Floor(1.0 * t)); // taper 2 -> 1
                            int bend = (int)Math.Round(Math.Sin(i * 0.6 + RNG.Rand.NextDouble()) * (isLeft ? -1 : 1));
                            int x = sx + dirX * i + bend;
                            int y = sy + dirY * i + (int)Math.Round(t * (isLeg ? 1.2 : 0.6));
                            if (x < margin || x >= w - margin || y < margin || y >= h - margin) break;
                            SetMaskCircle(mask, x, y, radius, margin);
                        }

                        // foot/hand end
                        int ex = sx + dirX * length;
                        int ey = sy + dirY * length;
                        SetMaskCircle(mask, ex, ey, isLeg ? 2 : 1, margin);
                    }
                    break;

                case LimbStyle.Segmented:
                    {
                        int segments = Math.Max(2, Math.Min(length, RNG.Rand.Next(2, 1 + Math.Max(2, length / 2))));
                        var centers = new List<Point>();
                        for (int s = 0; s < segments; s++)
                        {
                            double t = (s + 0.5) / segments;
                            int cx = sx + (int)Math.Round(dirX * t * length + RNG.Rand.Next(-1, 2));
                            int cy = sy + (int)Math.Round(dirY * t * length + (isLeg ? Math.Abs(t - 0.5) * 1.5 : Math.Sin(t * Math.PI) * 1.2));
                            centers.Add(new Point(cx, cy));
                            int segRadius = Math.Max(1, (int)Math.Round(2.2 * (1.0 - t * 0.6)));
                            SetMaskCircle(mask, cx, cy, segRadius, margin);
                        }

                        // connectors
                        for (int s = 0; s < centers.Count - 1; s++)
                        {
                            SetMaskLine(mask, centers[s].X, centers[s].Y, centers[s + 1].X, centers[s + 1].Y, margin);
                        }
                    }
                    break;

                case LimbStyle.Tentacle:
                    {
                        double phase = RNG.Rand.NextDouble() * Math.PI * 2.0;
                        double amp = Math.Max(1.0, Math.Min(2.5, length * 0.12));
                        for (int i = 1; i <= length; i++)
                        {
                            double t = i / (double)length;
                            int offset = (int)Math.Round(Math.Sin(phase + t * Math.PI * 2.0) * amp * (isLeft ? -1 : 1));
                            int x = sx + dirX * i + offset;
                            int y = sy + dirY * i + (int)Math.Round(t * (isLeg ? 1.0 : 0.5));
                            if (x < margin || x >= w - margin || y < margin || y >= h - margin) break;
                            int radius = Math.Max(1, (int)Math.Round(1.4 * (1.0 - t * 0.95)));
                            SetMaskCircle(mask, x, y, radius, margin);

                            // suction-cup pixel occasionally
                            if (RNG.Rand.NextDouble() < 0.12 && i % 2 == 0)
                            {
                                int cupX = x + (isLeft ? 1 : -1);
                                int cupY = y;
                                if (cupX >= margin && cupX < w - margin && cupY >= margin && cupY < h - margin)
                                    mask[cupX, cupY] = true;
                            }
                        }
                    }
                    break;

                case LimbStyle.Jointed:
                    {
                        double midT = 0.4 + RNG.Rand.NextDouble() * 0.25;
                        int elbowX = sx + (int)Math.Round(dirX * length * midT) + RNG.Rand.Next(-1, 2);
                        int elbowY = sy + (int)Math.Round(dirY * length * midT) + (isLeg ? RNG.Rand.Next(0, 2) : RNG.Rand.Next(-1, 2));

                        // first segment
                        SetMaskLine(mask, sx, sy, elbowX, elbowY, margin);
                        SetMaskCircle(mask, elbowX, elbowY, 2, margin);

                        // second segment to end
                        int endX = sx + dirX * length + RNG.Rand.Next(-1, 2);
                        int endY = sy + dirY * length + RNG.Rand.Next(0, 2);
                        SetMaskLine(mask, elbowX, elbowY, endX, endY, margin);
                        SetMaskCircle(mask, endX, endY, 1, margin);

                        // optional fingers/claws for arms
                        if (!isLeg && RNG.Rand.NextDouble() < 0.6)
                        {
                            int clawCount = RNG.Rand.Next(1, 4);
                            for (int c = 0; c < clawCount; c++)
                            {
                                int cx = endX + (isLeft ? -1 : 1) * (1 + c);
                                int cy = endY + c / 2;
                                if (cx >= margin && cx < w - margin && cy >= margin && cy < h - margin)
                                    mask[cx, cy] = true;
                            }
                        }
                    }
                    break;

                case LimbStyle.Paw:
                    {
                        for (int i = 1; i <= Math.Max(1, length - 1); i++)
                        {
                            int x = sx + dirX * i;
                            int y = sy + dirY * i;
                            if (x < margin || x >= w - margin || y < margin || y >= h - margin) break;
                            SetMaskCircle(mask, x, y, 1, margin);
                        }
                        int footX = sx + dirX * length;
                        int footY = sy + dirY * length;
                        SetMaskCircle(mask, footX, footY, 2, margin);

                        int toeCount = RNG.Rand.Next(2, 4);
                        for (int t = 0; t < toeCount; t++)
                        {
                            int tx = footX + (t - toeCount / 2);
                            int ty = footY - 1;
                            if (tx >= margin && tx < w - margin && ty >= margin && ty < h - margin)
                                mask[tx, ty] = true;
                        }
                    }
                    break;

                default:
                    // fallback: a simple line
                    SetMaskLine(mask, sx + dirX, sy + dirY, sx + dirX * length, sy + dirY * length, margin);
                    break;
            }
        }

        // Small helper to pick random limb style when not forced
        static LimbStyle RandomLimbStyle(bool isLeg)
        {
            double r = RNG.Rand.NextDouble();
            if (isLeg)
            {
                if (r < 0.35) return LimbStyle.Paw;
                if (r < 0.65) return LimbStyle.Thick;
                if (r < 0.85) return LimbStyle.Jointed;
                return LimbStyle.Simple;
            }
            else
            {
                if (r < 0.18) return LimbStyle.Tentacle;
                if (r < 0.42) return LimbStyle.Segmented;
                if (r < 0.78) return LimbStyle.Jointed;
                return LimbStyle.Thick;
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
