using System;
using System.Collections.Generic;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Simulation;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Принудительные смещения (§9.9, «Шквальный толчок»): двигает <c>Position</c> летящих юнитов по
    /// фиксированному шагу столько тиков, сколько требует дистанция при фиксированной скорости
    /// (<see cref="SimTuning.DisplaceTicks"/>), держит жёсткое оглушение полёта
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
            public float       ChainDistance;
            public float       PctArmorPen;
            public bool        WallHit; // урон об край арены добивается один раз за полёт
            public readonly List<RuntimeUnit> Hit = new List<RuntimeUnit>();
        }

        // Порог «шаг обрезан границей»: квадрат расхождения желаемой и фактической позиции. Меньше —
        // это арифметика float на самом краю, а не удар о стену.
        private const float WallHitEpsilonSq = 1e-6f;

        private readonly List<Active> _active = new List<Active>();
        private readonly List<RuntimeUnit> _lineBuffer = new List<RuntimeUnit>();

        /// <summary>Полёт завершился: (источник толчка, смещённая цель). Подписчик — CombatSimulation снимает
        /// маркер «в полёте» (тег KnockUp) → единый EffectExpired (на нём завязаны реактивы монаха, §10.6).</summary>
        public event Action<RuntimeUnit, RuntimeUnit> OnDisplacementEnded;

        /// <summary>Сбросить все активные полёты (перезапуск боя на месте) — иначе повиснут ссылки на удалённых юнитов.</summary>
        public void Clear()
        {
            _active.Clear();
            _lineBuffer.Clear();
        }

        /// <summary>
        /// Поставить смещение в работу. Цель сразу переходит в полётное оглушение.
        /// Длительность полёта считается ИЗ ДИСТАНЦИИ при фиксированной скорости
        /// (<see cref="SimTuning.DisplaceTicks"/>): дальний толчок сам по себе держит цель дольше.
        /// Коридор «ядра» шире заданной ширины на <see cref="SimTuning.CannonballWidthMult"/>.
        /// </summary>
        public void Add(in DisplaceRequest req, in SimTuning tuning)
        {
            RuntimeUnit target = req.Target;
            if (target == null || target.IsDead || req.Distance <= 0f) return;

            Vector2 dir = req.Direction.sqrMagnitude > 1e-6f ? req.Direction.normalized : Vector2.right;
            int ticks = tuning.DisplaceTicks(req.Distance, req.SpeedPerSecond);

            _active.Add(new Active
            {
                Unit           = target,
                Source         = req.Source,
                Step           = dir * (req.Distance / ticks),
                TicksRemaining = ticks,
                Cannonball     = req.Cannonball,
                Damage         = req.Damage,
                DamageType     = req.DamageType,
                Width          = req.Width * tuning.CannonballWidthMult,
                ChainDistance  = req.ChainDistance,
                PctArmorPen    = req.PctArmorPen,
            });

            target.DisplacedTicksRemaining = ticks;
        }

        /// <summary>Продвинуть все активные смещения на один тик.</summary>
        /// <param name="bounds">Границы поля: полётная позиция клампится внутрь — «ядро» упирается в стену (вики «15» §7).</param>
        public void Tick(ICombatContext ctx, float dt, in ArenaBounds bounds)
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
                // Клампим ДО «ядра»: сегмент удара идёт до стены, врагов за полем не задеваем.
                Vector2 wanted = prev + a.Step;
                u.Position = bounds.Clamp(wanted);

                if (a.Cannonball && a.Damage > 0f && a.Source != null)
                    Cannonball(a, prev, u.Position, ctx);

                // Впечатало в край арены (кламп съел часть шага) — уникальный случай, три следствия:
                // урон толчка ещё раз, полёт СТОИТ (скольжение вдоль стены выглядело бы сломанным),
                // и цель лежит оглушённой ещё WallImpactStunSeconds. Маркер «в полёте» при этом не
                // снимается, поэтому конец смещения (и телепорт монаха на него) приходит ПОСЛЕ лежания.
                // Самосмещение (рывок монаха: source == unit) о стену не наказывается — он бьётся не о
                // стену, а приезжает к цели; стан здесь заморозил бы кастующего на своей же способности.
                if (!a.WallHit && a.Source != null && !ReferenceEquals(a.Source, u)
                    && (u.Position - wanted).sqrMagnitude > WallHitEpsilonSq)
                {
                    a.WallHit = true;
                    a.Step = Vector2.zero;

                    float wallDamage = a.Damage * ctx.Tuning.WallImpactDamageMult;
                    if (wallDamage > 0f)
                        ctx.DealDamage(new DamageRequest(a.Source, u, wallDamage, a.DamageType, ctx.ArmorK,
                            bonusPctPen: a.PctArmorPen));

                    int stunTicks = (int)(ctx.Tuning.WallImpactStunSeconds * SimConstants.TickRate + 0.5f);
                    if (stunTicks > a.TicksRemaining) a.TicksRemaining = stunTicks;
                }

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
                ctx.DealDamage(new DamageRequest(a.Source, victim, a.Damage, a.DamageType, ctx.ArmorK,
                    bonusPctPen: a.PctArmorPen));

                // §10.6: задетый «ядром» не только получает урон, но и сам слабо отбрасывается вдоль полёта.
                // Источник — тот же (монах), поэтому конец этого цепного полёта тоже триггерит «Вихревой заход».
                // Без каскада (cannonball:false) и слабый (короче главного) → финальный телепорт сядет на исходную цель.
                if (a.ChainDistance > 0f && a.Source != null)
                {
                    // Направление цепного толчка учитывает УГОЛ попадания: чем дальше цель от оси полёта
                    // (скользящее/касательное попадание), тем сильнее толкает ВБОК, а не строго вперёд.
                    // dir = вперёд(единичный) + поперечное смещение цели от линии. По центру → чисто вперёд.
                    Vector2 fwd = seg / len;
                    Vector2 rel = victim.Position - from;
                    Vector2 lateral = rel - fwd * Vector2.Dot(rel, fwd); // перпендикулярная составляющая
                    Vector2 cdir = fwd + lateral;
                    cdir = cdir.sqrMagnitude > 1e-6f ? cdir.normalized : fwd;
                    ctx.Displace(new DisplaceRequest(
                        victim, a.Source, cdir, a.ChainDistance,
                        cannonball: false, damage: 0f, damageType: a.DamageType, width: 0f));
                }
            }
        }
    }
}
