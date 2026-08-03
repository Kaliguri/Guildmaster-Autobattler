using System;

namespace Guildmaster.Combat.Effects
{
    /// <summary>
    /// Внутренние боевые события для реактивных компонентов эффектов (вампиризм/шипы и т.п.).
    /// <c>[Flags]</c> — компонент подписывается на несколько типов через <see cref="IReactiveComponent.Events"/>.
    /// Это НЕ outward-события презентации (те идут C#-event'ами/MessagePipe) и НЕ команды сим
    /// (вики «6» §7, «12» §3.4).
    /// </summary>
    [Flags]
    public enum CombatEvent
    {
        None          = 0,
        DamageDealt   = 1 << 0,
        DamageTaken   = 1 << 1,
        Healed        = 1 << 2,
        UnitDied      = 1 << 3,
        EffectApplied = 1 << 4,

        /// <summary>Носитель совершил убийство (доставляется УБИЙЦЕ = Source). «Скрытность» ассасина (§10.5).</summary>
        UnitKilled    = 1 << 5,

        /// <summary>
        /// Эффект истёк/снят с юнита (доставляется ИСТОЧНИКУ эффекта = Source). Единый шов «эффект закончился»:
        /// реактив фильтрует по тегам истёкшего эффекта (<see cref="CombatEventData.Tags"/>) и команде юнита,
        /// на котором он закончился (<see cref="CombatEventData.Target"/>). Так реагируют «Вихревой заход»
        /// монаха (конец отбрасывания врага) и приземление рывка (конец смещения самого монаха), §10.6.
        /// Смещение — это эффект (тег <see cref="Guildmaster.Data.Definitions.EffectTag.KnockUp"/>), не жёсткое
        /// состояние: только длительность не скейлится (Neutral), всё остальное — через систему эффектов.
        /// </summary>
        EffectExpired = 1 << 6,

        /// <summary>
        /// Кто-то применил активную способность. Доставляется ВСЕМ живым врагам кастующего — потому что
        /// реагирует на чужой каст именно противник («Отражающий налёт» Антимага копит щит за каждое
        /// вражеское заклинание). Единственное широковещательное событие в очереди.
        /// </summary>
        AbilityCast   = 1 << 7,

        /// <summary>
        /// Носитель ЗАВЕРШИЛ Атаку: она прошла полный путь замах → (канал) → конец рекавери. Промах
        /// событие даёт, прерванная контролем Атака — нет (вердикт Макса 2026-08-01). Доставляется самому
        /// носителю; на нём живут «каждая N-я Атака» и цикл фаз, которые взводят заряд СЛЕДУЮЩЕЙ Атаки.
        /// </summary>
        /// <remarks>
        /// Отдельно от <see cref="DamageDealt"/> потому, что то — событие УДАРА: у многоударного кита оно
        /// приходит несколько раз за Атаку, у удара по площади — на каждого задетого, а у промаха не
        /// приходит вовсе. Считать им Атаки значит считать что-то другое.
        /// </remarks>
        AttackCompleted = 1 << 8,

        /// <summary>
        /// Комбо носителя порвалось: он пробыл вне атакующего лупа дольше <c>SimTuning.ComboBreakSeconds</c>
        /// (ГДД: глоссарий, 2026-07-30/11). Счётчик серии уже обнулён; событие нужно тем, у кого от серии
        /// остался ВЗВЕДЁННЫЙ заряд — его гасит владелец, а не общий диспел.
        /// </summary>
        ComboBroken     = 1 << 9,
    }

    /// <summary>Полезная нагрузка боевого события, диспатчится через внутреннюю FIFO-очередь (Stage 6).</summary>
    public readonly struct CombatEventData
    {
        public readonly CombatEvent Type;
        public readonly RuntimeUnit Source;
        public readonly RuntimeUnit Target;
        public readonly float       Amount;

        /// <summary>Теги, релевантные событию: для <see cref="CombatEvent.EffectExpired"/> — теги истёкшего эффекта. Иначе None.</summary>
        public readonly Data.Definitions.EffectTag Tags;

        /// <summary>Урон-события: откуда пришёл урон. Реактивы «на удар» гейтятся по нему — иначе тики DoT и
        /// их собственная ответка порождают новые срабатывания.</summary>
        public readonly DamageSourceKind SourceKind;

        /// <summary>
        /// Урон-события: тип урона. Одна ось вместо прежней пары «школа + стихия» (реформа
        /// 2026-07-30) — реактив отличает огонь от прочей магии и дробящий от режущего по ней же.
        /// Вне урон-событий — <see cref="Data.Definitions.DamageType.Undefined"/>: события «эффект
        /// истёк» или «применена способность» типа урона не несут, и подставлять им какой-нибудь
        /// значило бы соврать потребителю.
        /// </summary>
        public readonly Data.Definitions.DamageType DamageType;

        /// <summary>Школа урона события — следствие <see cref="DamageType"/>, отдельного поля нет.</summary>
        public Data.Definitions.DamageSchool School => Data.Definitions.DamageTypes.SchoolOf(DamageType);

        /// <summary>Урон стихии огня: то, что копит «Угли» (карточка [[burn]]).</summary>
        public bool IsFire => DamageType == Data.Definitions.DamageType.Fire;

        /// <summary>Удар был авто-атакой (разгон «Пылающих клинков», уклонение убийцы).</summary>
        public bool IsAutoAttack => SourceKind == DamageSourceKind.AutoAttack;

        /// <summary>Прямой удар — авто-атака или атакующая способность (будит шипы и щиты; доты — нет).</summary>
        public bool IsDirectHit => SourceKind is DamageSourceKind.AutoAttack or DamageSourceKind.Ability;

        public CombatEventData(CombatEvent type, RuntimeUnit source, RuntimeUnit target, float amount)
            : this(type, source, target, amount, Data.Definitions.EffectTag.None) { }

        public CombatEventData(CombatEvent type, RuntimeUnit source, RuntimeUnit target, float amount,
                               Data.Definitions.EffectTag tags,
                               DamageSourceKind sourceKind = DamageSourceKind.Ability,
                               Data.Definitions.DamageType damageType = Data.Definitions.DamageType.Undefined)
        {
            Type       = type;
            Source     = source;
            Target     = target;
            Amount     = amount;
            Tags       = tags;
            SourceKind = sourceKind;
            DamageType = damageType;
        }
    }
}
