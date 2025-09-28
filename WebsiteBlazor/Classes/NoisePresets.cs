using AutoSpriteCreator;

namespace WebsiteBlazor.Classes
{
    public static class NoisePresets
    {
        public static void ComputeForWidth(int width, NoiseStyle style,
            out float baseNoiseScale, out float amplitudePx, out int octaves, out float persistence, int? seed = null)
        {
            if (width < 8) width = 8;

            Random rnd = seed.HasValue ? new Random(seed.Value) : new Random(RNG.Rand.Next());

            int minCells, maxCells;
            float minAmpFactor, maxAmpFactor;
            int minOct, maxOct;
            float minPers, maxPers;
            float scaleMultiplier;

            switch (style)
            {
                case NoiseStyle.Blobby:
                    minCells = 1; maxCells = 3;
                    minAmpFactor = 0.08f; maxAmpFactor = 0.18f;
                    minOct = 1; maxOct = 2;
                    minPers = 0.28f; maxPers = 0.48f;
                    scaleMultiplier = 0.60f;
                    break;
                case NoiseStyle.Balanced:
                    minCells = 3; maxCells = 5;
                    minAmpFactor = 0.05f; maxAmpFactor = 0.11f;
                    minOct = 2; maxOct = 3;
                    minPers = 0.45f; maxPers = 0.60f;
                    scaleMultiplier = 1.00f;
                    break;
                case NoiseStyle.Detailed:
                    minCells = 6; maxCells = 12;
                    minAmpFactor = 0.02f; maxAmpFactor = 0.06f;
                    minOct = 3; maxOct = 5;
                    minPers = 0.55f; maxPers = 0.80f;
                    scaleMultiplier = 1.60f;
                    break;
                default:
                    minCells = 1; maxCells = 12;
                    minAmpFactor = 0.01f; maxAmpFactor = 0.30f;
                    minOct = 1; maxOct = 5;
                    minPers = 0.10f; maxPers = 0.90f;
                    scaleMultiplier = 1.60f;
                    break;
            }

            int targetCells = rnd.Next(minCells, maxCells + 1);
            float t = (float)rnd.NextDouble();
            float ampFactor = minAmpFactor + t * (maxAmpFactor - minAmpFactor);

            baseNoiseScale = (float)targetCells / (float)width;
            baseNoiseScale *= scaleMultiplier;

            amplitudePx = width * ampFactor;

            octaves = rnd.Next(minOct, maxOct + 1);
            persistence = minPers + (float)rnd.NextDouble() * (maxPers - minPers);

            // portable clamps
            baseNoiseScale = Clamp(baseNoiseScale, 0.004f, 0.6f);
            amplitudePx = Clamp(amplitudePx, Math.Max(0.5f, width * 0.02f), width * 0.35f);
            persistence = Clamp(persistence, 0.20f, 0.95f);

            float octavePenalty = 1.0f / (1.0f + 0.16f * Math.Max(0, octaves - 2));
            amplitudePx *= octavePenalty;

            amplitudePx = (float)Math.Round(amplitudePx, 3);
            baseNoiseScale = (float)Math.Round(baseNoiseScale, 6);
            persistence = (float)Math.Round(persistence, 3);
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }

}
