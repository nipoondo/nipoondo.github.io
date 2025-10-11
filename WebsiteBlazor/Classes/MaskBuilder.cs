// MaskBuilder.cs - drop this into your Classes project (replace existing file)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Security.AccessControl;
using WebsiteBlazor.Classes;

namespace AutoSpriteGenerator
{
    public static class MaskBuilder
    {
        // returns processed masks split into body and head so features/animations can use them separately.
        public static void BuildProcessedMasks(Settings settings, out bool[,] bodyMask, out bool[,] headMask)
        {
            // 1) build base shapes separately (these are deterministic-ish and stable)
            bool[,] baseBody = new bool[settings.Dimension, settings.Dimension];
            bool[,] baseHead = new bool[settings.Dimension, settings.Dimension];

            int archetype = DeterministicRandom.Next("BuildProcessedMasks", 8);
            int bodyStartY = settings.Dimension / 3;
            int bodyHeight = settings.Dimension - bodyStartY;
            int headHeight = bodyStartY;

            CreateBody(baseBody, settings, bodyStartY, bodyHeight);
            CreateHead(baseHead, settings, bodyStartY, headHeight);

            // 2) produce a varied body from the base body (noise, segments, lobes, spikes, holes)
            bool[,] variedBody = GenerateVariedBody(baseBody, bodyStartY, bodyHeight, settings);

            // 3) cleanup pipeline for the body only (NO head merged in)
            bool[,] processedBody = (bool[,])variedBody.Clone();
            AddPerimeterNoise(processedBody, 0.10, 0.05);
            MorphologicalClean(processedBody, 1);
            processedBody = ApplyOrganicSymmetry(processedBody, 0.18, settings.Margin);
            processedBody = CloseMask(processedBody, 1);
            EnforceMargin(processedBody, settings.Margin);

            // Ensure body does not contain any head pixels so the head can be positioned freely
            for (int x = 0; x < settings.Dimension; x++)
                for (int y = 0; y < settings.Dimension; y++)
                    if (baseHead[x, y]) processedBody[x, y] = false;

            // 4) outputs: body only (cleaned) and pure head (unchanged from base)
            bodyMask = processedBody;
            headMask = baseHead;
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

            NoisePresets.ComputeForWidth(w, settings.NoiseStyle, out float baseNoiseScale, out float amplitudePx, out int octaves, out float persistence, seed: settings.Seed);
            int targetCells = DeterministicRandom.Next("GenerateVariedBody", Math.Max(1, w / 24), Math.Max(2, w / 8)); // varied cell count

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

                    float fbm = SampleFbm(x * baseNoiseScale, y * baseNoiseScale, octaves, persistence, settings.Seed);
                    // value-noise based fbm roughly in [-1..1]; normalize by totalAmp
                    fbm /= totalAmp;
                    float displacement = fbm * amplitudePx * falloff;

                    float newSigned = signed[x, y] + displacement;
                    mask[x, y] = newSigned > 0f;
                }
            }

            // 2) segmented variants (sausage / armor plates)
            if (settings.UseSegments)
            {
                bool[,] seg = new bool[w, h];
                int segments = settings.NumberOfSegments;
                int cxBase = w / 2 + DeterministicRandom.Next("GenerateVariedBody", -3, 4);
                for (int i = 0; i < segments; i++)
                {
                    double t = segments == 1 ? 0.5 : (double)i / (segments - 1);
                    int cy = bodyStartY + (int)(t * bodyHeight) + DeterministicRandom.Next("GenerateVariedBody", -3, 4);
                    int rx = Math.Max(2, (int)(w * (0.14 + 0.24 * (0.5 + DeterministicRandom.NextDouble("GenerateVariedBody") * (1.0 - Math.Abs(0.5 - t))))));
                    int ry = Math.Max(2, (int)(bodyHeight * (0.10 + 0.18 * DeterministicRandom.NextDouble("GenerateVariedBody"))));
                    FillEllipseMask(seg, cxBase + DeterministicRandom.Next("GenerateVariedBody", -6, 7), cy, rx, ry, settings.Margin);
                }

                if (DeterministicRandom.NextDouble("GenerateVariedBody") < 0.6)
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
			var bbox = GetBoundingBox(mask);
			if (bbox.Width <= 0 || bbox.Height <= 0) bbox = GetBoundingBox(baseBody);

			if (settings.UseLobes)
            {
                int blobCount = settings.NumberOfLobes;

                for (int b = 0; b < blobCount; b++)
                {
                    int bx = DeterministicRandom.Next("GenerateVariedBody", Math.Max(settings.Margin, bbox.Left - 4), Math.Min(w - settings.Margin, bbox.Right + 4));
                    int by = DeterministicRandom.Next("GenerateVariedBody", Math.Max(settings.Margin, bbox.Top - 4), Math.Min(h - settings.Margin, bbox.Bottom + 4));
                    int brx = Math.Max(1, DeterministicRandom.Next("GenerateVariedBody", Math.Max(2, w / 24), Math.Max(2, w / 8)));
                    int bry = Math.Max(1, DeterministicRandom.Next("GenerateVariedBody", Math.Max(2, h / 32), Math.Max(2, h / 10)));
                    bool[,] blob = new bool[w, h];
                    FillEllipseMask(blob, bx, by, brx, bry, settings.Margin);

                    if (DeterministicRandom.NextDouble("GenerateVariedBody") < 0.72)
                    {
                        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) mask[x, y] = mask[x, y] || blob[x, y];
                    }
                    else
                    {
                        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (blob[x, y]) mask[x, y] = false;
                    }
                }
            }

            // 4) perforations (holes)
            if (settings.UseHoles)
            {
                int holes = settings.NumberOfHoles;
                for (int i = 0; i < holes; i++)
                {
                    int hx = DeterministicRandom.Next("GenerateVariedBody", Math.Max(settings.Margin, bbox.Left), Math.Min(w - settings.Margin, bbox.Right));
                    int hy = DeterministicRandom.Next("GenerateVariedBody", Math.Max(settings.Margin, bbox.Top), Math.Min(h - settings.Margin, bbox.Bottom));
                    int hrx = Math.Max(1, DeterministicRandom.Next("GenerateVariedBody", 1, Math.Max(2, w / 18)));
                    int hry = Math.Max(1, DeterministicRandom.Next("GenerateVariedBody", 1, Math.Max(2, h / 20)));
                    bool[,] hole = new bool[w, h];
                    FillEllipseMask(hole, hx, hy, hrx, hry, settings.Margin);
                    for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (hole[x, y]) mask[x, y] = false;
                }
            }

            // 5) spikes / protrusions along perimeter
            if (settings.UseProtrusions)
            {
                List<Point> edges = new List<Point>();
                for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (IsEdgeMask(mask, x, y)) edges.Add(new Point(x, y));
                if (edges.Count > 0)
                {
                    int spikeMax = Math.Max(0, Math.Min(12, edges.Count / 8 + DeterministicRandom.Next("GenerateVariedBody", 0, 5)));
                    for (int s = 0; s < spikeMax; s++)
                    {
                        var p = edges[DeterministicRandom.Next("GenerateVariedBody", edges.Count)];
                        int px = p.X, py = p.Y;
                        // gradient approx on signed field
                        float gx = SampleSafe(signed, px + 1, py) - SampleSafe(signed, px - 1, py);
                        float gy = SampleSafe(signed, px, py + 1) - SampleSafe(signed, px, py - 1);
                        float len = (float)Math.Sqrt(gx * gx + gy * gy);
                        float nx, ny;
                        if (len > 1e-6f) { nx = gx / len; ny = gy / len; }
                        else { nx = px - w / 2f; ny = py - (bodyStartY + bodyHeight / 2f); float nl = (float)Math.Sqrt(nx * nx + ny * ny) + 1e-6f; nx /= nl; ny /= nl; }

                        int spikeLen = DeterministicRandom.Next("GenerateVariedBody", Math.Max(2, w / 40), Math.Max(3, w / 18));
                        for (int l = 1; l <= spikeLen; l++)
                        {
                            int sx = px + (int)Math.Round(nx * l);
                            int sy = py + (int)Math.Round(ny * l);
                            int halfW = Math.Max(0, (int)Math.Round((1.0 - (double)l / spikeLen) * (1 + DeterministicRandom.Next("GenerateVariedBody", 0, 2))));
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
            if(settings.UseMorphologicClean)
                MorphologicalClean(mask, 1);
            if (settings.UseErosion)
                for (int i = 0; i < settings.NumberOfErosions; i++)
                    mask = Erode(mask);
            if (settings.UseDilatation)
                for (int i = 0; i < settings.NumberOfDilatations; i++)
                    mask = Dilate(mask);

            return mask;
        }

        // 3) Subtract helper used by ringed / hollow shapes
        static void SubtractEllipseMask(bool[,] mask, int cx, int cy, int rx, int ry, int margin)
        {
            int w = mask.GetLength(0), h = mask.GetLength(1);
            if (rx <= 0 || ry <= 0) return;
            bool[,] hole = new bool[w, h];
            FillEllipseMask(hole, cx, cy, rx, ry, margin);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (hole[x, y]) mask[x, y] = false;
        }

        // 4) Replace the old CreateBody with this richer version
        static void CreateBody(bool[,] mask, Settings settings, int bodyStartY, int bodyHeight)
        {
            int w = settings.Dimension, h = settings.Dimension;
            int cx = settings.Dimension / 2 + DeterministicRandom.Next("CreateBody", -4, 5);
            int midY = bodyStartY + bodyHeight / 2;
            // keep a little jitter to avoid too-perfect symmetry
            int jitterX = DeterministicRandom.Next("CreateBody", -3, 4);
            int jitterY = DeterministicRandom.Next("CreateBody", -2, 3);

            // if Auto selected, pick a non-Auto random archetype
            var archetype = settings.bodyArchetype;
            if (archetype == BodyArchetype.Random)
            {
                var vals = Enum.GetValues(typeof(BodyArchetype));
                archetype = (BodyArchetype)vals.GetValue(DeterministicRandom.Next("CreateBody", 1, vals.Length)); // skip Auto at 0
            }

            switch (archetype)
            {
                case BodyArchetype.Normal:
                    FillEllipseMask(mask, cx + jitterX, midY + jitterY, (int)(settings.Dimension * 0.36), (int)(bodyHeight * 0.45), settings.Margin);
                    break;

                case BodyArchetype.Thin:
                    FillEllipseMask(mask, cx + jitterX, midY, (int)(settings.Dimension * 0.18), (int)(bodyHeight * 0.78), settings.Margin);
                    // maybe a small side pouch
                    if (DeterministicRandom.NextDouble("CreateBody") < 0.4) FillEllipseMask(mask, cx - settings.Dimension / 5, midY - bodyHeight / 8, (int)(settings.Dimension * 0.12), (int)(bodyHeight * 0.18), settings.Margin);
                    break;

                case BodyArchetype.Tall:
                    FillEllipseMask(mask, cx + jitterX, bodyStartY + (int)(bodyHeight * 0.45) + jitterY, (int)(settings.Dimension * 0.22), (int)(bodyHeight * 0.92), settings.Margin);
                    break;

                case BodyArchetype.Wide:
                    FillEllipseMask(mask, cx + jitterX, midY + jitterY, (int)(settings.Dimension * 0.62), (int)(bodyHeight * 0.28), settings.Margin);
                    break;

                case BodyArchetype.Big:
                    FillEllipseMask(mask, cx + jitterX, midY + jitterY, (int)(settings.Dimension * 0.48), (int)(bodyHeight * 0.62), settings.Margin);
                    // add a lower belly bulge sometimes
                    if (DeterministicRandom.NextDouble("CreateBody") < 0.6) FillEllipseMask(mask, cx + DeterministicRandom.Next("CreateBody", -8, 9), bodyStartY + (int)(bodyHeight * 0.60), (int)(settings.Dimension * 0.28), (int)(bodyHeight * 0.28), settings.Margin);
                    break;

                case BodyArchetype.Tiny:
                    FillEllipseMask(mask, cx + DeterministicRandom.Next("CreateBody", -2, 3), bodyStartY + (int)(bodyHeight * 0.45), (int)(settings.Dimension * 0.18), (int)(bodyHeight * 0.22), settings.Margin);
                    break;

                case BodyArchetype.TopHeavy:
                    FillEllipseMask(mask, cx + jitterX, bodyStartY + (int)(bodyHeight * 0.20), (int)(settings.Dimension * 0.46), (int)(bodyHeight * 0.36), settings.Margin);
                    FillEllipseMask(mask, cx + DeterministicRandom.Next("CreateBody", -6, 7), bodyStartY + (int)(bodyHeight * 0.58), (int)(settings.Dimension * 0.14), (int)(bodyHeight * 0.18), settings.Margin);
                    break;

                case BodyArchetype.BottomHeavy:
                    FillEllipseMask(mask, cx + jitterX, bodyStartY + (int)(bodyHeight * 0.70), (int)(settings.Dimension * 0.46), (int)(bodyHeight * 0.36), settings.Margin);
                    FillEllipseMask(mask, cx + DeterministicRandom.Next("CreateBody", -6, 7), bodyStartY + (int)(bodyHeight * 0.28), (int)(settings.Dimension * 0.14), (int)(bodyHeight * 0.18), settings.Margin);
                    break;

                case BodyArchetype.Floating:
                    // place the body higher so bottom remains empty -> looks floating
                    int fcy = bodyStartY + (int)(bodyHeight * 0.22) + DeterministicRandom.Next("CreateBody", -2, 3);
                    FillEllipseMask(mask, cx + jitterX, fcy, (int)(settings.Dimension * 0.32), (int)(bodyHeight * 0.28), settings.Margin);
                    // optional little shadow / tether pixel lower down (small anchor)
                    if (DeterministicRandom.NextDouble("CreateBody") < 0.35) FillEllipseMask(mask, cx + DeterministicRandom.Next("CreateBody", -2, 3), fcy + (int)(bodyHeight * 0.36), Math.Max(1, (int)(settings.Dimension * 0.06)), Math.Max(1, (int)(bodyHeight * 0.06)), settings.Margin);
                    break;

                case BodyArchetype.Segmented:
                    {
                        int segments = DeterministicRandom.Next("CreateBody", 3, 6);
                        int segCx = cx + DeterministicRandom.Next("CreateBody", -4, 5);
                        int segW = Math.Max(2, settings.Dimension / 6);
                        for (int i = 0; i < segments; i++)
                        {
                            double t = segments == 1 ? 0.5 : (double)i / (segments - 1);
                            int cy = bodyStartY + (int)(t * bodyHeight) + DeterministicRandom.Next("CreateBody", -3, 4);
                            int rx = Math.Max(2, (int)(segW * (1.0 - 0.12 * i)));
                            int ry = Math.Max(2, (int)(bodyHeight * (0.10 + 0.12 * DeterministicRandom.NextDouble("CreateBody"))));
                            FillEllipseMask(mask, segCx + DeterministicRandom.Next("CreateBody", -4, 5), cy, rx, ry, settings.Margin);
                        }
                    }
                    break;

                case BodyArchetype.MultiLobed:
                    {
                        // central mass + 2..4 lobes around
                        FillEllipseMask(mask, cx + jitterX, midY + jitterY, (int)(settings.Dimension * 0.26), (int)(bodyHeight * 0.36), settings.Margin);
                        int lobes = DeterministicRandom.Next("CreateBody", 2, 5);
                        for (int i = 0; i < lobes; i++)
                        {
                            double ang = i * (Math.PI * 2.0 / lobes) + DeterministicRandom.NextDouble("CreateBody") * 0.5;
                            int lx = cx + (int)Math.Round(Math.Cos(ang) * settings.Dimension * 0.22) + DeterministicRandom.Next("CreateBody", -4, 5);
                            int ly = midY + (int)Math.Round(Math.Sin(ang) * bodyHeight * 0.18) + DeterministicRandom.Next("CreateBody", -3, 4);
                            FillEllipseMask(mask, lx, ly, (int)(settings.Dimension * 0.18), (int)(bodyHeight * 0.16), settings.Margin);
                        }
                    }
                    break;

                case BodyArchetype.Lopsided:
                    FillEllipseMask(mask, cx - (int)(settings.Dimension * 0.12), midY + DeterministicRandom.Next("CreateBody", -2, 3), (int)(settings.Dimension * 0.42), (int)(bodyHeight * 0.44), settings.Margin);
                    FillEllipseMask(mask, cx + (int)(settings.Dimension * 0.28), bodyStartY + (int)(bodyHeight * 0.36), (int)(settings.Dimension * 0.16), (int)(bodyHeight * 0.20), settings.Margin);
                    break;

                case BodyArchetype.Hourglass:
                    {
                        // two bulbs connected through a narrow waist
                        FillEllipseMask(mask, cx + DeterministicRandom.Next("CreateBody", -3, 4), bodyStartY + (int)(bodyHeight * 0.30), (int)(settings.Dimension * 0.32), (int)(bodyHeight * 0.22), settings.Margin);
                        FillEllipseMask(mask, cx + DeterministicRandom.Next("CreateBody", -3, 4), bodyStartY + (int)(bodyHeight * 0.62), (int)(settings.Dimension * 0.32), (int)(bodyHeight * 0.26), settings.Margin);
                        // narrow middle
                        FillEllipseMask(mask, cx + DeterministicRandom.Next("CreateBody", -2, 3), bodyStartY + (int)(bodyHeight * 0.46), (int)(settings.Dimension * 0.12), (int)(bodyHeight * 0.10), settings.Margin);
                    }
                    break;

                case BodyArchetype.Ringed:
                    {
                        // outer circle then subtract a smaller inner circle
                        FillEllipseMask(mask, cx + jitterX, midY, (int)(settings.Dimension * 0.42), (int)(bodyHeight * 0.42), settings.Margin);
                        SubtractEllipseMask(mask, cx + jitterX, midY, (int)(settings.Dimension * 0.20), (int)(bodyHeight * 0.20), settings.Margin);
                    }
                    break;

                case BodyArchetype.Spiky:
                    {
                        // base blob
                        FillEllipseMask(mask, cx + jitterX, midY, (int)(settings.Dimension * 0.34), (int)(bodyHeight * 0.36), settings.Margin);
                        // add spikes outwards from random perimeter points (simple local ellipses)
                        int spikes = DeterministicRandom.Next("CreateBody", 4, 12);
                        for (int s = 0; s < spikes; s++)
                        {
                            double ang = DeterministicRandom.NextDouble("CreateBody") * Math.PI * 2.0;
                            int sx = cx + (int)Math.Round(Math.Cos(ang) * settings.Dimension * 0.36) + DeterministicRandom.Next("CreateBody", -2, 3);
                            int sy = midY + (int)Math.Round(Math.Sin(ang) * bodyHeight * 0.36) + DeterministicRandom.Next("CreateBody", -2, 3);
                            int len = Math.Max(1, DeterministicRandom.Next("CreateBody", settings.Dimension / 24, settings.Dimension / 12));
                            FillEllipseMask(mask, sx, sy, len, Math.Max(1, len / 2), settings.Margin);
                        }
                    }
                    break;

                case BodyArchetype.Tentacled:
                    {
                        // central orb
                        FillEllipseMask(mask, cx + jitterX, midY - 1, (int)(settings.Dimension * 0.28), (int)(bodyHeight * 0.26), settings.Margin);
                        // tentacles are narrow ellipses that extend downward
                        int tent = DeterministicRandom.Next("CreateBody", 3, 7);
                        for (int t = 0; t < tent; t++)
                        {
                            int tx = cx + (int)Math.Round(Math.Cos(t * Math.PI * 2.0 / tent) * settings.Dimension * 0.32) + DeterministicRandom.Next("CreateBody", -3, 4);
                            int ty = midY + DeterministicRandom.Next("CreateBody", 2, 6) + (int)(bodyHeight * 0.12);
                            FillEllipseMask(mask, tx, ty, Math.Max(1, settings.Dimension / 18), Math.Max(1, bodyHeight / 10), settings.Margin);
                        }
                    }
                    break;

                case BodyArchetype.Worm:
                    FillEllipseMask(mask, cx + DeterministicRandom.Next("CreateBody", -6, 7), midY + DeterministicRandom.Next("CreateBody", -1, 2), (int)(settings.Dimension * 0.55), (int)(bodyHeight * 0.16), settings.Margin);
                    break;

                case BodyArchetype.Columnar:
                    FillEllipseMask(mask, cx + DeterministicRandom.Next("CreateBody", -2, 3), bodyStartY + (int)(bodyHeight * 0.50), (int)(settings.Dimension * 0.20), (int)(bodyHeight * 0.85), settings.Margin);
                    break;

                case BodyArchetype.Bulbous:
                    {
                        // stacked bulbs (3)
                        for (int i = 0; i < 3; i++)
                        {
                            int cy = bodyStartY + (int)((i + 0.5) / 3.0 * bodyHeight) + DeterministicRandom.Next("CreateBody", -2, 3);
                            int rx = (int)(settings.Dimension * (0.28 - 0.06 * i));
                            int ry = (int)(bodyHeight * (0.20 + 0.06 * i));
                            FillEllipseMask(mask, cx + DeterministicRandom.Next("CreateBody", -3, 4), cy, rx, ry, settings.Margin);
                        }
                    }
                    break;

                case BodyArchetype.Armored:
                    {
                        // overlapping plates
                        int plates = DeterministicRandom.Next("CreateBody", 3, 6);
                        for (int p = 0; p < plates; p++)
                        {
                            int cy = bodyStartY + (int)((p + 0.5) / plates * bodyHeight) + DeterministicRandom.Next("CreateBody", -2, 2);
                            int rx = Math.Max(2, (int)(settings.Dimension * (0.36 - p * 0.06)));
                            int ry = Math.Max(1, (int)(bodyHeight * 0.14));
                            FillEllipseMask(mask, cx + DeterministicRandom.Next("CreateBody", -5, 6), cy, rx, ry, settings.Margin);
                        }
                    }
                    break;

                case BodyArchetype.Split:
                    {
                        // left/right split (two halves with small gap)
                        FillEllipseMask(mask, cx - settings.Dimension / 8, midY, (int)(settings.Dimension * 0.28), (int)(bodyHeight * 0.42), settings.Margin);
                        FillEllipseMask(mask, cx + settings.Dimension / 8, midY + DeterministicRandom.Next("CreateBody", -2, 3), (int)(settings.Dimension * 0.28), (int)(bodyHeight * 0.42), settings.Margin);
                    }
                    break;

                case BodyArchetype.Dripping:
                    {
                        FillEllipseMask(mask, cx + jitterX, midY - 1, (int)(settings.Dimension * 0.34), (int)(bodyHeight * 0.34), settings.Margin);
                        // drops below
                        int drops = DeterministicRandom.Next("CreateBody", 1, 4);
                        for (int d = 0; d < drops; d++)
                        {
                            int dxp = cx + DeterministicRandom.Next("CreateBody", -6, 7);
                            int dyp = bodyStartY + (int)(bodyHeight * (0.75 + d * 0.05)) + DeterministicRandom.Next("CreateBody", 0, 3);
                            FillEllipseMask(mask, dxp, dyp, Math.Max(1, settings.Dimension / 24), Math.Max(1, bodyHeight / 18), settings.Margin);
                        }
                    }
                    break;

                case BodyArchetype.Jelly:
                    FillEllipseMask(mask, cx + jitterX, midY + jitterY, (int)(settings.Dimension * 0.40), (int)(bodyHeight * 0.38), settings.Margin);
                    break;

                case BodyArchetype.Flat:
                    FillEllipseMask(mask, cx + jitterX, midY + DeterministicRandom.Next("CreateBody", -1, 2), (int)(settings.Dimension * 0.58), (int)(bodyHeight * 0.12), settings.Margin);
                    break;

                case BodyArchetype.Orbital:
                    {
                        FillEllipseMask(mask, cx + jitterX, midY + jitterY, (int)(settings.Dimension * 0.28), (int)(bodyHeight * 0.28), settings.Margin);
                        int sats = DeterministicRandom.Next("CreateBody", 1, 4);
                        for (int s = 0; s < sats; s++)
                        {
                            int sx = cx + (int)Math.Round(Math.Cos(s * 2.0 * Math.PI / sats) * settings.Dimension * 0.30) + DeterministicRandom.Next("CreateBody", -3, 4);
                            int sy = midY + (int)Math.Round(Math.Sin(s * 2.0 * Math.PI / sats) * bodyHeight * 0.18) + DeterministicRandom.Next("CreateBody", -2, 3);
                            FillEllipseMask(mask, sx, sy, (int)(settings.Dimension * 0.12), (int)(bodyHeight * 0.10), settings.Margin);
                        }
                    }
                    break;

                case BodyArchetype.Asymmetric:
                    {
                        FillEllipseMask(mask, cx - settings.Dimension / 8, midY, (int)(settings.Dimension * 0.36), (int)(bodyHeight * 0.44), settings.Margin);
                        FillEllipseMask(mask, cx + settings.Dimension / 5, bodyStartY + (int)(bodyHeight * 0.3), (int)(settings.Dimension * 0.20), (int)(bodyHeight * 0.20), settings.Margin);
                        if (DeterministicRandom.NextDouble("CreateBody") < 0.4) FillEllipseMask(mask, cx + settings.Dimension / 3, bodyStartY + (int)(bodyHeight * 0.6), (int)(settings.Dimension * 0.10), (int)(bodyHeight * 0.12), settings.Margin);
                    }
                    break;

                case BodyArchetype.Layered:
                    {
                        int layers = DeterministicRandom.Next("CreateBody", 2, 5);
                        for (int L = 0; L < layers; L++)
                        {
                            int cy = bodyStartY + (int)((L + 0.5) / layers * bodyHeight);
                            FillEllipseMask(mask, cx + DeterministicRandom.Next("CreateBody", -3, 3), cy + DeterministicRandom.Next("CreateBody", -2, 3), (int)(settings.Dimension * (0.46 - L * 0.08)), (int)(bodyHeight * 0.12), settings.Margin);
                        }
                    }
                    break;

                case BodyArchetype.Radial:
                    {
                        // central + radial lobes
                        FillEllipseMask(mask, cx + jitterX, midY + jitterY, (int)(settings.Dimension * 0.22), (int)(bodyHeight * 0.22), settings.Margin);
                        int lobesCount = DeterministicRandom.Next("CreateBody", 4, 8);
                        for (int i = 0; i < lobesCount; i++)
                        {
                            double a = i * 2.0 * Math.PI / lobesCount;
                            int lx = cx + (int)Math.Round(Math.Cos(a) * settings.Dimension * 0.28);
                            int ly = midY + (int)Math.Round(Math.Sin(a) * bodyHeight * 0.22);
                            FillEllipseMask(mask, lx + DeterministicRandom.Next("CreateBody", -2, 3), ly + DeterministicRandom.Next("CreateBody", -2, 3), (int)(settings.Dimension * 0.12), (int)(bodyHeight * 0.10), settings.Margin);
                        }
                    }
                    break;

                case BodyArchetype.TreeLike:
                    {
                        // trunk
                        FillEllipseMask(mask, cx + DeterministicRandom.Next("CreateBody", -2, 3), bodyStartY + (int)(bodyHeight * 0.60), (int)(settings.Dimension * 0.14), (int)(bodyHeight * 0.46), settings.Margin);
                        // branches / masses
                        int branches = DeterministicRandom.Next("CreateBody", 2, 5);
                        for (int b = 0; b < branches; b++)
                        {
                            int bx = cx + (int)Math.Round((b - branches / 2.0) * settings.Dimension * 0.14) + DeterministicRandom.Next("CreateBody", -3, 4);
                            int by = bodyStartY + (int)(bodyHeight * (0.16 + b * 0.18)) + DeterministicRandom.Next("CreateBody", -2, 3);
                            FillEllipseMask(mask, bx, by, (int)(settings.Dimension * 0.18), (int)(bodyHeight * 0.14), settings.Margin);
                        }
                    }
                    break;

                default:
                    FillEllipseMask(mask, cx + jitterX, midY + jitterY, (int)(settings.Dimension * 0.36), (int)(bodyHeight * 0.45), settings.Margin);
                    break;
            }
        }

        static void CreateHead(bool[,] mask, Settings settings, int bodyStartY, int headHeight)
        {
            int headCx = settings.Dimension / 2 + DeterministicRandom.Next("CreateHead", -3, 4);
            int headCy = Math.Max(settings.Margin + 1, bodyStartY - headHeight / 3 + DeterministicRandom.Next("CreateHead", -2, 3));
            int headRx = (int)(settings.Dimension * 0.22) + DeterministicRandom.Next("CreateHead", -2, 3);
            int headRy = Math.Max(3, headHeight / 2 + DeterministicRandom.Next("CreateHead", -2, 3));

            FillEllipseMask(mask, headCx, headCy, headRx, headRy, settings.Margin);

            if (DeterministicRandom.NextDouble("CreateHead") < 0.45)
            {
                int hornY = headCy - headRy / 2;
                if (DeterministicRandom.NextDouble("CreateHead") < 0.5)
                {
                    FillEllipseMask(mask, headCx - headRx + 2, hornY - 2, 3, 3, settings.Margin);
                    FillEllipseMask(mask, headCx + headRx - 2, hornY - 2, 3, 3, settings.Margin);
                }
                else
                {
                    FillEllipseMask(mask, headCx, hornY - 3, 3, 4, settings.Margin);
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
                        if (IsEdgeMask(mask, x, y) && DeterministicRandom.NextDouble("AddPerimeterNoise") < removeProb) toRemove.Add(new Point(x, y));
                    }
                    else
                    {
                        if (HasAdjacentMask(mask, x, y) && DeterministicRandom.NextDouble("AddPerimeterNoise") < addProb) toAdd.Add(new Point(x, y));
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
            for (int x = 0; x < half; x++) colShift[x] = (DeterministicRandom.NextDouble("ApplyOrganicSymmetry") < 0.35) ? DeterministicRandom.Next("ApplyOrganicSymmetry", -1, 2) : 0;

            for (int x = 0; x < half; x++)
            {
                int mx = w - 1 - x;
                for (int y = 0; y < h; y++)
                {
                    if (!left[x, y]) continue;

                    int jitterY = (DeterministicRandom.NextDouble("ApplyOrganicSymmetry") < 0.28) ? DeterministicRandom.Next("ApplyOrganicSymmetry", -1, 2) : 0;
                    int ny = y + colShift[x] + jitterY;
                    if (ny < margin || ny >= h - margin) continue;
                    if (mx < margin || mx >= w - margin) continue;

                    if (DeterministicRandom.NextDouble("ApplyOrganicSymmetry") < jitterProb * 0.18) continue; // drop occasionally

                    result[mx, ny] = true;
                }

                if (DeterministicRandom.NextDouble("ApplyOrganicSymmetry") < jitterProb * 0.2)
                {
                    int probeY = DeterministicRandom.Next("ApplyOrganicSymmetry", margin, h - margin);
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
                    if (leftN || rightN) result[c, y] = (DeterministicRandom.NextDouble("ApplyOrganicSymmetry") < 0.9) ? true : (leftN && rightN);
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
