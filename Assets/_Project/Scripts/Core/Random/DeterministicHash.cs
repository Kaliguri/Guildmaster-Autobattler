namespace Guildmaster.Core.Random
{
    /// <summary>
    /// Стабильный хеш строк и сидов (FNV-1a). Единственный владелец правила «строка → число»
    /// для всего, что обязано совпадать между запусками, машинами и игроками: суб-сиды боёв,
    /// цены лавки, раскладка карты, изгиб дорожек.
    /// </summary>
    /// <remarks>
    /// <c>string.GetHashCode</c> здесь запрещён: .NET рандомизирует его от процесса к процессу, поэтому
    /// одна и та же карта раскладывалась бы после перезапуска игры иначе, а в коопе — у каждого по-своему.
    /// Формула была выписана в трёх местах (лавка, раскладка карты, дорожки) и разошлась бы на первой же
    /// правке; здесь она одна (аудит 2026-07-26, RC-8).
    /// </remarks>
    public static class DeterministicHash
    {
        private const ulong Offset64 = 14695981039346656037UL;
        private const ulong Prime64  = 1099511628211UL;

        private const uint Offset32 = 2166136261u;
        private const uint Prime32  = 16777619u;

        /// <summary>64-битный FNV-1a строки. <c>null</c> и пустая строка дают базовое смещение.</summary>
        public static ulong Of(string s)
        {
            ulong hash = Offset64;
            if (s != null)
                for (int i = 0; i < s.Length; i++) { hash ^= s[i]; hash *= Prime64; }
            return hash;
        }

        /// <summary>64-битный FNV-1a пары строк (порядок значим).</summary>
        public static ulong Of(string a, string b) => Mix(Of(a), Of(b));

        /// <summary>Домешать значение к уже посчитанному хешу — тем же шагом FNV.</summary>
        public static ulong Mix(ulong hash, ulong value)
        {
            unchecked
            {
                hash ^= value;
                hash *= Prime64;
                return hash;
            }
        }

        /// <summary>32-битный FNV-1a строки с досыпкой сида и соли — для раскладок и прочей визуальной вариации.</summary>
        public static uint Of32(string s, long seed, int salt)
        {
            unchecked
            {
                uint h = Offset32;
                if (s != null)
                    for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= Prime32; }
                h ^= (uint)seed;         h *= Prime32;
                h ^= (uint)(seed >> 32); h *= Prime32;
                h ^= (uint)salt;         h *= Prime32;
                return h;
            }
        }

        /// <summary>32-битный FNV-1a пары строк (порядок значим).</summary>
        public static uint Of32(string a, string b)
        {
            unchecked
            {
                uint h = Offset32;
                if (a != null) for (int i = 0; i < a.Length; i++) { h ^= a[i]; h *= Prime32; }
                if (b != null) for (int i = 0; i < b.Length; i++) { h ^= b[i]; h *= Prime32; }
                return h;
            }
        }
    }
}
