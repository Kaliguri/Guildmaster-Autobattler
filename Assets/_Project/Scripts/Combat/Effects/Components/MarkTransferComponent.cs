using System;
using System.Collections.Generic;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Метка охотника» (§9.5): реактив на <see cref="CombatEvent.UnitDied"/>. Когда носитель метки
    /// (враг Следопыта) гибнет — перевешивает эффект на ближайшего живого врага источника с
    /// обновлением длительности (свежий <c>ApplyEffect</c>). Если врагов не осталось — метка гаснет
    /// вместе с трупом. Диспатчится даже мёртвому носителю (см. <c>CombatSimulation.DrainEventQueue</c>).
    /// </summary>
    [Serializable]
    public sealed class MarkTransferComponent : IReactiveComponent
    {
        // Радиус «глобального» поиска ближайшего врага (метка — без ограничения дальности, §9.10).
        private const float SearchRadius = 500f;

        public CombatEvent Events => CombatEvent.UnitDied;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (e.Type != CombatEvent.UnitDied) return;

            RuntimeUnit dead = ctx.Target;          // носитель метки (труп)
            if (dead == null || e.Target != dead) return;

            RuntimeUnit source = ctx.Source;        // Следопыт (кто пометил)
            if (source == null) return;

            // Ближайший живой враг источника к точке смерти (без ограничения дальности).
            var candidates = new List<RuntimeUnit>();
            ctx.Combat.QueryUnitsInRadius(dead.Position, SearchRadius, candidates, TargetFilter.Enemies, source.Team);

            RuntimeUnit best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                RuntimeUnit c = candidates[i];
                if (c == dead || c.IsDead) continue;

                float sq = (c.Position - dead.Position).sqrMagnitude;
                if (sq < bestSq || (sq == bestSq && (best == null || c.Id < best.Id)))
                {
                    bestSq = sq;
                    best = c;
                }
            }

            if (best != null)
                ctx.Combat.ApplyEffect(best, ctx.Effect.Def, source); // свежая метка → длительность обновлена
        }
    }
}
