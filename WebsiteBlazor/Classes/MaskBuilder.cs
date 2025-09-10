using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoSpriteCreator
{
    public static class MaskBuilder
    {
        public static bool[,] BuildMask(int width, int height, int margin)
        {
            bool[,] mask = new bool[width, height];

            int archetype = RNG.Rand.Next(8);
            int bodyStartY = height / 3;
            int bodyHeight = height - bodyStartY;
            int headHeight = bodyStartY;

            CreateBody(mask, archetype, width, height, bodyStartY, bodyHeight, margin);
            CreateHead(mask, archetype, width, height, bodyStartY, headHeight, margin);

            AddPerimeterNoise(mask, 0.12, 0.06);
            MorphologicalClean(mask, 1);

            // Organic symmetry: build right side from left with jitter
            mask = ApplyOrganicSymmetry(mask, 0.18, margin);

            // Close and heal small holes
            mask = CloseMask(mask, 1);

            // Safety: strip any pixel inside margin (shouldn't be needed but defensive)
            EnforceMargin(mask, margin);

            return mask;
        }

        // Create body - margin-aware
        static void CreateBody(bool[,] mask, int archetype, int width, int height, int bodyStartY, int bodyHeight, int margin)
        {
            int cx = width / 2 + RNG.Rand.Next(-4, 5);
            int cy = bodyStartY + bodyHeight / 2;

            switch (archetype)
            {
                case 0:
                    FillEllipseMask(mask, cx, cy, (int)(width * 0.36), (int)(bodyHeight * 0.45), margin);
                    break;
                case 1:
                    FillEllipseMask(mask, cx, cy, (int)(width * 0.26), (int)(bodyHeight * 0.6), margin);
                    FillEllipseMask(mask, cx, bodyStartY + bodyHeight / 3, (int)(width * 0.34), (int)(bodyHeight * 0.28), margin);
                    break;
                case 2:
                    FillEllipseMask(mask, cx, cy, (int)(width * 0.52), (int)(bodyHeight * 0.36), margin);
                    break;
                case 3:
                    int segments = RNG.Rand.Next(3, 6);
                    int segW = width / 4;
                    for (int i = 0; i < segments; i++) FillEllipseMask(mask, cx, bodyStartY + (i + 1) * bodyHeight / (segments + 1), Math.Max(1, segW - i), Math.Max(1, segW - i), margin);
                    break;
                case 4:
                    FillEllipseMask(mask, cx, bodyStartY + bodyHeight / 3, (int)(width * 0.32), (int)(bodyHeight * 0.28), margin);
                    FillEllipseMask(mask, cx, bodyStartY + (int)(bodyHeight * 0.62), (int)(width * 0.22), (int)(bodyHeight * 0.32), margin);
                    FillEllipseMask(mask, cx, bodyStartY + bodyHeight / 2, (int)(width * 0.18), (int)(bodyHeight * 0.12), margin);
                    break;
                case 5:
                    FillEllipseMask(mask, cx, cy, (int)(width * 0.36), (int)(bodyHeight * 0.45), margin);
                    FillEllipseMask(mask, cx - (int)(width * 0.25), bodyStartY + bodyHeight / 3, (int)(width * 0.18), (int)(bodyHeight * 0.28), margin);
                    FillEllipseMask(mask, cx + (int)(width * 0.25), bodyStartY + bodyHeight / 3, (int)(width * 0.18), (int)(bodyHeight * 0.28), margin);
                    break;
                case 6:
                    FillEllipseMask(mask, cx, cy, (int)(width * 0.34), (int)(bodyHeight * 0.44), margin);
                    FillEllipseMask(mask, cx - (int)(width * 0.12), bodyStartY + bodyHeight / 3, (int)(width * 0.22), (int)(bodyHeight * 0.2), margin);
                    break;
                case 7:
                    FillEllipseMask(mask, cx - (int)(width * 0.08), cy, (int)(width * 0.36), (int)(bodyHeight * 0.4), margin);
                    FillEllipseMask(mask, cx + (int)(width * 0.18), bodyStartY + (int)(bodyHeight * 0.6), (int)(width * 0.18), (int)(bodyHeight * 0.12), margin);
                    break;
                default:
                    FillEllipseMask(mask, cx, cy, (int)(width * 0.36), (int)(bodyHeight * 0.45), margin);
                    break;
            }
        }

        // Create head -- margin aware
        static void CreateHead(bool[,] mask, int archetype, int width, int height, int bodyStartY, int headHeight, int margin)
        {
            int headCx = width / 2 + RNG.Rand.Next(-3, 4);
            int headCy = Math.Max(margin + 1, bodyStartY - headHeight / 3 + RNG.Rand.Next(-2, 3));
            int headRx = (int)(width * 0.22) + RNG.Rand.Next(-2, 3);
            int headRy = Math.Max(3, headHeight / 2 + RNG.Rand.Next(-2, 3));

            FillEllipseMask(mask, headCx, headCy, headRx, headRy, margin);

            if (RNG.Rand.NextDouble() < 0.45)
            {
                int hornY = headCy - headRy / 2;
                if (RNG.Rand.NextDouble() < 0.5)
                {
                    FillEllipseMask(mask, headCx - headRx + 2, hornY - 2, 3, 3, margin);
                    FillEllipseMask(mask, headCx + headRx - 2, hornY - 2, 3, 3, margin);
                }
                else
                {
                    FillEllipseMask(mask, headCx, hornY - 3, 3, 4, margin);
                }
            }
        }

        // margin-aware ellipse
        static void FillEllipseMask(bool[,] mask, int cx, int cy, int rx, int ry, int margin)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            if (rx <= 0 || ry <= 0) return;

            int leftSpace = cx - margin;
            int rightSpace = (w - 1 - margin) - cx;
            int topSpace = cy - margin;
            int bottomSpace = (h - 1 - margin) - cy;

            rx = Math.Min(rx, Math.Max(0, Math.Min(leftSpace, rightSpace)));
            ry = Math.Min(ry, Math.Max(0, Math.Min(topSpace, bottomSpace)));

            if (rx <= 0)
            {
                cx = Math.Max(margin + 1, Math.Min(w - margin - 2, cx));
                leftSpace = cx - margin; rightSpace = (w - 1 - margin) - cx;
                rx = Math.Max(1, Math.Min(leftSpace, rightSpace));
            }
            if (ry <= 0)
            {
                cy = Math.Max(margin + 1, Math.Min(h - margin - 2, cy));
                topSpace = cy - margin; bottomSpace = (h - 1 - margin) - cy;
                ry = Math.Max(1, Math.Min(topSpace, bottomSpace));
            }

            for (int x = Math.Max(margin, cx - rx); x <= Math.Min(w - 1 - margin, cx + rx); x++)
                for (int y = Math.Max(margin, cy - ry); y <= Math.Min(h - 1 - margin, cy + ry); y++)
                {
                    double dx = (x - cx) / (double)rx;
                    double dy = (y - cy) / (double)ry;
                    if (dx * dx + dy * dy <= 1.0) mask[x, y] = true;
                }
        }

        static void AddPerimeterNoise(bool[,] mask, double addProb, double removeProb)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            var toAdd = new List<Point>();
            var toRemove = new List<Point>();

            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    if (mask[x, y])
                    {
                        if (IsEdgeMask(mask, x, y) && RNG.Rand.NextDouble() < removeProb) toRemove.Add(new Point(x, y));
                    }
                    else
                    {
                        if (HasAdjacentMask(mask, x, y) && RNG.Rand.NextDouble() < addProb) toAdd.Add(new Point(x, y));
                    }
                }

            foreach (var p in toRemove) mask[p.X, p.Y] = false;
            foreach (var p in toAdd) mask[p.X, p.Y] = true;
        }

        static bool HasAdjacentMask(bool[,] mask, int x, int y)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < w && ny < h && mask[nx, ny]) return true;
                }
            return false;
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

        static void MorphologicalClean(bool[,] mask, int passes)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            for (int p = 0; p < passes; p++)
            {
                bool[,] next = (bool[,])mask.Clone();
                for (int x = 0; x < w; x++)
                    for (int y = 0; y < h; y++)
                    {
                        int neighbors = 0;
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                int nx = x + dx, ny = y + dy;
                                if (nx >= 0 && ny >= 0 && nx < w && ny < h && mask[nx, ny]) neighbors++;
                            }
                        next[x, y] = neighbors >= 3;
                    }
                for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) mask[x, y] = next[x, y];
            }
        }

        // ----------------- Organic symmetry by mirroring left -> right with jitter -----------------
        static bool[,] ApplyOrganicSymmetry(bool[,] mask, double jitterProb, int margin)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            int half = w / 2;

            // copy left half
            bool[,] left = new bool[half, h];
            for (int x = 0; x < half; x++) for (int y = 0; y < h; y++) left[x, y] = mask[x, y];

            bool[,] result = new bool[w, h];

            // paste left into result
            for (int x = 0; x < half; x++) for (int y = 0; y < h; y++) result[x, y] = left[x, y];

            // per-column shift
            int[] colShift = new int[half];
            for (int x = 0; x < half; x++) colShift[x] = (RNG.Rand.NextDouble() < 0.35) ? RNG.Rand.Next(-1, 2) : 0;

            for (int x = 0; x < half; x++)
            {
                int mx = w - 1 - x;
                for (int y = 0; y < h; y++)
                {
                    if (!left[x, y]) continue;

                    int jitterY = (RNG.Rand.NextDouble() < 0.28) ? RNG.Rand.Next(-1, 2) : 0;
                    int ny = y + colShift[x] + jitterY;
                    if (ny < margin || ny >= h - margin) continue;
                    if (mx < margin || mx >= w - margin) continue;

                    if (RNG.Rand.NextDouble() < jitterProb * 0.18) continue; // drop occasionally

                    result[mx, ny] = true;
                }

                if (RNG.Rand.NextDouble() < jitterProb * 0.2)
                {
                    int probeY = RNG.Rand.Next(margin, h - margin);
                    if (mx >= margin && mx < w - margin) result[mx, probeY] = true;
                }
            }

            // odd width center blending
            if (w % 2 == 1)
            {
                int c = half;
                for (int y = 0; y < h; y++)
                {
                    bool leftN = (c - 1 >= 0) && result[c - 1, y];
                    bool rightN = (c + 1 < w) && result[c + 1, y];
                    if (leftN || rightN) result[c, y] = (RNG.Rand.NextDouble() < 0.9) ? true : (leftN && rightN);
                }
            }

            return result;
        }

        // ------------------ Closing ------------------
        static bool[,] CloseMask(bool[,] mask, int iterations)
        {
            bool[,] current = mask;
            for (int i = 0; i < iterations; i++) { current = Dilate(current); current = Erode(current); }
            return current;
        }

        static bool[,] Dilate(bool[,] mask)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            bool[,] outm = new bool[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    bool any = false;
                    for (int dx = -1; dx <= 1 && !any; dx++)
                        for (int dy = -1; dy <= 1 && !any; dy++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < w && ny < h && mask[nx, ny]) any = true;
                        }
                    outm[x, y] = any;
                }
            return outm;
        }

        static bool[,] Erode(bool[,] mask)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            bool[,] outm = new bool[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    bool all = true;
                    for (int dx = -1; dx <= 1 && all; dx++)
                        for (int dy = -1; dy <= 1 && all; dy++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (!(nx >= 0 && ny >= 0 && nx < w && ny < h && mask[nx, ny])) all = false;
                        }
                    outm[x, y] = all;
                }
            return outm;
        }

        static void EnforceMargin(bool[,] mask, int margin)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (x < margin || x >= w - margin || y < margin || y >= h - margin) mask[x, y] = false;
        }
    }
}
