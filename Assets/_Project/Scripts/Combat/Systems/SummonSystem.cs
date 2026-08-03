using System.Collections.Generic;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Жизнь призванных тел (M10): срок жизни и связь с призывателем. Владелец полей
    /// <see cref="RuntimeUnit.SummonLifetimeRemaining"/> и <see cref="RuntimeUnit.DiesWithSummoner"/> —
    /// эта система и только она; <c>AbilitySystem</c> их заполняет в момент призыва.
    /// <para><b>Условия у каждого призыва свои</b> (решение Макса 2026-07-29): обычный призыв бессрочен и
    /// переживает хозяина, у некоторых есть срок, некоторые уходят вместе с призывателем. Поэтому здесь
    /// нет ни одного числа — только исполнение того, что объявил ассет способности.</para>
    /// <para><b>Смерть призыва — обычная смерть:</b> HP уводится в ноль, а помечает мёртвым
    /// <c>DeathSystem</c> в конце тика. Так призыв уходит тем же путём, что все, и презентация получает
    /// то же событие — иначе тело исчезало бы с арены без смерти, а лента показала бы пропажу без причины.</para>
    /// </summary>
    public sealed class SummonSystem
    {
        /// <summary>Продвинуть жизнь призывов на тик.</summary>
        /// <remarks>
        /// Двухфазности не требует: система читает только СВОЁ состояние (счётчик тиков, ссылку на
        /// призывателя) и пишет только в него плюс в HP умирающих. Порядок обхода на исход не влияет —
        /// смерть призыва не меняет условий смерти другого призыва в этом же тике.
        /// </remarks>
        public void Tick(IReadOnlyList<RuntimeUnit> units)
        {
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit unit = units[i];
                if (unit.IsDead || !unit.IsSummon) continue;

                // Хозяин пал: связанный призыв уходит с ним, независимый — остаётся.
                if (unit.DiesWithSummoner && (unit.Summoner == null || unit.Summoner.IsDead))
                {
                    Dissolve(unit);
                    continue;
                }

                if (unit.SummonLifetimeRemaining <= 0) continue;   // бессрочный — обычный случай

                unit.SummonLifetimeRemaining--;
                if (unit.SummonLifetimeRemaining <= 0) Dissolve(unit);
            }
        }

        /// <summary>Сколько живых призывов от этой способности держит юнит — по нему гейтится лимит.</summary>
        public static int CountLiveSummons(
            RuntimeUnit summoner, string abilityId, IReadOnlyList<RuntimeUnit> units)
        {
            if (summoner == null || string.IsNullOrEmpty(abilityId)) return 0;

            int count = 0;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit unit = units[i];
                if (unit.IsDead || !ReferenceEquals(unit.Summoner, summoner)) continue;
                if (unit.SummonAbilityId == abilityId) count++;
            }
            return count;
        }

        // Развеять: HP в ноль, дальше как у любой смерти. Своего «исчезновения» у призыва нет намеренно.
        private static void Dissolve(RuntimeUnit summon) => summon.CurrentHP = 0f;
    }
}
