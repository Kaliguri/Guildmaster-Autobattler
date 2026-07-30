using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;
using UnityEngine.Serialization;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Периодический урон (DoT). Потенция задаётся как <b>урон в секунду</b> (per-second rate) и
    /// масштабируется статами источника через <see cref="ScalableValue"/>; за один тик применяется
    /// <c>Potency × Interval × Stacks</c>. Total масштабируется числом тиков (длительностью), а не
    /// запекается — вики «11» §5.1.
    /// <para><b>Числа:</b> <c>_damagePerSecond</c> — урон В СЕКУНДУ (не за тик!), масштабируется
    /// статами источника; <c>_interval</c> — как часто капает, секунды (за раз применяется
    /// «в секунду × интервал», поэтому частота не меняет суммарный урон, только его дробность);
    /// <c>_damagePctTargetMaxHp</c> — добавка в долях от МАКСИМАЛЬНОГО HP цели, тоже в секунду
    /// (анти-танк; у поджога Мечника снята — процент переехал в «Угли», решение 2026-07-26/4);
    /// <c>_damageSchool</c>/<c>_physicalSubtype</c>/<c>_magicElement</c>/<c>_affinity</c> — тип урона.</para>
    /// <para><b>Когда срабатывает:</b> каждые <c>_interval</c> секунд, пока эффект висит. Тик DoT —
    /// не прямой удар: он не будит шипы и щиты.</para>
    /// </summary>
    [Serializable]
    public sealed class PeriodicDamageComponent : IPeriodicComponent, IScalablePotency
    {
        [Tooltip("Интервал между тиками, сек.")]
        [SerializeField] private float _interval = 1f;

        [Tooltip("Урон В СЕКУНДУ (per-second rate). Скейлится статами источника.")]
        [SerializeField] private ScalableValue _damagePerSecond;

        [Tooltip("Школа урона DoT (гасится соответствующей бронёй).")]
        [FormerlySerializedAs("_damageType")]
        [SerializeField] private DamageSchool _damageSchool = DamageSchool.Magical;

        [Tooltip("Физ-подтип урона DoT (при школе Physical). Питает тег быстрого чтения; None = не задан.")]
        [SerializeField] private PhysicalSubtype _physicalSubtype = PhysicalSubtype.None;

        [Tooltip("Магический элемент урона DoT (при школе Magical): Огонь для «Поджога» и т.п. Питает тег; None = не задан.")]
        [SerializeField] private MagicElement _magicElement = MagicElement.None;

        [Tooltip("Сродство урона DoT: Яд для отравления (иммунна Нежить/Конструкты), Тьма/Свет — по типу существа цели.")]
        [SerializeField] private DamageAffinity _affinity = DamageAffinity.None;

        [Tooltip("Доля от МАКСИМАЛЬНОГО HP цели в секунду (0.03 = 3%/сек, «Поджог» Огненного мечника). " +
                 "Складывается с плоским уроном выше; так DoT одинаково жалит и толстых, и тонких.")]
        [SerializeField] private float _damagePctTargetMaxHp;

        [Tooltip("Прибавка к урону В СЕКУНДУ за каждую секунду, что эффект уже висит («Кошмар»: чем дольше " +
                 "цель спит, тем больнее тик). 0 = ровный DoT. Нарастание считается от ПРОЖИТОГО времени " +
                 "эффекта, поэтому подкрепление, обновившее длительность, сбрасывает разгон.")]
        [SerializeField] private float _growthPerSecond;

        public float Interval => _interval;
        public ScalableValue Potency => _damagePerSecond;

        /// <summary>Тип урона этого DoT (прямые поля источника) — для агрегации тегов «быстрого чтения».</summary>
        public DamageType DamageType => new DamageType(_damageSchool, _physicalSubtype, _magicElement, _affinity);

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnTick(in EffectContext ctx)
        {
            // Share: доля вкладчика, за которого идёт этот проход. Эффект на цели один, но держать
            // его могут несколько — тогда тик прогоняется по вкладчикам, и урон каждого куска
            // засчитывается своему источнику (реш. Макса 2026-07-26). Один вкладчик → Share = 1.
            float rate = DamagePerSecond(ctx.Potency, ctx.Target) + Growth(in ctx);
            float damage = rate * ctx.Dt * ctx.Stacks * ctx.Share;
            if (damage <= 0f) return;

            // Periodic: тик DoT не будит реактивы «на удар» — горение и яд не должны запускать шипы и щиты.
            ctx.Combat.DealDamage(new DamageRequest(ctx.Source, ctx.Target, damage, _damageSchool, ctx.Combat.ArmorK,
                sourceKind: DamageSourceKind.Periodic, affinity: _affinity, element: _magicElement));
        }

        /// <summary>
        /// Прибавка за разгон: сколько урона в секунду добавилось к этому моменту жизни эффекта.
        /// Считается из ПРОЖИТОГО времени (базовая длительность минус остаток), а не из счётчика
        /// сработавших тиков — счётчик пришлось бы держать в эффекте, а stateless-компоненту его негде
        /// хранить. Бессрочные эффекты не разгоняются: у них нет «прожитого» относительно конца.
        /// </summary>
        private float Growth(in EffectContext ctx)
        {
            if (_growthPerSecond <= 0f || ctx.Effect == null || ctx.Effect.IsPermanent) return 0f;

            float total = ctx.Effect.Def != null ? ctx.Effect.Def.BaseDuration : 0f;
            if (total <= 0f) return 0f;

            float elapsed = total - ctx.Effect.RemainingTicks / (float)Core.Simulation.SimConstants.TickRate;
            return elapsed > 0f ? elapsed * _growthPerSecond : 0f;
        }

        /// <summary>
        /// Урон в секунду одного стака по этой цели: плоская потенция (уже отскейленная статами источника)
        /// плюс доля от максимального HP цели. Публичен, потому что детонация («Воспламенение») должна
        /// досчитать НЕНАНЕСЁННЫЙ остаток DoT — а для этого ей нужен тот же самый расчёт, что и в тике.
        /// </summary>
        public float DamagePerSecond(float scaledPotency, RuntimeUnit target)
        {
            float dps = scaledPotency;
            if (_damagePctTargetMaxHp > 0f && target != null)
                dps += target.Stats.Get(StatType.MaxHP) * _damagePctTargetMaxHp;
            return dps;
        }
    }
}
