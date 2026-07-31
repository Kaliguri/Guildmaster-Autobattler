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
        /// <summary>
        /// БАЗОВАЯ скорость набора ресурса, единиц в секунду — одинаковая у всех (решение 2026-07-27).
        /// Публичное поле: стартовое значение засевает боевой скоуп из <c>StatsConfig</c> (единственный
        /// источник), здесь — код-дефолт для headless-конструирования без конфига.
        /// <para>Итоговая скорость юнита — <c>(база + ResourceRegenFlat) × ResourceGainEff</c>, то есть
        /// «одинаково у всех» с 2026-07-31 верно только про базу: эффекты могут её сдвигать плоско.</para>
        /// </summary>
        /// <remarks>
        /// Ресурс капает по времени, а не с попаданий: пока он набирался ударами, скорость атаки была
        /// двойным бустом (быстрее бьёшь → чаще ульта), а темп способности зависел ещё и от того,
        /// дотянулся ли кит до цели. Механизм набора с удара (<c>UnitData.ResourceOnHit</c>) намеренно
        /// оставлен в коде — он понадобится Ярости, когда её заведут.
        /// </remarks>
        public float ResourcePerSecond = 5f;

        /// <summary>Применить регенерацию HP и ресурса ко всем живым юнитам за один тик.</summary>
        public void Tick(List<RuntimeUnit> units, float dt)
        {
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit unit = units[i];
                if (unit.IsDead || unit.CurrentHP <= 0f) continue;

                RegenHp(unit, dt);
                RegenResource(unit, dt);
            }
        }

        private static void RegenHp(RuntimeUnit unit, float dt)
        {
            float flat = unit.Stats.Get(StatType.HpRegenFlat);
            float pct  = unit.Stats.Get(StatType.HpRegenPct);
            if (flat <= 0f && pct <= 0f) return;

            float maxHp = unit.Stats.Get(StatType.MaxHP);
            float regen = (flat + pct * maxHp) * dt;
            if (regen <= 0f) return;

            unit.CurrentHP = Mathf.Min(unit.CurrentHP + regen, maxHp);
        }

        private void RegenResource(RuntimeUnit unit, float dt)
        {
            float maxResource = unit.Stats.Get(StatType.MaxResource);
            if (maxResource <= 0f) return;              // кит без ресурса (Убийца) — копить нечего
            if (unit.CurrentResource >= maxResource) return;

            // Плоская дельта складывается с базой ДО множителя и может утащить скорость в минус
            // (мана-дрейн проказника: -4 из базовых 5). Отрицательный набор — это НЕ утечка уже
            // накопленного: дебафф обещает «медленнее восстанавливается», а не «сосёт ману», поэтому
            // ниже нуля скорость клампится, а не разворачивается.
            float perSecond = ResourcePerSecond + unit.Stats.Get(StatType.ResourceRegenFlat);
            if (perSecond <= 0f) return;

            float gain = perSecond * unit.Stats.Get(StatType.ResourceGainEff) * dt;
            if (gain <= 0f) return;

            unit.CurrentResource = Mathf.Min(unit.CurrentResource + gain, maxResource);
        }
    }
}
