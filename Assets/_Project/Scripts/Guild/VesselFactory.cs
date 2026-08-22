using System.Collections.Generic;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Рождение процедурного «Сосуда»: из сида разворачиваются имя и перки, и всё это ложится в
    /// <see cref="VesselState"/>. Чистая функция без DI — её зовут и дом при основании, и наём.
    /// </summary>
    /// <remarks>
    /// <b>Хранится сид, а не собранная строка</b> (<c>VesselState.BirthSeed</c>, «меняться не должен
    /// никогда»): правка пула имён обязана доезжать до уже нанятых, а замороженная в сейве строка
    /// осталась бы старой навсегда.
    /// <para><b>Перки выдаются роллом, и это временно.</b> По ГДД игрок выбирает их мини-игрой при
    /// найме (<c>relics-overview</c> §Перки), либо отказывается от выбора режимом «Истинный герой» —
    /// но мини-игры пока нет вовсе, и человек без перков читался бы как недоделанный. Ролл честный,
    /// от того же сида, поэтому воспроизводим; когда мини-игра появится, она встанет ПЕРЕД этим
    /// вызовом и передаст выбранное.</para>
    /// </remarks>
    public static class VesselFactory
    {
        /// <summary>
        /// Родить человека. <paramref name="birthSeed"/> задаёт и имя, и перки — один и тот же сид
        /// всегда даёт одного и того же человека.
        /// </summary>
        /// <param name="traits">Пул перков; пусто — человек родится без них (с предупреждением).</param>
        public static VesselState Create(long birthSeed, VesselNamePool names, IReadOnlyList<TraitData> traits)
        {
            var rng = new XorShiftRng(unchecked((ulong)birthSeed));

            var vessel = new VesselState
            {
                Id        = "vessel." + birthSeed.ToString("x"),
                BirthSeed = birthSeed,
                Name      = ComposeName(rng, names),
            };

            AssignTraits(vessel, rng, traits);
            return vessel;
        }

        /// <summary>Имя из фрагментов пула. Пустой пул — незаполненный ассет, а не «имён не бывает».</summary>
        private static string ComposeName(XorShiftRng rng, VesselNamePool pool)
        {
            if (pool == null || pool.IsEmpty)
            {
                Debug.LogError("[VesselFactory] - пул имён пуст или не задан: человек родится с техническим ярлыком");
                return "Безымянный";
            }

            string[] names = pool.Names;
            string name = names[(int)(rng.NextUInt() % (uint)names.Length)];

            string[] epithets = pool.Epithets;
            if (epithets == null || epithets.Length == 0) return name;

            // Прозвище не у каждого: половина людей остаётся с одним именем, иначе ростер читается
            // как список титулов и перестаёт различаться на глаз.
            if ((rng.NextUInt() & 1u) == 0u) return name;

            string epithet = epithets[(int)(rng.NextUInt() % (uint)epithets.Length)];
            return string.IsNullOrEmpty(epithet) ? name : name + ", " + epithet;
        }

        /// <summary>
        /// Положительный и отрицательный перки. Берутся из разных половин пула по полярности: человек
        /// без минуса — это не «повезло», а нарушение модели (перков ровно два, плюс и минус).
        /// </summary>
        private static void AssignTraits(VesselState vessel, XorShiftRng rng, IReadOnlyList<TraitData> traits)
        {
            if (traits == null || traits.Count == 0)
            {
                Debug.LogWarning("[VesselFactory] - пул перков пуст: человек родится без перков");
                return;
            }

            var positive = new List<TraitData>();
            var negative = new List<TraitData>();
            for (int i = 0; i < traits.Count; i++)
            {
                TraitData trait = traits[i];
                if (trait == null) continue;
                if (trait.Polarity == TraitPolarity.Positive) positive.Add(trait);
                else negative.Add(trait);
            }

            vessel.PositiveTraitId = PickId(rng, positive);
            vessel.NegativeTraitId = PickId(rng, negative);
        }

        private static string PickId(XorShiftRng rng, List<TraitData> pool)
        {
            if (pool.Count == 0) return string.Empty;
            return pool[(int)(rng.NextUInt() % (uint)pool.Count)].Id;
        }
    }
}
