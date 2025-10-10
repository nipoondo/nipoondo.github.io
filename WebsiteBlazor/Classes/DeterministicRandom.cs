using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;

namespace WebsiteBlazor.Classes
{
    public static class DeterministicRandom
    {
        // Must call Initialize(...) once at app start to set the global seed you want,
        // or the helper uses Environment.TickCount as a default.
        private static int _globalSeed = Environment.TickCount;
        private static readonly ConcurrentDictionary<string, Random> _rngs = new(StringComparer.Ordinal);

        public static void Initialize(int seed)
        {
            _globalSeed = seed;
            _rngs.Clear();
        }

        // -------------------------
        // Public API (recommended)
        // -------------------------
        // Named stream:
        public static int Next(string key) => NextInternal(key, () => GetRng(key).Next());
        public static int Next(string key, int maxValue) => NextInternal(key, () => GetRng(key).Next(maxValue));
        public static int Next(string key, int minValue, int maxValue) => NextInternal(key, () => GetRng(key).Next(minValue, maxValue));
        public static double NextDouble(string key) => NextInternal(key, () => GetRng(key).NextDouble());
        public static void NextBytes(string key, byte[] buffer) => NextInternal(key, () => { GetRng(key).NextBytes(buffer); return 0; });

        // Call-site keyed stream (no need to provide key — unique per source line)
        public static int NextForCallSite(int minValue, int maxValue, string label = null,
            [CallerFilePath] string callerFile = "", [CallerMemberName] string callerMember = "", [CallerLineNumber] int callerLine = 0)
        {
            string key = BuildCallSiteKey(callerFile, callerMember, callerLine, label);
            return Next(key, minValue, maxValue);
        }

        public static int NextForCallSite(string label = null,
            [CallerFilePath] string callerFile = "", [CallerMemberName] string callerMember = "", [CallerLineNumber] int callerLine = 0)
        {
            string key = BuildCallSiteKey(callerFile, callerMember, callerLine, label);
            return Next(key);
        }

        public static double NextDoubleForCallSite(string label = null,
            [CallerFilePath] string callerFile = "", [CallerMemberName] string callerMember = "", [CallerLineNumber] int callerLine = 0)
        {
            string key = BuildCallSiteKey(callerFile, callerMember, callerLine, label);
            return NextDouble(key);
        }

        // -------------------------
        // Internals
        // -------------------------
        private static string BuildCallSiteKey(string file, string member, int line, string label)
        {
            var sb = new StringBuilder();
            sb.Append(file).Append(':').Append(member).Append(':').Append(line);
            if (!string.IsNullOrEmpty(label))
            {
                sb.Append(':').Append(label);
            }
            return sb.ToString();
        }

        private static Random GetRng(string key)
        {
            return _rngs.GetOrAdd(key, k => new Random(ComputeSeedForKey(k)));
        }

        // We lock on the Random instance to make it thread-safe.
        private static T NextInternal<T>(string key, Func<T> action)
        {
            var rng = GetRng(key);
            lock (rng)
            {
                return action();
            }
        }

        // Compute a deterministic seed from global seed + key string using a stable hash (FNV-1a -> mix).
        private static int ComputeSeedForKey(string key)
        {
            // FNV-1a 32-bit
            const uint offsetBasis = 2166136261u;
            const uint prime = 16777619u;

            uint hash = offsetBasis;
            byte[] bytes = Encoding.UTF8.GetBytes(key);
            foreach (var b in bytes)
            {
                hash ^= b;
                hash *= prime;
            }

            // mix in the global seed using a 32-bit mix (like boost::hash_combine)
            uint g = (uint)_globalSeed;
            uint mixed = hash ^ (g + 0x9e3779b9u + (hash << 6) + (hash >> 2));
            return unchecked((int)mixed);
        }
    }
}
