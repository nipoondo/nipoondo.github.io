using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WebsiteBlazor.Classes;

namespace AutoSpriteGenerator
{
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
                mode = (PaletteMode)(DeterministicRandom.Next("ApplyNoisePalette", 0, 6));

            // Build main palette and optional head palette
            Rgba32[] palette = BuildPaletteFromMode(baseColor, mode, nColors, paletteSeed);
            Rgba32[] headPalette = null;
            if (headMask != null)
            {
                var hm = headMode ?? mode;
                // small seed difference so palettes are related but not identical
                headPalette = BuildPaletteFromMode(baseColor, hm, nColors, (paletteSeed ?? 0) + 12345);
            }

            var noise = new ValueNoise(DeterministicRandom.Next("ApplyNoisePalette"))
            {
                Octaves = 5,
                Period = 30.0,
                Persistence = 0.4,
                Lacunarity = 3.0
            };
            var noise2 = new ValueNoise(DeterministicRandom.Next("ApplyNoisePalette"))
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
                        double spread = 40.0 + DeterministicRandom.NextDouble("Analogous") * 20.0; // 40..60 degrees total
                        double start = Wrap(h - spread * 0.5);
                        for (int i = 0; i < nColors; i++)
                        {
                            double t = nColors == 1 ? 0.5 : (double)i / (nColors - 1);
                            double hh = Wrap(start + t * spread);
                            double ss = Math.Max(clampSMin, Math.Min(clampSMax, s * (0.9 + DeterministicRandom.NextDouble("Analogous") * 0.2)));
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
                        double off = 22 + DeterministicRandom.NextDouble("SplitComplementary") * 8; // 22..30 deg from complement
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
                        double hue2 = Wrap(h + (DeterministicRandom.NextDouble("TwoToneRandom") * 80 + 60) * (DeterministicRandom.NextDouble("TwoToneRandom") < 0.5 ? 1 : -1));
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
            if (DeterministicRandom.NextDouble("AddInternalPatterns") < 0.5)
            {
                int spots = DeterministicRandom.Next("AddInternalPatterns", 2, 6);
                for (int s = 0; s < spots; s++)
                {
                    int cx = DeterministicRandom.Next("AddInternalPatterns", w);
                    int cy = DeterministicRandom.Next("AddInternalPatterns", h);
                    int r = DeterministicRandom.Next("AddInternalPatterns", 1, 3);
                    if (PixelUtils.IsPointInMask(mask, cx, cy))
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, r, patternColor);
                }
            }
            else
            {
                bool horizontal = DeterministicRandom.NextDouble("AddInternalPatterns") < 0.5;
                int lines = DeterministicRandom.Next("AddInternalPatterns", 2, 5);
                for (int l = 0; l < lines; l++)
                {
                    int off = DeterministicRandom.Next("AddInternalPatterns", h);
                    for (int x = 0; x < w; x++)
                    {
                        int y = horizontal ? off + l * 2 : (off + x / 3 + l * 2) % h;
                        if (y >= 0 && y < h && mask[x, y])
                            PixelUtils.SafeSetPixel(bmp, x, y, patternColor);
                    }
                }
            }
        }

        /// <summary>
        /// Draws cute eyes. If eyeCountOverride is provided, that many eyes will be drawn (clamped to 1..6).
        /// If null, a small random count is chosen based on head size.
        /// eyeStyle can be EyeStyle.Random to randomize per-eye, or a specific EyeStyle.
        /// headMask should be supplied if you have a dedicated head mask; when provided, positions and bounding
        /// box are anchored to that mask so eye placement follows your head edits exactly.
        /// </summary>
        public static void AddEyes(Image<Rgba32> bmp, Rgba32 accent, Settings settings, bool[,] headMask = null)
        {
            int w = headMask.GetLength(0), h = headMask.GetLength(1);

            // Determine head centroid and bounding box. If headMask is provided, anchor to it exactly.
            Point headCenter = Point.Empty;
            int left = w, right = -1, top = h, bottom = -1;

            if (headMask != null)
            {
                headCenter = EstimateHeadCenterFromMask(headMask);
                for (int x = 0; x < w; x++)
                    for (int y = 0; y < h; y++)
                        if (headMask[x, y])
                        {
                            if (x < left) left = x;
                            if (x > right) right = x;
                            if (y < top) top = y;
                            if (y > bottom) bottom = y;
                        }
            }

            if (right < left || bottom < top)
            {
                // make a small default head around center
                if (headCenter.IsEmpty) headCenter = new Point(w / 2, Math.Max(0, h / 6));
                left = Math.Max(0, headCenter.X - Math.Max(2, w / 12));
                right = Math.Min(w - 1, headCenter.X + Math.Max(2, w / 12));
                top = Math.Max(0, headCenter.Y - Math.Max(1, h / 24));
                bottom = Math.Min(h - 1, headCenter.Y + Math.Max(1, h / 24));
            }

            int headWidth = Math.Max(1, right - left + 1);
            int headHeight = Math.Max(1, bottom - top + 1);

            // decide eye count
            int eyeCount = 0;
            if (settings.eyeCount.HasValue) 
            { 
                if(settings.eyeCount > 0 && settings.eyeCount < 7)
                    eyeCount = (int)settings.eyeCount;
            }
            if (eyeCount == 0) 
            {
                // heuristics: small heads -> 1, medium -> 1-2, large -> 2-4
                if (headWidth < w * 0.15) eyeCount = 1;
                else if (headWidth < w * 0.28) eyeCount = DeterministicRandom.Next("AddEyes", 1, 3); // 1..2
                else eyeCount = DeterministicRandom.Next("AddEyes", 1, Math.Min(4, Math.Max(2, headWidth / Math.Max(1, w / 8))) + 1);
            }

            // special logic for two eyes:
            // - sometimes place them on the sides (current behavior),
            // - sometimes place them front-facing (near headCenter) so they look like human eyes.
            // The choice is random but biased by how centered the head is.
            bool preferFrontal = false;
            if (eyeCount == 2)
            {
                // normalized head center in [0..1] relative to head bounding box
                double centerNorm = 0.5;
                if (!headCenter.IsEmpty) centerNorm = (headCenter.X - left) / (double)Math.Max(1, headWidth);
                // dist 0 => perfect center, 1 => far edge
                double dist = Math.Abs(centerNorm - 0.5) / 0.5;
                // frontal probability increases when head center is near bounding-box center.
                // Range roughly: 25% (very lopsided head) .. 95% (centered head).
                double frontalProbability = 0.25 + (1.0 - dist) * 0.4; // WAS 0.25 + ...
                preferFrontal = DeterministicRandom.NextDouble("AddEyes") < frontalProbability;
            }

            // compute candidate x positions evenly across head width (anchored to headMask bounding box)
            int usableWidth = Math.Max(1, headWidth - 2);
            double spacing = eyeCount > 1 ? (double)(usableWidth) / (eyeCount - 1) : 0.0;
            var candidates = new List<Point>();
            bool[,] searchMask = headMask;

            // helper: try adding a candidate point found near base coords
            void TryAddCandidate(int baseX, int baseY, int radius)
            {
                baseX = Math.Clamp(baseX, 0, w - 1);
                baseY = Math.Clamp(baseY, 0, h - 1);
                var p = PixelUtils.FindNearestMaskPoint(searchMask, baseX, baseY, radius);
                if (!p.IsEmpty) candidates.Add(p);
            }

            if (eyeCount == 2 && preferFrontal)
            {
                // frontal placement: both eyes near the head center (left/right of centroid)
                int offset = Math.Max(1, headWidth / 6); // how far from center to place each eye
                int baseY = top + Math.Max(1, (int)Math.Round(headHeight * 0.35));
                int radius = Math.Max(2, Math.Min(headHeight, Math.Max(3, headWidth / 6)));

                TryAddCandidate(headCenter.X - offset + DeterministicRandom.Next("AddEyes", -1, 2), baseY + DeterministicRandom.Next("AddEyes", -1, 2), radius);
                TryAddCandidate(headCenter.X + offset + DeterministicRandom.Next("AddEyes", -1, 2), baseY + DeterministicRandom.Next("AddEyes", -1, 2), radius);

                // If frontal search failed to find both eyes, fall back to side/spacing approach below.
                if (candidates.Count < 2)
                {
                    candidates.Clear();
                    for (int i = 0; i < eyeCount; i++)
                    {
                        int baseX = eyeCount == 1 ? headCenter.X : left + (int)Math.Round(i * spacing);
                        int jitterX = DeterministicRandom.Next("AddEyes", -1, 2);
                        int ex = Math.Clamp(baseX + jitterX, 0, w - 1);
                        int baseY2 = top + Math.Max(1, (int)Math.Round(headHeight * 0.35)) + DeterministicRandom.Next("AddEyes", -1, 2);
                        int radius2 = Math.Max(2, Math.Min(headHeight, Math.Max(3, headWidth / 6)));
                        TryAddCandidate(ex, baseY2, radius2);
                    }
                }
            }
            else
            {
                // default spacing / side placement
                for (int i = 0; i < eyeCount; i++)
                {
                    int baseX = eyeCount == 1 ? headCenter.X : left + (int)Math.Round(i * spacing);
                    int jitterX = DeterministicRandom.Next("AddEyes", -1, 2);
                    int ex = Math.Clamp(baseX + jitterX, 0, w - 1);
                    // prefer placing eyes slightly above the vertical midline of the head box for cuteness
                    int baseY = top + Math.Max(1, (int)Math.Round(headHeight * 0.35)) + DeterministicRandom.Next("AddEyes", -1, 2);
                    // search radius - relative to head size
                    int radius = Math.Max(2, Math.Min(headHeight, Math.Max(3, headWidth / 6)));

                    TryAddCandidate(ex, baseY, radius);
                }
            }

            // If we still have no candidates (rare), try a final broad search centered on the head center.
            if (candidates.Count == 0)
            {
                int fallbackRadius = Math.Max(2, headWidth / 3);
                TryAddCandidate(headCenter.X, top + Math.Max(1, headHeight / 3), fallbackRadius);
            }

            if (candidates.Count == 0) return;

            // remove candidates that are too close to each other (simple greedy)
            candidates.Sort((a, b) => a.X.CompareTo(b.X));
            var filtered = new List<Point>();
            int minDist2 = (int)Math.Pow(Math.Max(1, w / 30), 2);
            foreach (var p in candidates)
            {
                bool ok = true;
                foreach (var q in filtered)
                {
                    int dx = p.X - q.X;
                    int dy = p.Y - q.Y;
                    if (dx * dx + dy * dy < minDist2) { ok = false; break; }
                }
                if (ok) filtered.Add(p);
            }

            if (filtered.Count == 0) return;

            // draw each eye — size scales with head width but kept within readable bounds
            // determine style for eyes
            EyeStyle styleToUse = settings.eyeStyle;
            if (styleToUse == EyeStyle.Random)
            {
                // pick a random non-Random style
                var vals = Enum.GetValues(typeof(EyeStyle));
                styleToUse = (EyeStyle)vals.GetValue(DeterministicRandom.Next("AddEyes", 1, vals.Length));
            }

            foreach (var p in filtered)
            {
                int r = Math.Max(1, Math.Clamp(headWidth / (6 + eyeCount), w / 32, w / 12));

                DrawCuteEye(bmp, headMask, p.X, p.Y, r, accent, styleToUse);
            }
        }

        static void DrawCuteEye(Image<Rgba32> bmp, bool[,] mask, int cx, int cy, int r, Rgba32 accent, EyeStyle style)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            if (cx < 0 || cy < 0 || cx >= w || cy >= h) return;

            Rgba32 white = Rgba32.ParseHex("#FFFFFFFF");
            Rgba32 black = Rgba32.ParseHex("#000000FF");
            Rgba32 irisBase = ColorUtils.Lighten(accent, 0.22f);
            Rgba32 irisDark = ColorUtils.Darken(accent, 0.35f);
            Rgba32 outline = ColorUtils.Darken(accent, 0.45f);

            // Helper key for hashset
            long Key(int x, int y) => ((long)x << 32) | (uint)y;

            // We'll collect the exact pixels where sclera was drawn so outline can attach only to them.
            var scleraPixels = new HashSet<long>();

            // Collect mask-visible pixels inside a circle radius rr (used after we draw sclera)
            void CollectSclera(int rr)
            {
                scleraPixels.Clear();
                int rr2 = rr * rr;
                for (int dx = -rr; dx <= rr; dx++)
                    for (int dy = -rr; dy <= rr; dy++)
                    {
                        int px = cx + dx, py = cy + dy;
                        if (px < 0 || py < 0 || px >= w || py >= h) continue;
                        if (dx * dx + dy * dy <= rr2 && PixelUtils.IsPointInMask(mask, px, py))
                            scleraPixels.Add(Key(px, py));
                    }
            }

            // Draw outline only on pixels that are outside mask and adjacent to any sclera pixel.
            void DrawOutlineAttachedToSclera()
            {
                if (scleraPixels.Count == 0) return;
                var written = new HashSet<long>();
                foreach (var k in scleraPixels)
                {
                    int sx = (int)(k >> 32);
                    int sy = (int)(k & 0xffffffff);
                    for (int ox = -1; ox <= 1; ox++)
                        for (int oy = -1; oy <= 1; oy++)
                        {
                            if (ox == 0 && oy == 0) continue;
                            int nx = sx + ox, ny = sy + oy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            long nk = Key(nx, ny);
                            if (written.Contains(nk)) continue;
                            // only draw outline outside the mask
                            if (!PixelUtils.IsPointInMask(mask, nx, ny))
                            {
                                PixelUtils.SafeSetPixel(bmp, nx, ny, outline);
                                written.Add(nk);
                            }
                        }
                }
            }

            switch (style)
            {
                case EyeStyle.BigSparkle:
                    {
                        int sclR = r + 1;
                        // draw sclera clipped
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, sclR, white);
                        CollectSclera(sclR);
                        DrawOutlineAttachedToSclera();

                        int ri = Math.Max(1, (int)Math.Round(r * 0.75));
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, ri, ColorUtils.Lighten(irisBase, 0.12f));
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, Math.Max(1, ri - 1), irisDark);

                        int rp = Math.Max(1, (int)Math.Round(ri * 0.4));
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, rp, black);

                        int hx = cx - Math.Max(1, ri / 2);
                        int hy = cy - Math.Max(1, ri / 2);
                        if (hx >= 0 && hy >= 0 && hx < w && hy < h && PixelUtils.IsPointInMask(mask, hx, hy))
                        {
                            PixelUtils.SafeSetPixel(bmp, hx, hy, white);
                            if (hx + 1 < w && PixelUtils.IsPointInMask(mask, hx + 1, hy)) PixelUtils.SafeSetPixel(bmp, hx + 1, hy, white);
                            if (hy + 1 < h && PixelUtils.IsPointInMask(mask, hx, hy + 1)) PixelUtils.SafeSetPixel(bmp, hx, hy + 1, white);
                        }
                    }
                    break;

                case EyeStyle.WideIris:
                    {
                        int sclR = r + 1;
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, sclR, white);
                        CollectSclera(sclR);
                        DrawOutlineAttachedToSclera();

                        int ri = Math.Max(1, (int)Math.Round(r * 0.9));
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, ri, irisBase);
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, Math.Max(1, ri - 1), irisDark);
                        int rp = Math.Max(1, (int)Math.Round(ri * 0.45));
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, rp, black);

                        int hx = cx - Math.Max(1, ri / 2);
                        int hy = cy - Math.Max(1, ri / 3);
                        if (hx >= 0 && hy >= 0 && hx < w && hy < h && PixelUtils.IsPointInMask(mask, hx, hy)) PixelUtils.SafeSetPixel(bmp, hx, hy, white);
                        if (hx + 1 < w && hy + 1 < h && PixelUtils.IsPointInMask(mask, hx + 1, hy + 1)) PixelUtils.SafeSetPixel(bmp, hx + 1, hy + 1, white);
                    }
                    break;

                case EyeStyle.Almond:
                    {
                        int rA = Math.Max(1, (int)Math.Round(r * 0.8));
                        PixelUtils.FillCircleClipped(bmp, mask, cx - 1, cy, rA, white);
                        PixelUtils.FillCircleClipped(bmp, mask, cx + 1, cy, rA, white);

                        // collect union of both white circles
                        scleraPixels.Clear();
                        int rr2 = rA * rA;
                        for (int dx = -rA; dx <= rA; dx++)
                            for (int dy = -rA; dy <= rA; dy++)
                            {
                                int px = cx - 1 + dx, py = cy + dy;
                                if (px >= 0 && py >= 0 && px < w && py < h && dx * dx + dy * dy <= rr2 && PixelUtils.IsPointInMask(mask, px, py))
                                    scleraPixels.Add(Key(px, py));
                                px = cx + 1 + dx;
                                if (px >= 0 && py >= 0 && px < w && py < h && dx * dx + dy * dy <= rr2 && PixelUtils.IsPointInMask(mask, px, py))
                                    scleraPixels.Add(Key(px, py));
                            }
                        DrawOutlineAttachedToSclera();

                        int ri = Math.Max(1, (int)Math.Round(r * 0.55));
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, ri, irisBase);
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, Math.Max(1, ri - 1), irisDark);
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, Math.Max(1, (int)Math.Round(ri * 0.45)), black);

                        int lashY = cy - Math.Max(1, r / 2);
                        for (int dx = -ri; dx <= ri; dx++)
                        {
                            int px = cx + dx;
                            if (px >= 0 && px < w && lashY >= 0 && lashY < h && PixelUtils.IsPointInMask(mask, px, lashY))
                                PixelUtils.SafeSetPixel(bmp, px, lashY, ColorUtils.Darken(white, 0.9f));
                        }
                    }
                    break;

                case EyeStyle.Sleepy:
                    {
                        // IMPORTANT: do NOT draw a full white sclera here.
                        int ri = Math.Max(1, (int)Math.Round(r * 0.5));
                        // small visible iris (clipped) and pupil
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy + 1, ri, irisBase);
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy + 1, Math.Max(1, (int)Math.Round(ri * 0.4)), black);

                        // lid pixels (only drawn where inside the mask) to create half-closed look
                        int lidTop = cy - (int)Math.Round(r * 0.3);
                        for (int dx = -ri - 1; dx <= ri + 1; dx++)
                        {
                            int px = cx + dx;
                            if (px < 0 || px >= w || lidTop < 0 || lidTop >= h) continue;
                            if (PixelUtils.IsPointInMask(mask, px, lidTop))
                                PixelUtils.SafeSetPixel(bmp, px, lidTop, ColorUtils.Darken(white, 0.88f));
                        }
                    }
                    break;

                case EyeStyle.Winking:
                    {
                        // IMPORTANT: do NOT draw a full white sclera for wink.
                        // Draw a small curved line / arc inside mask to represent the closed eye.
                        var winkColor = ColorUtils.Darken(white, 0.88f);
                        int ax = cx - 1, ay = cy;
                        if (ax >= 0 && ay >= 0 && ax < w && ay < h && PixelUtils.IsPointInMask(mask, ax, ay)) PixelUtils.SafeSetPixel(bmp, ax, ay, winkColor);
                        if (cx >= 0 && cy + 1 >= 0 && cx < w && cy + 1 < h && PixelUtils.IsPointInMask(mask, cx, cy + 1)) PixelUtils.SafeSetPixel(bmp, cx, cy + 1, winkColor);
                        if (cx + 1 >= 0 && cy >= 0 && cx + 1 < w && cy < h && PixelUtils.IsPointInMask(mask, cx + 1, cy)) PixelUtils.SafeSetPixel(bmp, cx + 1, cy, winkColor);
                    }
                    break;

                case EyeStyle.Button:
                    {
                        int smallR = Math.Max(1, (int)Math.Round(r * 0.35));
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, smallR, black);
                        if (cx - 1 >= 0 && cy - 1 >= 0 && PixelUtils.IsPointInMask(mask, cx - 1, cy - 1)) PixelUtils.SafeSetPixel(bmp, cx - 1, cy - 1, white);
                    }
                    break;

                default:
                    {
                        // fallback: draw regular-eye with sclera and outline attached to it
                        int sclR = r + 1;
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, sclR, white);
                        CollectSclera(sclR);
                        DrawOutlineAttachedToSclera();

                        int ri = Math.Max(1, (int)Math.Round(r * 0.7));
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, ri, irisBase);
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, Math.Max(1, ri - 1), irisDark);
                        PixelUtils.FillCircleClipped(bmp, mask, cx, cy, Math.Max(1, (int)Math.Round(ri * 0.45)), black);
                    }
                    break;
            }
        }

        // compute centroid from a dedicated head mask
        private static Point EstimateHeadCenterFromMask(bool[,] headMask)
        {
            if (headMask == null) return Point.Empty;
            int w = headMask.GetLength(0), h = headMask.GetLength(1);
            int sumX = 0, sumY = 0, count = 0;
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (headMask[x, y])
                    {
                        sumX += x;
                        sumY += y;
                        count++;
                    }
            if (count == 0) return Point.Empty;
            return new Point(sumX / count, sumY / count);
        }

        public static void AddMouth(Image<Rgba32> bmp, Settings settings, bool[,] headMask = null)
        {
            int w = headMask.GetLength(0), h = headMask.GetLength(1);

            // Compute head centroid and bounding-box (prefer headMask if supplied)
            Point headCenter = Point.Empty;
            int left = w, right = -1, top = h, bottom = -1;

            if (headMask != null)
            {
                headCenter = EstimateHeadCenterFromMask(headMask);
                for (int x = 0; x < w; x++)
                    for (int y = 0; y < h; y++)
                        if (headMask[x, y])
                        {
                            if (x < left) left = x;
                            if (x > right) right = x;
                            if (y < top) top = y;
                            if (y > bottom) bottom = y;
                        }
            }

            if (right < left || bottom < top)
            {
                // make a small default head around center (robust fallback)
                if (headCenter.IsEmpty) headCenter = new Point(w / 2, Math.Max(0, h / 6));
                left = Math.Max(0, headCenter.X - Math.Max(2, w / 12));
                right = Math.Min(w - 1, headCenter.X + Math.Max(2, w / 12));
                top = Math.Max(0, headCenter.Y - Math.Max(1, h / 24));
                bottom = Math.Min(h - 1, headCenter.Y + Math.Max(1, h / 24));
            }

            int headWidth = Math.Max(1, right - left + 1);
            int headHeight = Math.Max(1, bottom - top + 1);

            // Choose a base mouth position: slightly lower than head center (cute), with small jitter.
            int baseY = top + (int)Math.Round(headHeight * 0.60) + DeterministicRandom.Next("AddMouth",  - 1, 2);
            int baseX = headCenter.IsEmpty ? left + headWidth / 2 : headCenter.X + DeterministicRandom.Next("AddMouth", -1, 2);

            bool[,] searchMask = headMask;
            var found = PixelUtils.FindNearestMaskPoint(searchMask, baseX, baseY, Math.Max(2, headWidth / 2));
            int mx = found.IsEmpty ? Math.Clamp(baseX, 0, w - 1) : found.X;
            int my = found.IsEmpty ? Math.Clamp(baseY, 0, h - 1) : found.Y;

            // mouth width: scale with headWidth — tuned for small sprites (64x64)
            int mouthWidth = Math.Clamp(headWidth / 3, 1, Math.Max(1, w / 8)); // for 64px headWidth ~ 12 -> mouthWidth ~ 4
            int half = Math.Max(1, mouthWidth / 2);

            Rgba32 black = Rgba32.ParseHex("#000000FF");
            Rgba32 white = Rgba32.ParseHex("#FFFFFFFF");

            // Helper to paint a pixel if it's inside the (search) mask and the canvas.
            void Put(int px, int py, Rgba32 c)
            {
                if (px < 0 || py < 0 || px >= w || py >= h) return;
                if (PixelUtils.IsPointInMask(searchMask, px, py))
                    PixelUtils.SafeSetPixel(bmp, px, py, c);
            }

            var style = settings.mouthStyle;
            if (style == MouthStyle.Random)
            {
                var vals = Enum.GetValues(typeof(MouthStyle));
                // exclude last enum (Random) when picking
                style = (MouthStyle)vals.GetValue(DeterministicRandom.Next("AddMouth", 0, vals.Length - 1));
            }

            // Draw styles — keep mouths small & simple so they read well at 64x64 pixel art.
            switch (style)
            {
                case MouthStyle.SmallO:
                    {
                        // Bigger tiny 'o' — radius depends on half (1..2). Draw a filled-ish circle
                        // but omit the top-centered pixel so it never looks like a star.
                        int r = Math.Clamp(half, 1, 2); // grows a bit for larger head/mouth widths

                        if (r <= 0)
                        {
                            Put(mx, my, black);
                        }
                        else
                        {
                            for (int dx = -r; dx <= r; dx++)
                                for (int dy = -r; dy <= r; dy++)
                                {
                                    if (dx * dx + dy * dy <= r * r)
                                    {
                                        // Omit the top-most center pixel to avoid "star" artifacts.
                                        if (dx == 0 && dy == -r) continue;
                                        Put(mx + dx, my + dy, black);
                                    }
                                }
                        }
                    }
                    break;

                case MouthStyle.Open:
                    {
                        // small open mouth: 1-2 px tall band
                        for (int dx = -half; dx <= half; dx++)
                        {
                            Put(mx + dx, my, black);
                            if (my + 1 < h) Put(mx + dx, my + 1, black);
                        }
                    }
                    break;

                case MouthStyle.Toothy:
                    {
                        // small smile + two tiny teeth **below** the mouth row (moved down one more pixel)
                        // draw a shallow smile curve (same parabola family as Smile)
                        for (int dx = -half; dx <= half; dx++)
                        {
                            double t = half > 0 ? (Math.Abs((double)dx) / (double)half) : 0.0;
                            int curve = (int)Math.Round((1.0 - t * t) * 1.0); // 0..1
                            Put(mx + dx, my + curve, black);
                        }
                        // place two tiny teeth one pixel further down so they sit clearly below the mouth
                        Put(mx - 1, my + 2, white);
                        Put(mx + 1, my + 2, white);
                    }
                    break;

                case MouthStyle.Grin:
                    {
                        // wider smile + small inner darkness for open grin, teeth below
                        int span = Math.Max(half, 1);
                        for (int dx = -span; dx <= span; dx++)
                        {
                            double t = (Math.Abs((double)dx) / (double)span);
                            int curve = (int)Math.Round((1.0 - t * t) * 1.0);
                            Put(mx + dx, my + curve, black);
                            // small inner darkness for "open grin"
                            if (my + curve + 1 < h) Put(mx + dx, my + curve + 1, black);
                        }
                        // teeth row inside/below the mouth (kept at +1 for a compact grin)
                        Put(mx - 1, my + 1, white);
                        Put(mx, my + 1, white);
                        Put(mx + 1, my + 1, white);
                    }
                    break;

                case MouthStyle.Sad:
                    {
                        // true downward arc (frown): center is higher than corners
                        // use the same parabola shape as the smile but inverted vertically.
                        for (int dx = -half; dx <= half; dx++)
                        {
                            double t = half > 0 ? (Math.Abs((double)dx) / (double)half) : 0.0;
                            int curve = (int)Math.Round((1.0 - t * t) * 1.0); // 0..1 (peak center)
                            Put(mx + dx, my - curve, black); // center above, corners below -> frown
                        }
                    }
                    break;

                case MouthStyle.Smile:
                default:
                    {
                        // gentle upward 'u' shaped smile (cute, subtle)
                        for (int dx = -half; dx <= half; dx++)
                        {
                            double t = half > 0 ? (Math.Abs((double)dx) / (double)half) : 0.0;
                            int curve = (int)Math.Round((1.0 - t * t) * 1.0); // 0..1
                            Put(mx + dx, my + curve, black);
                        }
                    }
                    break;
            }
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
                forcedStyle = (LimbStyle)(DeterministicRandom.Next("AddAnchoredLimbs") % (Enum.GetValues(typeof(LimbStyle)).Length - 1));

            // draw limb shapes into limbMask (no coloring yet)
            if (!leftArm.IsEmpty) DrawAdvancedLimbMask(limbMask, leftArm.X, leftArm.Y, -1, 1, DeterministicRandom.Next("AddAnchoredLimbs", armLenMin, armLenMax + 1), margin, forcedStyle ?? RandomLimbStyle(isLeg: false), isLeft: true, isLeg: false);
            if (!rightArm.IsEmpty) DrawAdvancedLimbMask(limbMask, rightArm.X, rightArm.Y, 1, 1, DeterministicRandom.Next("AddAnchoredLimbs", armLenMin, armLenMax + 1), margin, forcedStyle ?? RandomLimbStyle(isLeg: false), isLeft: false, isLeg: false);
            if (!leftLeg.IsEmpty) DrawAdvancedLimbMask(limbMask, leftLeg.X, leftLeg.Y, -1, 2, DeterministicRandom.Next("AddAnchoredLimbs", legLenMin, legLenMax + 1), margin, forcedStyle ?? RandomLimbStyle(isLeg: true), isLeft: true, isLeg: true);
            if (!rightLeg.IsEmpty) DrawAdvancedLimbMask(limbMask, rightLeg.X, rightLeg.Y, 1, 2, DeterministicRandom.Next("AddAnchoredLimbs", legLenMin, legLenMax + 1), margin, forcedStyle ?? RandomLimbStyle(isLeg: true), isLeft: false, isLeg: true);

            // If no limb pixels were drawn, exit
            bool any = false;
            for (int x = 0; x < w && !any; x++)
                for (int y = 0; y < h && !any; y++)
                    if (limbMask[x, y]) any = true;
            if (!any) return;

            // Now color the limb image with the same noise/palette code used by body/head
            // Outline = false for palette step: we'll draw outline after adding patterns
            ApplyNoisePalette(limbImg, limbMask, baseColor, accentColor, nColors: nColors, outline: false, mode: paletteMode);

            // Finally draw the outline so limbs have the same edge treatment as the body
            DrawMaskOutline(limbImg, limbMask, outlineColor);

            // Done — limbImg now contains properly-colored + outlined limb pixels on transparent background.
        }

        // ---------- Helpers: mask-painting primitives (operates on boolean masks only) ----------
        private static void SetMaskCircle(bool[,] mask, int cx, int cy, int radius, int margin)
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

        private static void SetMaskLine(bool[,] mask, int x0, int y0, int x1, int y1, int margin)
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
        private static void DrawAdvancedLimbMask(bool[,] mask, int sx, int sy, int dirX, int dirY, int length, int margin, LimbStyle style, bool isLeft, bool isLeg)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            if (length <= 0) return;

            switch (style)
            {
                case LimbStyle.Simple:
                    {
                        int thickness = DeterministicRandom.Next("DrawAdvancedLimbMask", 1, 3);
                        for (int i = 1; i <= length; i++)
                        {
                            int jitterX = DeterministicRandom.Next("DrawAdvancedLimbMask", -1, 2);
                            int jitterY = DeterministicRandom.Next("DrawAdvancedLimbMask", 0, 2);
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
                            int bend = (int)Math.Round(Math.Sin(i * 0.6 + DeterministicRandom.NextDouble("DrawAdvancedLimbMask")) * (isLeft ? -1 : 1));
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
                        int segments = Math.Max(2, Math.Min(length, DeterministicRandom.Next("DrawAdvancedLimbMask", 2, 1 + Math.Max(2, length / 2))));
                        var centers = new List<Point>();
                        for (int s = 0; s < segments; s++)
                        {
                            double t = (s + 0.5) / segments;
                            int cx = sx + (int)Math.Round(dirX * t * length + DeterministicRandom.Next("DrawAdvancedLimbMask", -1, 2));
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
                        double phase = DeterministicRandom.NextDouble("DrawAdvancedLimbMask") * Math.PI * 2.0;
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
                            if (DeterministicRandom.NextDouble("DrawAdvancedLimbMask") < 0.12 && i % 2 == 0)
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
                        double midT = 0.4 + DeterministicRandom.NextDouble("DrawAdvancedLimbMask") * 0.25;
                        int elbowX = sx + (int)Math.Round(dirX * length * midT) + DeterministicRandom.Next("DrawAdvancedLimbMask", -1, 2);
                        int elbowY = sy + (int)Math.Round(dirY * length * midT) + (isLeg ? DeterministicRandom.Next("DrawAdvancedLimbMask", 0, 2) : DeterministicRandom.Next("DrawAdvancedLimbMask", -1, 2));

                        // first segment
                        SetMaskLine(mask, sx, sy, elbowX, elbowY, margin);
                        SetMaskCircle(mask, elbowX, elbowY, 2, margin);

                        // second segment to end
                        int endX = sx + dirX * length + DeterministicRandom.Next("DrawAdvancedLimbMask", -1, 2);
                        int endY = sy + dirY * length + DeterministicRandom.Next("DrawAdvancedLimbMask", 0, 2);
                        SetMaskLine(mask, elbowX, elbowY, endX, endY, margin);
                        SetMaskCircle(mask, endX, endY, 1, margin);

                        // optional fingers/claws for arms
                        if (!isLeg && DeterministicRandom.NextDouble("DrawAdvancedLimbMask") < 0.6)
                        {
                            int clawCount = DeterministicRandom.Next("DrawAdvancedLimbMask", 1, 4);
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

                        int toeCount = DeterministicRandom.Next("DrawAdvancedLimbMask", 2, 4);
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
        private static LimbStyle RandomLimbStyle(bool isLeg)
        {
            double r = DeterministicRandom.NextDouble("RandomLimbStyle");
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

        private static bool IsEdgeMask(bool[,] mask, int x, int y)
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
    }
}
