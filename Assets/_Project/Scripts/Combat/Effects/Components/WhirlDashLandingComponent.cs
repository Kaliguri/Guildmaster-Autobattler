using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Приземление рывка монаха (§10.6, «Шквальный толчок», фаза 3 из 4: рывок → фиксация → ОТБРАСЫВАНИЕ →
    /// телепорт). Реактив на конец эффекта смещения (<see cref="CombatEvent.EffectExpired"/> с тегом
    /// <see cref="EffectTag.KnockUp"/>), где смещённый — САМ носитель (конец собственного рывка, по команде).
    /// В этот момент монах уже вплотную к цели → отталкивает ближайшего врага «от себя вперёд» + «ядро» по
    /// линии. Конец этого отбрасывания (смещённый = враг) поймает <see cref="VortexEntryComponent"/> → телепорт.
    /// </summary>
    [Serializable]
    public sealed class WhirlDashLandingComponent : IReactiveComponent
    {
        [Tooltip("Дистанция отбрасывания цели (мировые единицы).")]
        [SerializeField] private float _displaceDistance = 4f;
        [Tooltip("Длительность полёта отбрасывания в тиках (30/сек).")]
        [SerializeField] private int _displaceTicks = 12;
        [Tooltip("Множитель урона-«ядра» от AutoAttackDamage монаха (0 = без урона на линии).")]
        [SerializeField] private float _displaceDamageMult = 1.5f;
        [Tooltip("Ширина линии «ядра» (мировые единицы).")]
        [SerializeField] private float _displaceWidth = 1.5f;

        [Tooltip("Слабое цепное отбрасывание врагов, задетых «ядром»: дистанция (мировые ед.). Держать МАЛЕНЬКИМ, чтобы цепь кончалась раньше главного полёта и финальный телепорт сел на исходную цель. 0 = только урон по задетым.")]
        [SerializeField] private float _chainDistance = 0.6f;
        [Tooltip("Длительность цепного полёта в тиках. Короче главного (_displaceTicks).")]
        [SerializeField] private int _chainTicks = 4;

        public CombatEvent Events => CombatEvent.EffectExpired;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            // Только конец смещения (KnockUp), и только когда смещался САМ носитель = конец рывка.
            if (e.Type != CombatEvent.EffectExpired || (e.Tags & EffectTag.KnockUp) == 0) return;

            RuntimeUnit monk = ctx.Target;             // носитель = источник смещения (carrier = Source)
            if (monk == null || monk.IsDead) return;
            if (!ReferenceEquals(e.Target, monk)) return; // смещался не сам монах (это отбрасывание врага) — не наше

            // Монах приземлился вплотную к цели рывка = ближайший враг. Отбрасываем именно его.
            RuntimeUnit victim = NearestEnemy(monk, monk.Position, ctx.Combat, exclude: null);
            if (victim == null) return;

            // Монах бьёт ТОЛЬКО прямо от себя. «Умная» часть — позиция приземления рывка (см. AbilitySystem):
            // монах заходит на сторону цели, противоположную ближайшему другому врагу, поэтому «прямо от
            // монаха через цель» уже смотрит в того врага. Здесь направление простое.
            Vector2 dir = victim.Position - monk.Position;
            float dmg = _displaceDamageMult > 0f
                ? _displaceDamageMult * monk.Stats.Get(StatType.AutoAttackDamage)
                : 0f;
            DamageType dmgType = monk.Relic != null ? monk.Relic.DamageType : DamageType.Physical;

            ctx.Combat.ReportAreaHit(AreaHit.Line(
                victim.Position, dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.right,
                _displaceDistance, _displaceWidth, monk.Team));

            ctx.Combat.Displace(new DisplaceRequest(
                victim, monk, dir, _displaceDistance, _displaceTicks,
                cannonball: true, damage: dmg, damageType: dmgType, width: _displaceWidth,
                kind: DisplaceKind.Knockback, chainDistance: _chainDistance, chainTicks: _chainTicks));
        }

        // Ближайший к точке <paramref name="from"/> живой враг монаха (тай-брейк по Id — детерминизм), кроме
        // <paramref name="exclude"/>. Радиус большой: берём глобально, чтобы не зависеть от точной посадки.
        // Локальный буфер — событие редкое (per-cast).
        private static RuntimeUnit NearestEnemy(RuntimeUnit monk, UnityEngine.Vector2 from, ICombatContext combat, RuntimeUnit exclude)
        {
            var buffer = new List<RuntimeUnit>();
            combat.QueryUnitsInRadius(from, 500f, buffer, TargetFilter.Enemies, monk.Team);

            RuntimeUnit best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < buffer.Count; i++)
            {
                RuntimeUnit o = buffer[i];
                if (o.IsDead || ReferenceEquals(o, exclude)) continue;
                float sq = (o.Position - from).sqrMagnitude;
                if (sq < bestSq || (sq == bestSq && (best == null || o.Id < best.Id)))
                {
                    bestSq = sq;
                    best = o;
                }
            }
            return best;
        }
    }
}
