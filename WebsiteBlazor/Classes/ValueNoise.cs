using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoSpriteCreator
{
    public class ValueNoise
    {
        private readonly int seed;
        public int Octaves { get; set; } = 4;
        public double Period { get; set; } = 30.0;
        public double Persistence { get; set; } = 0.5;
        public double Lacunarity { get; set; } = 2.0;

        public ValueNoise(int seed)
        {
            this.seed = seed;
        }

        // returns approx [-1..1]
        public double GetNoise2D(double x, double y)
        {
            double total = 0.0;
            double amplitude = 1.0;
            double frequency = 1.0;
            double maxAmp = 0.0;

            for (int o = 0; o < Math.Max(1, Octaves); o++)
            {
                double nx = (x * frequency) / Math.Max(1.0, Period);
                double ny = (y * frequency) / Math.Max(1.0, Period);
                total += SingleNoise(nx, ny, seed + o * 1009) * amplitude;
                maxAmp += amplitude;
                amplitude *= Persistence;
                frequency *= Lacunarity;
            }

            if (maxAmp == 0) return 0;
            return Math.Max(-1.0, Math.Min(1.0, total / maxAmp));
        }

        // 2D value noise: hashed corner values + smooth interpolation
        private static double SingleNoise(double x, double y, int s)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double tx = x - x0, ty = y - y0;
            // smoothstep
            double sx = tx * tx * (3 - 2 * tx);
            double sy = ty * ty * (3 - 2 * ty);

            double v00 = HashToDouble(x0, y0, s);
            double v10 = HashToDouble(x0 + 1, y0, s);
            double v01 = HashToDouble(x0, y0 + 1, s);
            double v11 = HashToDouble(x0 + 1, y0 + 1, s);

            double ix0 = Lerp(v00, v10, sx);
            double ix1 = Lerp(v01, v11, sx);
            double value = Lerp(ix0, ix1, sy);

            return value;
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        // deterministic hash -> [-1..1]
        private static double HashToDouble(int x, int y, int seed)
        {
            unchecked
            {
                // A simple but decent integer hash
                int n = x * 374761393 + y * 668265263 + seed * 69069;
                n = (n ^ (n >> 13)) * 1274126177;
                n = n ^ (n >> 16);
                // map to [0,1]
                uint un = (uint)n;
                double v = (un & 0x7FFFFFFF) / (double)int.MaxValue;
                return v * 2.0 - 1.0; // [-1..1]
            }
        }
    }

}
