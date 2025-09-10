using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AutoSpriteCreator
{
    public static class FeatureDrawer
    {
        public static void ApplyNoisePalette(Image<Rgba32> bmp, bool[,] mask, Rgba32 baseColor, Rgba32 accentColor, int nColors = 6, bool outline = true)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);

            Rgba32[] palette = BuildPaletteFromBase(baseColor, nColors);

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

                    bool right = PixelUtils.IsPointInMask(mask, x + 1, y);
                    bool left = PixelUtils.IsPointInMask(mask, x - 1, y);
                    bool down = PixelUtils.IsPointInMask(mask, x, y + 1);
                    bool up = PixelUtils.IsPointInMask(mask, x, y - 1);

                    if (!down)
                    {
                        n -= 0.45; n *= 0.8;
                        if (outline) PixelUtils.SafeSetPixel(bmp, x, y + 1, Rgba32.ParseHex("#00000000"));
                    }
                    if (!right)
                    {
                        n += 0.2; n *= 1.1;
                        if (outline) PixelUtils.SafeSetPixel(bmp, x + 1, y, Rgba32.ParseHex("#00000000"));
                    }
                    if (!up)
                    {
                        n += 0.45; n *= 1.2;
                        if (outline) PixelUtils.SafeSetPixel(bmp, x, y - 1, Rgba32.ParseHex("#00000000"));
                    }
                    if (!left)
                    {
                        n += 0.2; n *= 1.1;
                        if (outline) PixelUtils.SafeSetPixel(bmp, x - 1, y, Rgba32.ParseHex("#00000000"));
                    }

                    int idx0 = NoiseIndexToPalette(noise.GetNoise2D(colX, y), nColors);
                    int idx1 = NoiseIndexToPalette(noise.GetNoise2D(colX, y - 1), nColors);
                    int idx2 = NoiseIndexToPalette(noise.GetNoise2D(colX, y + 1), nColors);
                    int idx3 = NoiseIndexToPalette(noise.GetNoise2D(colX - 1, y), nColors);
                    int idx4 = NoiseIndexToPalette(noise.GetNoise2D(colX + 1, y), nColors);

                    Rgba32 c0 = palette[idx0];
                    Rgba32 c1 = palette[idx1];
                    Rgba32 c2 = palette[idx2];
                    Rgba32 c3 = palette[idx3];
                    Rgba32 c4 = palette[idx4];

                    double diff =
                        (Math.Abs(c0.R - c1.R) / 255.0 + Math.Abs(c0.G - c1.G) / 255.0 + Math.Abs(c0.B - c1.B) / 255.0) +
                        (Math.Abs(c0.R - c2.R) / 255.0 + Math.Abs(c0.G - c2.G) / 255.0 + Math.Abs(c0.B - c2.B) / 255.0) +
                        (Math.Abs(c0.R - c3.R) / 255.0 + Math.Abs(c0.G - c3.G) / 255.0 + Math.Abs(c0.B - c3.B) / 255.0) +
                        (Math.Abs(c0.R - c4.R) / 255.0 + Math.Abs(c0.G - c4.G) / 255.0 + Math.Abs(c0.B - c4.B) / 255.0);

                    if (diff > 2.0)
                    {
                        n += 0.3; n *= 1.5;
                        n2 += 0.3; n2 *= 1.5;
                    }

                    n = Math.Max(0.0, Math.Min(1.0, n));
                    int pick = (int)Math.Floor(n * (nColors - 1));
                    pick = Math.Max(0, Math.Min(nColors - 1, pick));

                    PixelUtils.SafeSetPixel(bmp, x, y, palette[pick]);
                }
            }
        }

        static int NoiseIndexToPalette(double noiseVal, int nColors)
        {
            double v = noiseVal * 0.5 + 0.5;
            int idx = (int)Math.Floor(v * (nColors - 1) + 1e-9);
            return Math.Clamp(idx, 0, nColors - 1);
        }

        static Rgba32[] BuildPaletteFromBase(Rgba32 baseColor, int nColors)
        {
            Rgba32[] pal = new Rgba32[nColors];
            double h, s, v;
            ColorUtils.RGBtoHSV(baseColor, out h, out s, out v);

            double vMin = Math.Max(0.12, v * 0.35);
            double vMax = Math.Min(1.0, v * 1.15 + 0.05);

            for (int i = 0; i < nColors; i++)
            {
                double t = nColors == 1 ? 0.5 : (double)i / (nColors - 1);
                double vv = vMin + (vMax - vMin) * t;
                double ss = Math.Max(0.05, Math.Min(1.0, s * (1.0 - 0.15 * (t - 0.5))));
                pal[i] = ColorUtils.ColorFromHSV(h, ss, vv);
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

        public static void AddAppendages(Image<Rgba32> bmp, bool[,] mask, Rgba32 color, int margin)
        {
            var edges = PixelUtils.GetMaskEdgePoints(mask);
            if (edges.Count == 0) return;
            int w = mask.GetLength(0), h = mask.GetLength(1);
            int tries = 6;
            for (int i = 0; i < tries; i++)
            {
                Point p = edges[RNG.Rand.Next(edges.Count)];
                int dir = (p.X < w / 2) ? -1 : 1;
                for (int s = 1; s <= RNG.Rand.Next(1, 5); s++)
                {
                    int tx = p.X + dir * s;
                    int ty = p.Y + ((RNG.Rand.NextDouble() < 0.5) ? -s : s / 2);
                    if (tx < margin || tx >= w - margin || ty < margin || ty >= h - margin) break;
                    PixelUtils.SafeSetPixel(bmp, tx, ty, color);
                }
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

        public static void ApplySimpleShading(Image<Rgba32> bmp, bool[,] mask)
        {
            int w = bmp.Width, h = bmp.Height;
            float lx = w * 0.2f, ly = h * 0.2f;

            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (mask[x, y])
                    {
                        Rgba32 orig = bmp[x, y];
                        float dx = (x - lx) / w;
                        float dy = (y - ly) / h;
                        float shade = 1f - Math.Min(0.35f, (dx * dx + dy * dy));
                        PixelUtils.SafeSetPixel(bmp, x, y, ColorUtils.Lighten(orig, 1 - shade));
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
