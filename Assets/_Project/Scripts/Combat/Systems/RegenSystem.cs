using System.Collections.Generic;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Регенерация HP живых юнитов за тик: <c>(HpRegenFlat + HpRegenPct × MaxHP) × dt</c>,
    /// с клампом по MaxHP. Реализует стат-блок выживаемости Фазы 1 (вики «11» §3.1).
    /// </summary>
    /// <remarks>
    /// Тикает ПЕРЕД <see cref="EffectSystem"/>: реген применяется до DoT этого тика, поэтому
    /// не «вытягивает» юнита из летального урона задним числом. Юнит с <c>CurrentHP ≤ 0</c>
    /// (помечен к смерти на этом тике, но ещё не обработан <see cref="DeathSystem"/>) не
    /// регенерируется — воскрешать его не должно.
    /// </remarks>
    public sealed class RegenSystem
    {
        /// <summary>Применить регенерацию ко всем живым юнитам за один тик.</summary>
        public void Tick(List<RuntimeUnit> units, float dt)
        {
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit unit = units[i];
                if (unit.IsDead || unit.CurrentHP <= 0f) continue;

                float flat = unit.Stats.Get(StatType.HpRegenFlat);
                float pct  = unit.Stats.Get(StatType.HpRegenPct);
                if (flat <= 0f && pct <= 0f) continue;

                float maxHp = unit.Stats.Get(StatType.MaxHP);
                float regen = (flat + pct * maxHp) * dt;
                if (regen <= 0f) continue;

                unit.CurrentHP = Mathf.Min(unit.CurrentHP + regen, maxHp);
            }
        }
    }
}
