using AutoSpriteCreator;

namespace WebsiteBlazor.Classes
{
    public enum NoiseStyle { Blobby, Balanced, Detailed }

    public static class NoisePresets
    {
        public static void ComputeForWidth(int width, NoiseStyle style,
            out float baseNoiseScale, out float amplitudePx, out int octaves, out float persistence, int? seed = null)
        {
            if (width < 8) width = 8;

            // reproducible RNG if user supplies seed, otherwise use global RNG
            Random rnd = seed.HasValue ? new Random(seed.Value) : new Random(RNG.Rand.Next());

            // Hard, well-separated ranges per style (designed for 32..128px sprites)
            int minCells, maxCells;
            float minAmpFactor, maxAmpFactor;
            int minOct, maxOct;
            float minPers, maxPers;
            float scaleMultiplier;

            switch (style)
            {
                // Big smooth blobs, strong silhouette displacement, low detail
                case NoiseStyle.Blobby:
                    minCells = 1; maxCells = 3;            // very few cells across width
                    minAmpFactor = 0.08f; maxAmpFactor = 0.18f; // large amplitude relative to width
                    minOct = 1; maxOct = 2;                // few octaves (smooth)
                    minPers = 0.28f; maxPers = 0.48f;      // low persistence (attenuate high freq)
                    scaleMultiplier = 0.60f;               // push scale smaller (bigger blobs)
                    break;

                // Middle-of-the-road: recognizable silhouette changes, moderate detail
                case NoiseStyle.Balanced:
                    minCells = 3; maxCells = 5;
                    minAmpFactor = 0.05f; maxAmpFactor = 0.11f;
                    minOct = 2; maxOct = 3;
                    minPers = 0.45f; maxPers = 0.60f;
                    scaleMultiplier = 1.00f;
                    break;

                // Lots of tight detail (many wiggles), small silhouette displacement
                default: // Detailed
                    minCells = 6; maxCells = 12;
                    minAmpFactor = 0.02f; maxAmpFactor = 0.06f; // small amplitude so wiggles are fine-grained
                    minOct = 3; maxOct = 5;                       // many octaves
                    minPers = 0.55f; maxPers = 0.80f;             // keep higher persistence so fine octaves are visible
                    scaleMultiplier = 1.60f;                      // push scale larger -> more cells (higher freq)
                    break;
            }

            // pick discrete targetCells and float interpolation for factors
            int targetCells = rnd.Next(minCells, maxCells + 1);
            float t = (float)rnd.NextDouble();
            float ampFactor = minAmpFactor + t * (maxAmpFactor - minAmpFactor);

            // baseNoiseScale = targetCells / width, then bias by style multiplier for stronger separation
            baseNoiseScale = (float)targetCells / (float)width;
            baseNoiseScale *= scaleMultiplier;

            // amplitude is fraction of width (px)
            amplitudePx = width * ampFactor;

            // octaves & persistence
            octaves = rnd.Next(minOct, maxOct + 1);
            persistence = minPers + (float)rnd.NextDouble() * (maxPers - minPers);

            // Safety / sanity clamps (hard limits)
            baseNoiseScale = float.Clamp(baseNoiseScale, 0.004f, 0.6f);
            amplitudePx = float.Clamp(amplitudePx, Math.Max(0.5f, width * 0.02f), width * 0.35f);
            persistence = float.Clamp(persistence, 0.20f, 0.95f);

            // Balance: more octaves should generally reduce silhouette amplitude (avoid over-busy edges)
            // stronger reduction for Detailed style where octaves can be large
            float octavePenalty = 1.0f / (1.0f + 0.16f * Math.Max(0, octaves - 2));
            amplitudePx *= octavePenalty;

            // final small rounding to produce cleaner logs/inspection
            amplitudePx = (float)Math.Round(amplitudePx, 3);
            baseNoiseScale = (float)Math.Round(baseNoiseScale, 6);
            persistence = (float)Math.Round(persistence, 3);
        }
    }
}
