using System;
using System.Collections.Generic;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// <b>Разгон насквозь на старте боя</b> («Волчий разгон» наездника): носитель разгоняется к ближайшему
    /// врагу и проходит СКВОЗЬ строй, снося уроном всех на линии полёта, после чего возвращается к обычной
    /// охоте. Урон растёт со скоростью передвижения — замедление режет его пропорционально.
    /// <para><b>Числа:</b> <c>_distance</c> — длина разгона, мировые единицы; <c>_width</c> — ширина
    /// коридора; <c>_damageMultiplier</c> — множитель урона при базовой скорости; <c>_speedBaseline</c> и
    /// <c>_multPerSpeed</c> — от какой скорости и насколько круто множитель растёт (наездник: база 1.0,
    /// +1.5 за единицу сверх 1.0, что при его 3.63 даёт ×4.95); <c>_pctArmorPen</c> — доля игнорируемой
    /// брони (0.5 = половина); <c>_damageType</c> — чем бьёт разгон (у наездника Колющий).</para>
    /// <para><b>Когда срабатывает:</b> один раз за бой, первым тиком — счётчик эффекта служит отметкой
    /// «уже разогнался».</para>
    /// </summary>
    /// <remarks>
    /// <b>Почему не ветка <c>AbilityData.Displaces</c>.</b> Та реализует «Шквальный толчок» Монаха: рывок
    /// ВПЛОТНУЮ к цели с выбором стороны по «наковальне», чтобы толкнуть жертву в соседа. Наезднику нужно
    /// обратное — проехать цель насквозь и оказаться за строем, — и уложить оба смысла в одну ветку значило
    /// бы дать ей два хозяина.
    /// <para><b>Смещается САМ носитель</b> (<c>DisplaceRequest.Target</c> = он же): «ядро» на линии полёта
    /// и есть удар разгона, а бесплатным бонусом приходит вся уже написанная физика — удар о край арены,
    /// маркер полёта, реактивы на его окончание.</para>
    /// <para><b>Урон от скорости — свойство разгона, не кита:</b> считается в момент старта из текущего
    /// <see cref="StatType.MoveSpeed"/>, поэтому замедление, наложенное до боя или в первый тик, честно
    /// ослабляет удар (карточка: «замедло может хорошо снизить урон»).</para>
    /// </remarks>
    [Serializable]
    public sealed class ChargeThroughOnBattleStartComponent : IPeriodicComponent
    {
        private static readonly List<RuntimeUnit> Enemies = new List<RuntimeUnit>(32);

        /// <summary>Радиус поиска первой цели: заведомо больше арены — «ближайший из всех живых врагов».</summary>
        private const float SearchRadius = 100f;

        [Tooltip("Длина разгона, мировые единицы: должна хватать, чтобы пройти строй насквозь.")]
        [Min(0.5f)]
        [SerializeField] private float _distance = 6f;

        [Tooltip("Ширина коридора разгона, мировые единицы.")]
        [Min(0.1f)]
        [SerializeField] private float _width = 1.2f;

        [Tooltip("Множитель урона при базовой скорости (см. _speedBaseline).")]
        [Min(0f)]
        [SerializeField] private float _damageMultiplier = 1f;

        [Tooltip("Скорость передвижения, при которой работает базовый множитель.")]
        [Min(0f)]
        [SerializeField] private float _speedBaseline = 1f;

        [Tooltip("Прибавка к множителю за каждую единицу скорости сверх базовой.")]
        [Min(0f)]
        [SerializeField] private float _multPerSpeed = 1.5f;

        [Tooltip("Доля игнорируемой брони: 0.5 = разгон считает броню вдвое меньшей.")]
        [Range(0f, 1f)]
        [SerializeField] private float _pctArmorPen = 0.5f;

        [Tooltip("Тип урона разгона (наездник — Колющий).")]
        [SerializeField] private Data.Definitions.DamageType _damageType = Data.Definitions.DamageType.Pierce;

        public float Interval => 1f / Core.Simulation.SimConstants.TickRate;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnTick(in EffectContext ctx)
        {
            RuntimeEffect eff = ctx.Effect;
            if (eff.Counter != 0) return;   // разгон бывает один за бой

            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead || ctx.Combat == null) return;

            RuntimeUnit victim = NearestEnemy(self, ctx);
            if (victim == null) return;     // врагов ещё нет в списках — попробуем следующим тиком

            eff.Counter = 1;

            Vector2 toVictim = victim.Position - self.Position;
            Vector2 dir = toVictim.sqrMagnitude > 1e-6f ? toVictim.normalized : Vector2.right;

            float excess = self.Stats.Get(StatType.MoveSpeed) - _speedBaseline;
            float mult = excess > 0f ? _damageMultiplier + _multPerSpeed * excess : _damageMultiplier;
            float damage = self.Stats.Get(StatType.AutoAttackDamage) * mult;

            ctx.Combat.Displace(new DisplaceRequest(
                self, self, dir, _distance,
                cannonball: true, damage: damage, damageType: _damageType, width: _width,
                chainDistance: 0f, speedPerSecond: 0f, pctArmorPen: _pctArmorPen));
        }

        /// <summary>Ближайший живой враг; тай-брейк по Id — иначе разгон зеркальных сторон разошёлся бы.</summary>
        private static RuntimeUnit NearestEnemy(RuntimeUnit self, in EffectContext ctx)
        {
            Enemies.Clear();
            ctx.Combat.QueryUnitsInRadius(self.Position, SearchRadius, Enemies, TargetFilter.Enemies, self.Team);

            RuntimeUnit best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < Enemies.Count; i++)
            {
                RuntimeUnit u = Enemies[i];
                if (u == null || u.IsDead) continue;

                float sq = (u.Position - self.Position).sqrMagnitude;
                if (sq < bestSq || (sq == bestSq && best != null && u.Id < best.Id))
                {
                    bestSq = sq;
                    best = u;
                }
            }
            Enemies.Clear();
            return best;
        }
    }
}
