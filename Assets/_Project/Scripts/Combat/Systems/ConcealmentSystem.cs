using System.Collections.Generic;
using Guildmaster.Combat.Effects;
using Guildmaster.Combat.Effects.Components;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Кто сейчас скрыт: собирает сильнейшую ступень Маскировки с активных эффектов каждого юнита и
    /// решает, заметил ли его противник. Один проход на весь бой, до выбора целей.
    /// </summary>
    /// <remarks>
    /// <b>Почему система, а не поле, которое ставит компонент:</b> маскировок на юните может оказаться
    /// две (пассив плюс активка), и снятие одной обнулило бы поле, оставив вторую висеть без действия.
    /// Пересчёт по списку такой гонки не знает — и заодно делает «сильнейшая побеждает» одним правилом
    /// в одном месте.
    /// <para><b>Обнаружение командное</b> (решение Макса 2026-07-31): достаточно, чтобы ОДИН враг был
    /// ближе радиуса, — видит вся его команда. Личное обнаружение дало бы картину, которую нельзя
    /// прочитать глазами: игрок не понимает, кто именно его видит, а читаемость боя у нас доктрина.</para>
    /// <para><b>Обнаружение не залипает:</b> отошли все — юнит снова пропал. Иначе маскировка была бы
    /// «бесплатным первым ходом» и больше ничем, а Убийце некуда было бы возвращаться из боя.</para>
    /// <para><b>Порядок в тике:</b> до <c>BrainSystem</c>, по позициям начала тика. Это тот же закон
    /// видимости, что у эффектов: все решают по одному снимку мира, а не по тому, кто раньше в списке.</para>
    /// </remarks>
    public sealed class ConcealmentSystem
    {
        public void Tick(IReadOnlyList<RuntimeUnit> units, in SimTuning tuning)
        {
            // Проход 1: чья маскировка какой ступени. Считаем всем, включая только что потерявших её, —
            // иначе снятый эффект оставил бы юнита скрытым до следующего тика.
            bool anyConcealed = false;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit unit = units[i];
                ConcealmentTier tier = StrongestTier(unit);
                unit.ConcealTier = tier;

                if (tier == ConcealmentTier.None) { unit.Revealed = false; continue; }

                // Инвиз расстоянием не снимается вовсе — только своим действием (атака, каст).
                unit.Revealed = false;
                if (tier != ConcealmentTier.Invisible) anyConcealed = true;
            }

            if (!anyConcealed) return;

            // Проход 2: кого заметили. Квадратичный обход по замаскированным, а не запрос к
            // SpatialHash: замаскированных в бою единицы, а хэш на этой фазе тика ещё описывает
            // прошлые позиции — брать из него значило бы смешать два снимка мира.
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit hider = units[i];
                if (hider.IsDead || hider.ConcealTier == ConcealmentTier.None) continue;
                if (hider.ConcealTier == ConcealmentTier.Invisible) continue;

                float radius = RadiusOf(hider.ConcealTier, in tuning);
                if (radius <= 0f) continue;
                float radiusSq = radius * radius;

                for (int j = 0; j < units.Count; j++)
                {
                    RuntimeUnit seeker = units[j];
                    if (seeker.IsDead || seeker.Team == hider.Team) continue;
                    if ((seeker.Position - hider.Position).sqrMagnitude > radiusSq) continue;

                    hider.Revealed = true;
                    break;
                }
            }
        }

        /// <summary>
        /// Радиус обнаружения ступени. Живёт здесь, а не в <see cref="SimTuning"/>, по границе сборок:
        /// <c>Guildmaster.Core</c> не ссылается на <c>Guildmaster.Data</c> и про ступени не знает —
        /// снимок держит только числа, а какое из них чьё, решает бой.
        /// <para>Инвиз и None дают 0: «не обнаруживается расстоянием». Звать метод для них можно, но
        /// решать по числу нельзя — для того и есть сама ступень.</para>
        /// </summary>
        public static float RadiusOf(ConcealmentTier tier, in SimTuning tuning)
        {
            switch (tier)
            {
                case ConcealmentTier.Weak:   return tuning.ConcealWeakRadius;
                case ConcealmentTier.Medium: return tuning.ConcealMediumRadius;
                case ConcealmentTier.Strong: return tuning.ConcealStrongRadius;
                default: return 0f;
            }
        }

        /// <summary>Сильнейшая ступень среди активных эффектов носителя; None — маскировки нет.</summary>
        private static ConcealmentTier StrongestTier(RuntimeUnit unit)
        {
            ConcealmentTier best = ConcealmentTier.None;

            for (int i = 0; i < unit.ActiveEffects.Count; i++)
            {
                RuntimeEffect effect = unit.ActiveEffects[i];
                IEffectComponent[] components = effect.Def != null ? effect.Def.Components : null;
                if (components == null) continue;

                for (int c = 0; c < components.Length; c++)
                {
                    if (components[c] is not ConcealmentComponent conceal) continue;
                    if (conceal.Tier > best) best = conceal.Tier;
                }
            }

            return best;
        }
    }
}
