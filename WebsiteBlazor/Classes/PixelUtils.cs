using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AutoSpriteCreator
{
    // Reuse the same Point struct as in FeatureDrawer
    public struct Point
    {
        public int X { get; }
        public int Y { get; }
        public static readonly Point Empty = new Point(int.MinValue, int.MinValue);

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool IsEmpty => X == int.MinValue && Y == int.MinValue;
    }

    public static class PixelUtils
    {
        public static void SafeSetPixel(Image<Rgba32> image, int x, int y, Rgba32 color)
        {
            if (image == null) return;

            if (x >= 0 && y >= 0 && x < image.Width && y < image.Height)
            {
                // Force alpha = 255 (like original)
                if (color.A != 255)
                    color = new Rgba32(color.R, color.G, color.B, 255);

                image[x, y] = color;
            }
        }

        public static void FillCircle(Image<Rgba32> bmp, int cx, int cy, int r, Rgba32 c)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (dx * dx + dy * dy <= r * r)
                        SafeSetPixel(bmp, cx + dx, cy + dy, c);
                }
            }
        }

        public static void FillCircleClipped(Image<Rgba32> bmp, bool[,] mask, int cx, int cy, int r, Rgba32 c)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (dx * dx + dy * dy <= r * r && IsPointInMask(mask, cx + dx, cy + dy))
                        SafeSetPixel(bmp, cx + dx, cy + dy, c);
                }
            }
        }

        public static bool IsPointInMask(bool[,] mask, int x, int y)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            return x >= 0 && y >= 0 && x < w && y < h && mask[x, y];
        }

        public static Point FindNearestMaskPoint(bool[,] mask, int x0, int y0, int maxR)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            for (int r = 0; r <= maxR; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        int x = x0 + dx, y = y0 + dy;
                        if (x >= 0 && y >= 0 && x < w && y < h && mask[x, y])
                            return new Point(x, y);
                    }
                }
            }
            return Point.Empty;
        }

        public static List<Point> GetMaskEdgePoints(bool[,] mask)
        {
            var edges = new List<Point>();
            int w = mask.GetLength(0), h = mask.GetLength(1);

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    if (IsEdgeMask(mask, x, y))
                        edges.Add(new Point(x, y));
                }
            }
            return edges;
        }

        public static Point FindEdgeNear(List<Point> edges, int targetX, int targetY)
        {
            Point best = Point.Empty;
            double bestDist = double.MaxValue;

            foreach (var p in edges)
            {
                double d = (p.X - targetX) * (p.X - targetX) +
                           (p.Y - targetY) * (p.Y - targetY);

                if (d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }
            return best;
        }

        static bool IsEdgeMask(bool[,] mask, int x, int y)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            if (!mask[x, y]) return false;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                        return true;
                    if (!mask[nx, ny])
                        return true;
                }
            }
            return false;
        }
    }
}
