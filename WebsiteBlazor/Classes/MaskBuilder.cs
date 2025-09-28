// MaskBuilder.cs - drop this into your AutoSpriteCreator project (replace existing file)
using System;
using System.Collections.Generic;
using System.Drawing;
using WebsiteBlazor.Classes;

namespace AutoSpriteCreator
{
    public static class MaskBuilder
    {
        // Backwards-compatible: returns the combined mask (body + head) like your original API.
        public static bool[,] BuildMask(Settings settings)
        {
            BuildProcessedMasks(settings, out bool[,] bodyMask, out bool[,] headMask);

            bool[,] result = new bool[settings.Dimension, settings.Dimension];
            for (int x = 0; x < settings.Dimension; x++)
                for (int y = 0; y < settings.Dimension; y++)
                    result[x, y] = bodyMask[x, y] || headMask[x, y];

            return result;
        }

        // New: returns processed masks split into body and head so features/animations can use them separately.
        public static void BuildProcessedMasks(Settings settings, out bool[,] bodyMask, out bool[,] headMask)
        {
            // 1) build base shapes separately (these are deterministic-ish and stable)
            bool[,] baseBody = new bool[settings.Dimension, settings.Dimension];
            bool[,] baseHead = new bool[settings.Dimension, settings.Dimension];

            int archetype = RNG.Rand.Next(8);
            int bodyStartY = settings.Dimension / 3;
            int bodyHeight = settings.Dimension - bodyStartY;
            int headHeight = bodyStartY;

            CreateBody(baseBody, archetype, settings.Dimension, settings.Dimension, bodyStartY, bodyHeight, settings.Margin);
            CreateHead(baseHead, archetype, settings.Dimension, settings.Dimension, bodyStartY, headHeight, settings.Margin);

            // 2) produce a varied body from the base body (noise, segments, lobes, spikes, holes)
            bool[,] variedBody = GenerateVariedBody(baseBody, bodyStartY, bodyHeight, settings);

            // 3) recombine with processed extras and the preserved head region
            // we apply a tiny perimeter noise and cleanup again for painterly niceness
            bool[,] combined = new bool[settings.Dimension, settings.Dimension];
            for (int x = 0; x < settings.Dimension; x++)
                for (int y = 0; y < settings.Dimension; y++)
                    combined[x, y] = variedBody[x, y] || baseHead[x, y];

            AddPerimeterNoise(combined, 0.10, 0.05);
            MorphologicalClean(combined, 1);
            combined = ApplyOrganicSymmetry(combined, 0.18, settings.Margin);
            combined = CloseMask(combined, 1);
            EnforceMargin(combined, settings.Margin);

            // 4) split into head vs body masks: prefer original head pixels to ensure stable eyes/mouth
            bodyMask = new bool[settings.Dimension, settings.Dimension];
            headMask = new bool[settings.Dimension, settings.Dimension];
            for (int x = 0; x < settings.Dimension; x++)
                for (int y = 0; y < settings.Dimension; y++)
                {
                    if (!combined[x, y]) continue;
                    if (baseHead[x, y]) headMask[x, y] = true;
                    else bodyMask[x, y] = true;
                }
        }

        // ------------------- Core varied body generator -------------------
        static bool[,] GenerateVariedBody(bool[,] baseBody, int bodyStartY, int bodyHeight, Settings settings)
        {
            int w = baseBody.GetLength(0), h = baseBody.GetLength(1);

            // signed distance field (inside positive)
            float[,] distToBG = DistanceTransformInverse(baseBody);
            float[,] distToFG = DistanceTransform(baseBody);
            float[,] signed = new float[w, h];
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) signed[x, y] = distToBG[x, y] - distToFG[x, y];

            var seed = RNG.Rand.Next();
            NoisePresets.ComputeForWidth(w, settings.NoiseStyle, out float baseNoiseScale, out float amplitudePx, out int octaves, out float persistence, seed: seed);
            int targetCells = RNG.Rand.Next(Math.Max(1, w / 24), Math.Max(2, w / 8)); // varied cell count

            // Ensure consistent effect across octaves
            float totalAmp = 0; float a = 1f;
            for (int i = 0; i < octaves; i++) { totalAmp += a; a *= persistence; }
            if (totalAmp <= 0) totalAmp = 1;

            bool[,] mask = new bool[w, h];

            // 1) Perimeter displacement with vertical falloff: protect the neck/head region
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    float v = (y - bodyStartY) / (float)Math.Max(1, bodyHeight);
                    float falloff = Clamp01(SmoothStep(Clamp01(v))); // 0 near head, 1 in lower body

                    float fbm = SampleFbm(x * baseNoiseScale, y * baseNoiseScale, octaves, persistence, seed);
                    // value-noise based fbm roughly in [-1..1]; normalize by totalAmp
                    fbm /= totalAmp;
                    float displacement = fbm * amplitudePx * falloff;

                    float newSigned = signed[x, y] + displacement;
                    mask[x, y] = newSigned > 0f;
                }
            }

            // 2) segmented variants (sausage / armor plates)
            if (RNG.Rand.NextDouble() < 0.5)
            {
                bool[,] seg = new bool[w, h];
                int segments = RNG.Rand.Next(2, 6);
                int cxBase = w / 2 + RNG.Rand.Next(-3, 4);
                for (int i = 0; i < segments; i++)
                {
                    double t = segments == 1 ? 0.5 : (double)i / (segments - 1);
                    int cy = bodyStartY + (int)(t * bodyHeight) + RNG.Rand.Next(-3, 4);
                    int rx = Math.Max(2, (int)(w * (0.14 + 0.24 * (0.5 + RNG.Rand.NextDouble() * (1.0 - Math.Abs(0.5 - t))))));
                    int ry = Math.Max(2, (int)(bodyHeight * (0.10 + 0.18 * RNG.Rand.NextDouble())));
                    FillEllipseMask(seg, cxBase + RNG.Rand.Next(-6, 7), cy, rx, ry, settings.Margin);
                }

                if (RNG.Rand.NextDouble() < 0.6)
                {
                    // union for bulgy segmented look
                    for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) mask[x, y] = mask[x, y] || seg[x, y];
                }
                else
                {
                    // replace
                    mask = seg;
                }
            }

            // 3) add / subtract lobes (extra bellies, side pouches)
            int blobCount = RNG.Rand.Next(0, 4);
            var bbox = GetBoundingBox(mask);
            if (bbox.Width <= 0 || bbox.Height <= 0) bbox = GetBoundingBox(baseBody);

            for (int b = 0; b < blobCount; b++)
            {
                int bx = RNG.Rand.Next(Math.Max(settings.Margin, bbox.Left - 4), Math.Min(w - settings.Margin, bbox.Right + 4));
                int by = RNG.Rand.Next(Math.Max(settings.Margin, bbox.Top - 4), Math.Min(h - settings.Margin, bbox.Bottom + 4));
                int brx = Math.Max(1, RNG.Rand.Next(Math.Max(2, w / 24), Math.Max(2, w / 8)));
                int bry = Math.Max(1, RNG.Rand.Next(Math.Max(2, h / 32), Math.Max(2, h / 10)));
                bool[,] blob = new bool[w, h];
                FillEllipseMask(blob, bx, by, brx, bry, settings.Margin);

                if (RNG.Rand.NextDouble() < 0.72)
                {
                    for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) mask[x, y] = mask[x, y] || blob[x, y];
                }
                else
                {
                    for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (blob[x, y]) mask[x, y] = false;
                }
            }

            // 4) perforations (holes)
            if (RNG.Rand.NextDouble() < 0.45)
            {
                int holes = RNG.Rand.Next(0, 4);
                for (int i = 0; i < holes; i++)
                {
                    int hx = RNG.Rand.Next(Math.Max(settings.Margin, bbox.Left), Math.Min(w - settings.Margin, bbox.Right));
                    int hy = RNG.Rand.Next(Math.Max(settings.Margin, bbox.Top), Math.Min(h - settings.Margin, bbox.Bottom));
                    int hrx = Math.Max(1, RNG.Rand.Next(1, Math.Max(2, w / 18)));
                    int hry = Math.Max(1, RNG.Rand.Next(1, Math.Max(2, h / 20)));
                    bool[,] hole = new bool[w, h];
                    FillEllipseMask(hole, hx, hy, hrx, hry, settings.Margin);
                    for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (hole[x, y]) mask[x, y] = false;
                }
            }

            // 5) spikes / protrusions along perimeter
            if (RNG.Rand.NextDouble() < 0.55)
            {
                List<Point> edges = new List<Point>();
                for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (IsEdgeMask(mask, x, y)) edges.Add(new Point(x, y));
                if (edges.Count > 0)
                {
                    int spikeMax = Math.Max(0, Math.Min(12, edges.Count / 8 + RNG.Rand.Next(0, 5)));
                    for (int s = 0; s < spikeMax; s++)
                    {
                        var p = edges[RNG.Rand.Next(edges.Count)];
                        int px = p.X, py = p.Y;
                        // gradient approx on signed field
                        float gx = SampleSafe(signed, px + 1, py) - SampleSafe(signed, px - 1, py);
                        float gy = SampleSafe(signed, px, py + 1) - SampleSafe(signed, px, py - 1);
                        float len = (float)Math.Sqrt(gx * gx + gy * gy);
                        float nx, ny;
                        if (len > 1e-6f) { nx = gx / len; ny = gy / len; }
                        else { nx = px - w / 2f; ny = py - (bodyStartY + bodyHeight / 2f); float nl = (float)Math.Sqrt(nx * nx + ny * ny) + 1e-6f; nx /= nl; ny /= nl; }

                        int spikeLen = RNG.Rand.Next(Math.Max(2, w / 40), Math.Max(3, w / 18));
                        for (int l = 1; l <= spikeLen; l++)
                        {
                            int sx = px + (int)Math.Round(nx * l);
                            int sy = py + (int)Math.Round(ny * l);
                            int halfW = Math.Max(0, (int)Math.Round((1.0 - (double)l / spikeLen) * (1 + RNG.Rand.Next(0, 2))));
                            for (int wx = -halfW; wx <= halfW; wx++)
                                for (int wy = -halfW; wy <= halfW; wy++)
                                {
                                    int ax = sx + wx, ay = sy + wy;
                                    if (ax >= 0 && ay >= 0 && ax < w && ay < h && ax >= settings.Margin && ay >= settings.Margin && ax < w - settings.Margin && ay < h - settings.Margin)
                                        mask[ax, ay] = true;
                                }
                        }
                    }
                }
            }

            // 6) cleanup and small morphology for readability at small sprite sizes
            MorphologicalClean(mask, 1);
            if (RNG.Rand.NextDouble() < 0.5) mask = Dilate(mask);
            if (RNG.Rand.NextDouble() < 0.5) mask = Erode(mask);

            return mask;
        }

        // ----------------- Base shape builders (unchanged style) -----------------
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

        // ------------------- Low-level helpers & morphology -------------------

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

        static bool[,] ApplyOrganicSymmetry(bool[,] mask, double jitterProb, int margin)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            int half = w / 2;

            bool[,] left = new bool[half, h];
            for (int x = 0; x < half; x++) for (int y = 0; y < h; y++) left[x, y] = mask[x, y];

            bool[,] result = new bool[w, h];
            for (int x = 0; x < half; x++) for (int y = 0; y < h; y++) result[x, y] = left[x, y];

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

        // ------------------ Distance transforms (for SDF) ------------------
        static float[,] DistanceTransform(bool[,] binary)
        {
            int w = binary.GetLength(0), h = binary.GetLength(1);
            float INF = 1e6f;
            float[,] dist = new float[w, h];

            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    dist[x, y] = binary[x, y] ? 0f : INF;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float v = dist[x, y];
                    if (x > 0) v = Math.Min(v, dist[x - 1, y] + 1f);
                    if (y > 0) v = Math.Min(v, dist[x, y - 1] + 1f);
                    if (x > 0 && y > 0) v = Math.Min(v, dist[x - 1, y - 1] + 1.41421356f);
                    if (x + 1 < w && y > 0) v = Math.Min(v, dist[x + 1, y - 1] + 1.41421356f);
                    dist[x, y] = v;
                }
            }

            for (int y = h - 1; y >= 0; y--)
            {
                for (int x = w - 1; x >= 0; x--)
                {
                    float v = dist[x, y];
                    if (x + 1 < w) v = Math.Min(v, dist[x + 1, y] + 1f);
                    if (y + 1 < h) v = Math.Min(v, dist[x, y + 1] + 1f);
                    if (x + 1 < w && y + 1 < h) v = Math.Min(v, dist[x + 1, y + 1] + 1.41421356f);
                    if (x > 0 && y + 1 < h) v = Math.Min(v, dist[x - 1, y + 1] + 1.41421356f);
                    dist[x, y] = v;
                }
            }

            return dist;
        }

        static float[,] DistanceTransformInverse(bool[,] mask)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            bool[,] inv = new bool[w, h];
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) inv[x, y] = !mask[x, y];
            return DistanceTransform(inv);
        }

        // ------------------ Simple value-noise + fBm (no external deps) ------------------
        static float SampleFbm(float x, float y, int octaves, float persistence, int seed)
        {
            float sum = 0;
            float amp = 1;
            float freq = 1;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * ValueNoise2D(x * freq, y * freq, seed + i * 1315423911);
                amp *= persistence;
                freq *= 2f;
            }
            return sum;
        }

        static float ValueNoise2D(float x, float y, int seed)
        {
            int x0 = FastFloor(x), y0 = FastFloor(y);
            float sx = x - x0, sy = y - y0;
            float n00 = HashFloat(x0, y0, seed);
            float n10 = HashFloat(x0 + 1, y0, seed);
            float n01 = HashFloat(x0, y0 + 1, seed);
            float n11 = HashFloat(x0 + 1, y0 + 1, seed);

            float ix0 = Lerp(n00, n10, SmoothStep(sx));
            float ix1 = Lerp(n01, n11, SmoothStep(sx));
            return Lerp(ix0, ix1, SmoothStep(sy));
        }

        static int FastFloor(float v) { return (int)Math.Floor(v); }
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float SmoothStep(float t) => t * t * t * (t * (t * 6 - 15) + 10); // 6t^5 - 15t^4 + 10t^3

        // deterministic integer hash -> [-1,1]
        static float HashFloat(int x, int y, int seed)
        {
            unchecked
            {
                uint n = (uint)(x * 374761393u) ^ (uint)(y * 668265263u) ^ (uint)seed;
                n = (n ^ (n >> 13)) * 1274126177u;
                float res = (n & 0x7fffffff) / (float)0x7fffffff;
                return res * 2f - 1f;
            }
        }

        // ------------------ little helpers ------------------
        static Rectangle GetBoundingBox(bool[,] mask)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (mask[x, y]) { if (x < minX) minX = x; if (y < minY) minY = y; if (x > maxX) maxX = x; if (y > maxY) maxY = y; }
            if (maxX < minX || maxY < minY) return Rectangle.Empty;
            return Rectangle.FromLTRB(minX, minY, maxX, maxY);
        }

        static float SampleSafe(float[,] arr, int x, int y)
        {
            int w = arr.GetLength(0), h = arr.GetLength(1);
            if (x < 0) x = 0; if (y < 0) y = 0; if (x >= w) x = w - 1; if (y >= h) y = h - 1;
            return arr[x, y];
        }

        static float Clamp01(float v) { if (v < 0f) return 0f; if (v > 1f) return 1f; return v; }
    }
}
