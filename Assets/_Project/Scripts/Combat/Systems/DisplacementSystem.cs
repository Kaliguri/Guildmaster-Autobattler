using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Принудительные смещения (§9.9, «Шквальный толчок»): двигает <c>Position</c> летящих юнитов по
    /// фиксированному шагу за фикс. число тиков, держит жёсткое оглушение полёта
    /// (<see cref="RuntimeUnit.DisplacedTicksRemaining"/> — не эффект, поэтому иммунно к сопротивлению
    /// контролю), и при «ядре» бьёт врагов источника на пройденном сегменте. По завершении полёта
    /// зовёт <see cref="OnDisplacementEnded"/> (сигнал для «Вихревого захода»).
    /// <para>Тикается ПОСЛЕ MovementSystem и ДО перестройки spatial hash — позиции полёта попадают в
    /// хэш этого тика. Обход активных смещений по индексу списка → детерминизм (гейт S5 в срез-тестах).</para>
    /// </summary>
    public sealed class DisplacementSystem
    {
        private sealed class Active
        {
            public RuntimeUnit Unit;
            public RuntimeUnit Source;
            public Vector2     Step;
            public int         TicksRemaining;
            public bool        Cannonball;
            public float       Damage;
            public Data.Definitions.DamageType DamageType;
            public float       Width;
            public readonly List<RuntimeUnit> Hit = new List<RuntimeUnit>();
        }

        private readonly List<Active> _active = new List<Active>();
        private readonly List<RuntimeUnit> _lineBuffer = new List<RuntimeUnit>();

        /// <summary>Полёт завершился: (источник толчка, смещённая цель). Подписчик — CombatSimulation (сигнал UnitDisplaced).</summary>
        public event Action<RuntimeUnit, RuntimeUnit> OnDisplacementEnded;

        /// <summary>Поставить смещение в работу. Цель сразу переходит в полётное оглушение.</summary>
        public void Add(in DisplaceRequest req)
        {
            RuntimeUnit target = req.Target;
            if (target == null || target.IsDead || req.Ticks <= 0) return;

            Vector2 dir = req.Direction.sqrMagnitude > 1e-6f ? req.Direction.normalized : Vector2.right;

            _active.Add(new Active
            {
                Unit           = target,
                Source         = req.Source,
                Step           = dir * (req.Distance / req.Ticks),
                TicksRemaining = req.Ticks,
                Cannonball     = req.Cannonball,
                Damage         = req.Damage,
                DamageType     = req.DamageType,
                Width          = req.Width,
            });

            target.DisplacedTicksRemaining = req.Ticks;
        }

        /// <summary>Продвинуть все активные смещения на один тик.</summary>
        public void Tick(ICombatContext ctx, float dt)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Active a = _active[i];
                RuntimeUnit u = a.Unit;

                // Цель погибла в полёте — снимаем полёт, финальный сигнал не шлём (некому в спину).
                if (u == null || u.IsDead)
                {
                    if (u != null) u.DisplacedTicksRemaining = 0;
                    _active.RemoveAt(i);
                    continue;
                }

                Vector2 prev = u.Position;
                u.PreviousPosition = prev;
                u.Position = prev + a.Step;

                if (a.Cannonball && a.Damage > 0f && a.Source != null)
                    Cannonball(a, prev, u.Position, ctx);

                a.TicksRemaining--;
                u.DisplacedTicksRemaining = a.TicksRemaining;

                if (a.TicksRemaining <= 0)
                {
                    u.DisplacedTicksRemaining = 0;
                    _active.RemoveAt(i);
                    OnDisplacementEnded?.Invoke(a.Source, u);
                }
            }
        }

        /// <summary>«Ядро»: летящая цель бьёт врагов источника на сегменте [from, to], каждого — один раз, без friendly-fire.</summary>
        private void Cannonball(Active a, Vector2 from, Vector2 to, ICombatContext ctx)
        {
            Vector2 seg = to - from;
            float len = seg.magnitude;
            if (len < 1e-5f) return;

            ctx.QueryUnitsInLine(from, seg, len, a.Width, _lineBuffer, TargetFilter.Enemies, a.Source.Team);

            for (int i = 0; i < _lineBuffer.Count; i++)
            {
                RuntimeUnit victim = _lineBuffer[i];
                if (victim == a.Unit || victim.IsDead || a.Hit.Contains(victim)) continue;

                a.Hit.Add(victim);
                ctx.DealDamage(new DamageRequest(a.Source, victim, a.Damage, a.DamageType, ctx.ArmorK));
            }
        }
    }
}
