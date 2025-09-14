using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoSpriteCreator
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using WebsiteBlazor.Classes;

    public static class MaskBuilder
    {
        public static bool[,] BuildMask(Settings settings)
        {
            bool[,] bodyMask = new bool[settings.Dimension, settings.Dimension];
            bool[,] headMask = new bool[settings.Dimension, settings.Dimension];

            int archetype = RNG.Rand.Next(8);
            int bodyStartY = settings.Dimension / 3;
            int bodyHeight = settings.Dimension - bodyStartY;
            int headHeight = bodyStartY;

            // Build body and head separately
            CreateBody(bodyMask, archetype, settings.Dimension, settings.Dimension, bodyStartY, bodyHeight, settings.Margin);
            CreateHead(headMask, archetype, settings.Dimension, settings.Dimension, bodyStartY, headHeight, settings.Margin);

            // Strong, varied body processing (keeps head intact)
            bool[,] variedBody = GenerateVariedBody(bodyMask, bodyStartY, bodyHeight, settings.Margin, settings);

            // Recombine, head guaranteed intact
            bool[,] mask = new bool[settings.Dimension, settings.Dimension];
            for (int x = 0; x < settings.Dimension; x++)
                for (int y = 0; y < settings.Dimension; y++)
                    mask[x, y] = variedBody[x, y] || headMask[x, y];

            // Small perimeter noise and cleanup (keeps that painterly detail)
            AddPerimeterNoise(mask, 0.12, 0.06);
            MorphologicalClean(mask, 1);

            // Organic symmetry and closing/healing
            mask = ApplyOrganicSymmetry(mask, 0.18, settings.Margin);
            mask = CloseMask(mask, 1);

            // Safety margin strip
            EnforceMargin(mask, settings.Margin);

            return mask;
        }

        // ------------------ Core creative pipeline ------------------
        static bool[,] GenerateVariedBody(bool[,] baseBody, int bodyStartY, int bodyHeight, int margin, Settings settings)
        {
            int w = baseBody.GetLength(0), h = baseBody.GetLength(1);

            // 1) Signed distance field of the base body
            float[,] distToBG = DistanceTransformInverse(baseBody); // inside >0
            float[,] distToFG = DistanceTransform(baseBody);        // outside >0
            float[,] signed = new float[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    signed[x, y] = distToBG[x, y] - distToFG[x, y];

            // We'll work on a mask starting from an FBM-displaced signed field
            // Parameters that strongly affect style — editable!

            NoisePresets.ComputeForWidth(settings.Dimension, settings.NoiseStyle, out float baseNoiseScale, out float amplitudePx, out int octaves, out float persistence);

            //float baseNoiseScale = 0.025f + (float)RNG.Rand.NextDouble() * 0.045f; // controls feature size
            //float amplitudePx = Math.Max(2f, (float)w * (0.05f + (float)RNG.Rand.NextDouble() * 0.15f)); // silhouette displacement
            //int octaves = RNG.Rand.Next(2, 5);
            //float persistence = 0.48f + (float)RNG.Rand.NextDouble() * 0.14f;

            bool[,] mask = new bool[w, h];

            // 2) Perimeter displacement with a vertical falloff so the head stays safe
            // falloff: 0 at above/around head, 1 in lower body
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    float v = (y - bodyStartY) / (float)Math.Max(1, bodyHeight);
                    float falloff = Clamp01(SmoothStep(Clamp01(v))); // smoother transition
                    float fbm = SampleFbm(x * baseNoiseScale, y * baseNoiseScale, octaves, persistence, RNG.Rand.Next());
                    // Normalize rough [-1,1] sampling of our fBm approx (value noise maps to approx [-1,1])
                    float displacement = fbm * amplitudePx * falloff;
                    float newSigned = signed[x, y] + displacement;
                    mask[x, y] = newSigned > 0f;
                }
            }

            // 3) Add optional segmented variants (sausage / armor plates)
            if (RNG.Rand.NextDouble() < 0.45)
            {
                bool[,] seg = new bool[w, h];
                int segments = RNG.Rand.Next(2, 6);
                int cxBase = w / 2 + RNG.Rand.Next(-3, 4);
                for (int i = 0; i < segments; i++)
                {
                    // place segments along the body axis with jitter and varied radii
                    double t = segments == 1 ? 0.5 : (double)i / (segments - 1);
                    int cy = bodyStartY + (int)(t * bodyHeight) + RNG.Rand.Next(-3, 4);
                    int rx = Math.Max(2, (int)(w * (0.18 + 0.18 * (0.5 + RNG.Rand.NextDouble() * (1.0 - Math.Abs(0.5 - t))))));
                    int ry = Math.Max(2, (int)(bodyHeight * (0.12 + 0.18 * RNG.Rand.NextDouble())));
                    FillEllipseMask(seg, cxBase + RNG.Rand.Next(-6, 7), cy, rx, ry, margin);
                }

                if (RNG.Rand.NextDouble() < 0.55)
                {
                    // union for bulgy segmented look
                    for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) mask[x, y] = mask[x, y] || seg[x, y];
                }
                else
                {
                    // replace (pure segmented silhouette)
                    mask = seg;
                }
            }

            // 4) Extra blobs / lobes: additive or subtractive
            int blobCount = RNG.Rand.Next(0, 5);
            var bbox = GetBoundingBox(mask);
            // If mask is empty fallback to baseBody bbox
            if (bbox.Width <= 0 || bbox.Height <= 0) bbox = GetBoundingBox(baseBody);

            for (int b = 0; b < blobCount; b++)
            {
                int bx = RNG.Rand.Next(Math.Max(margin, bbox.Left - 4), Math.Min(w - margin, bbox.Right + 4));
                int by = RNG.Rand.Next(Math.Max(margin, bbox.Top - 4), Math.Min(h - margin, bbox.Bottom + 4));
                int brx = Math.Max(1, RNG.Rand.Next(Math.Max(2, w / 24), Math.Max(2, w / 8)));
                int bry = Math.Max(1, RNG.Rand.Next(Math.Max(2, h / 32), Math.Max(2, h / 10)));
                bool[,] blob = new bool[w, h];
                FillEllipseMask(blob, bx, by, brx, bry, margin);

                if (RNG.Rand.NextDouble() < 0.68)
                {
                    // union (extra lobe)
                    for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) mask[x, y] = mask[x, y] || blob[x, y];
                }
                else
                {
                    // subtract (pouch / notch)
                    for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (blob[x, y]) mask[x, y] = false;
                }
            }

            // 5) Perforations (holes)
            if (RNG.Rand.NextDouble() < 0.5)
            {
                int holes = RNG.Rand.Next(0, 4);
                for (int i = 0; i < holes; i++)
                {
                    int hx = RNG.Rand.Next(Math.Max(margin, bbox.Left), Math.Min(w - margin, bbox.Right));
                    int hy = RNG.Rand.Next(Math.Max(margin, bbox.Top), Math.Min(h - margin, bbox.Bottom));
                    int hrx = Math.Max(1, RNG.Rand.Next(1, Math.Max(2, w / 18)));
                    int hry = Math.Max(1, RNG.Rand.Next(1, Math.Max(2, h / 20)));
                    bool[,] hole = new bool[w, h];
                    FillEllipseMask(hole, hx, hy, hrx, hry, margin);
                    for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (hole[x, y]) mask[x, y] = false;
                }
            }

            // 6) Spikes / protrusions along perimeter
            if (RNG.Rand.NextDouble() < 0.55)
            {
                List<Point> edges = new List<Point>();
                for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (IsEdgeMask(mask, x, y)) edges.Add(new Point(x, y));
                int spikeMax = Math.Max(0, Math.Min(12, edges.Count / 8 + RNG.Rand.Next(0, 5)));
                for (int s = 0; s < spikeMax; s++)
                {
                    var p = edges[RNG.Rand.Next(edges.Count)];
                    int px = p.X, py = p.Y;
                    // gradient of signed field (simple finite diff)
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
                        // small width for triangular feel
                        int halfW = Math.Max(0, (int)Math.Round((1.0 - (double)l / spikeLen) * (1 + RNG.Rand.Next(0, 2))));
                        for (int wx = -halfW; wx <= halfW; wx++)
                            for (int wy = -halfW; wy <= halfW; wy++)
                            {
                                int ax = sx + wx, ay = sy + wy;
                                if (ax >= 0 && ay >= 0 && ax < w && ay < h && ax >= margin && ay >= margin && ax < w - margin && ay < h - margin)
                                    mask[ax, ay] = true;
                            }
                    }
                }
            }

            // 7) Final morphological cleanup to make shapes readable at small sprite sizes
            MorphologicalClean(mask, 1);

            // Slight random dilation/erosion for variety
            if (RNG.Rand.NextDouble() < 0.5) mask = Dilate(mask);
            if (RNG.Rand.NextDouble() < 0.5) mask = Erode(mask);

            return mask;
        }

        // --------------------- Utilities and helpers (kept / adapted) ---------------------

        // Create body - original logic (unchanged)
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

        // Create head - original logic (unchanged)
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

        // margin-aware ellipse (unchanged)
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

        // Mirror left->right with jitter (unchanged)
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

                    if (RNG.Rand.NextDouble() < jitterProb * 0.18) continue;

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

        // Closing (unchanged)
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
                for (int x = 0; x < w; x++)
                {
                    float v = dist[x, y];
                    if (x > 0) v = Math.Min(v, dist[x - 1, y] + 1f);
                    if (y > 0) v = Math.Min(v, dist[x, y - 1] + 1f);
                    if (x > 0 && y > 0) v = Math.Min(v, dist[x - 1, y - 1] + 1.41421356f);
                    if (x + 1 < w && y > 0) v = Math.Min(v, dist[x + 1, y - 1] + 1.41421356f);
                    dist[x, y] = v;
                }

            for (int y = h - 1; y >= 0; y--)
                for (int x = w - 1; x >= 0; x--)
                {
                    float v = dist[x, y];
                    if (x + 1 < w) v = Math.Min(v, dist[x + 1, y] + 1f);
                    if (y + 1 < h) v = Math.Min(v, dist[x, y + 1] + 1f);
                    if (x + 1 < w && y + 1 < h) v = Math.Min(v, dist[x + 1, y + 1] + 1.41421356f);
                    if (x > 0 && y + 1 < h) v = Math.Min(v, dist[x - 1, y + 1] + 1.41421356f);
                    dist[x, y] = v;
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
        static float SmoothStep(float t) => t * t * t * (t * (t * 6 - 15) + 10);
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

        // ------------------ Small helpers ------------------
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
