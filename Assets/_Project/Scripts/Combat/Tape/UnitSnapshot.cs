using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Tape
{
    /// <summary>
    /// Состояние одного юнита на конец одного тика — то, из чего показ рисует кадр, не заглядывая в
    /// живой <see cref="RuntimeUnit"/>. Сим уходит вперёд на окно опережения, поэтому живой юнит уже
    /// «в будущем»: читать из него — не задержка, а рассинхрон.
    /// <para><b>Состав не произвольный:</b> ровно те поля, которые сегодня читает презентация
    /// (<c>UnitView</c>, полоски HP/маны, силуэт). Что читается один раз при привязке вида —
    /// <c>UnitData</c>, палитра, префаб — здесь не дублируется: это не состояние, а определение.</para>
    /// </summary>
    public readonly struct UnitSnapshot
    {
        /// <summary>Id юнита в симуляции — ключ, по которому показ находит свой вид.</summary>
        public readonly int Id;

        /// <summary>Команда: нужна цвету полосок и фильтрам показа.</summary>
        public readonly int Team;

        public readonly Vector2 Position;

        /// <summary>Позиция на конец ПРЕДЫДУЩЕГО тика — из неё показ интерполирует движение.</summary>
        public readonly Vector2 PreviousPosition;

        public readonly float CurrentHP;
        public readonly float MaxHP;
        public readonly float CurrentShield;
        public readonly float CurrentResource;
        public readonly float MaxResource;

        /// <summary>Размер тела: масштаб вида и радиус попадания в презентации.</summary>
        public readonly float Size;

        /// <summary>Фаза атаки на конец тика — по ней показ выбирает анимацию.</summary>
        public readonly AttackPhase Phase;

        /// <summary>Полная длительность замаха в тиках (0 = замаха нет) — знаменатель прогресса.</summary>
        public readonly int WindupTicks;

        /// <summary>Сколько тиков замаха осталось — вместе с <see cref="WindupTicks"/> даёт прогресс.</summary>
        public readonly int WindupRemaining;

        /// <summary>Тики до следующей авто-атаки.</summary>
        public readonly int AttackCooldownTicks;

        /// <summary>Полная длительность ХВОСТА-доигрыша в тиках (0 = хвоста нет) — знаменатель его прогресса.</summary>
        /// <remarks>
        /// Доигрыш обязан идти по СВОЕЙ длине, а не растягиваться до следующего замаха (решение Макса,
        /// 30.07): медленный кит отыгрывает удар за свою секунду и ЖДЁТ, и именно эта пауза делает
        /// «редкие тяжёлые удары» видимыми. Пока показ тянул хвост по кулдауну, Защитник бесконечно
        /// медленно опускал меч все 0.83 сек паузы, и режима «атака с окном» на экране не существовало.
        /// </remarks>
        public readonly int RecoveryTicks;

        /// <summary>Сколько тиков доигрыша осталось — вместе с <see cref="RecoveryTicks"/> даёт прогресс.</summary>
        public readonly int RecoveryRemaining;

        /// <summary>
        /// Период между тиками урона ВНУТРИ канала авто-атаки, тиков (0 = канала нет). По нему показ крутит
        /// свинг циклом, чтобы кадр контакта пришёлся на каждый удар потока.
        /// </summary>
        /// <remarks>
        /// Период везём числом, а не даём показу пересчитать его из <c>AttackSpeed</c>: формула
        /// <see cref="AttackTiming.IntervalTicks"/> уже имеет владельца, и её копия на стороне показа
        /// разъехалась бы на первом же бафе скорости — причём молча, потому что урон бы шёл правильно.
        /// </remarks>
        public readonly int AttackChannelTickPeriod;

        /// <summary>Тиков до следующего удара внутри канала — вместе с периодом даёт фазу цикла.</summary>
        public readonly int AttackChannelTickRemaining;

        /// <summary>Id текущей цели или <c>-1</c>: показ разворачивает юнита к ней. Ссылки на объект
        /// здесь нет намеренно — иначе через цель протёк бы живой сим.</summary>
        public readonly int TargetId;

        /// <summary>Маска тегов эффектов: по ней показ включает стелс-силуэт и прочие метки.</summary>
        public readonly EffectTag EffectTagMask;

        public readonly bool IsDead;

        // --- Поля dev-оверлеев (Ф7). Живут здесь по той же причине, что и всё остальное: оверлей,
        // читающий живой сим, рисует кольца там, где на экране юнитов ещё нет. Хранятся признаками, а
        // не числами — оверлею нужен факт «горит ли статус», а не его величина.

        /// <summary>Радиус авто-атаки — dev-круг досягаемости (бафф его меняет, поэтому это состояние).</summary>
        public readonly float AttackRange;

        /// <summary>Может ли юнит действовать: <c>false</c> = выведен контролем (кольцо стана).</summary>
        public readonly bool CanAct;

        /// <summary>Юнит в полёте от отбрасывания — вторая половина «стана» в понимании оверлея.</summary>
        public readonly bool IsDisplaced;

        /// <summary>Взведено усиление следующего удара (<c>EmpowerDamageMult</c> живого юнита).</summary>
        public readonly bool IsEmpowered;

        /// <summary>
        /// Насколько юнит разогнался [0..1] — по этой доле показ подмешивает клип бега к шагу. Живёт в
        /// СИМУЛЯЦИИ (там же ускорение), сюда приезжает снимком: решать «бежит ли он быстро» по наблюдаемой
        /// скорости показ не может — на дистанции в один тик разница со шагом неотличима от шума.
        /// </summary>
        public readonly float SprintRamp;

        /// <summary>Разгон начался — тот же факт, что <see cref="SprintRamp"/>, для читаемости условий.</summary>
        public bool IsSprinting => SprintRamp > 0f;

        /// <summary>
        /// Текущий свинг идёт с разбега — показ выбирает по нему клип. Несём именно признак СВИНГА, а не
        /// взведённый заряд: заряд гаснет в том же тике, в котором взведён, и в снимке был бы всегда false.
        /// </summary>
        public readonly bool ChargedSwing;

        /// <summary>Юнит в замахе — имя и смысл те же, что у одноимённого свойства живого юнита.</summary>
        public bool IsWindingUp => Phase == AttackPhase.Windup;

        public UnitSnapshot(
            int id, int team, Vector2 position, Vector2 previousPosition,
            float currentHp, float maxHp, float currentShield, float currentResource, float maxResource,
            float size, AttackPhase phase, int windupTicks, int windupRemaining,
            int attackCooldownTicks, int targetId, EffectTag effectTagMask, bool isDead,
            float attackRange = 0f, bool canAct = true, bool isDisplaced = false, bool isEmpowered = false,
            float sprintRamp = 0f, bool chargedSwing = false,
            int recoveryTicks = 0, int recoveryRemaining = 0,
            int attackChannelTickPeriod = 0, int attackChannelTickRemaining = 0)
        {
            Id                  = id;
            Team                = team;
            Position            = position;
            PreviousPosition    = previousPosition;
            CurrentHP           = currentHp;
            MaxHP               = maxHp;
            CurrentShield       = currentShield;
            CurrentResource     = currentResource;
            MaxResource         = maxResource;
            Size                = size;
            Phase               = phase;
            WindupTicks         = windupTicks;
            WindupRemaining     = windupRemaining;
            AttackCooldownTicks = attackCooldownTicks;
            TargetId            = targetId;
            EffectTagMask       = effectTagMask;
            IsDead              = isDead;
            AttackRange         = attackRange;
            CanAct              = canAct;
            IsDisplaced         = isDisplaced;
            IsEmpowered         = isEmpowered;
            SprintRamp          = sprintRamp;
            ChargedSwing        = chargedSwing;
            RecoveryTicks       = recoveryTicks;
            RecoveryRemaining   = recoveryRemaining;

            AttackChannelTickPeriod    = attackChannelTickPeriod;
            AttackChannelTickRemaining = attackChannelTickRemaining;
        }

        /// <summary>Снять состояние с живого юнита. Единственное место, где сим встречается с лентой.</summary>
        public static UnitSnapshot From(RuntimeUnit unit)
        {
            RuntimeUnit target = unit.CurrentTarget;
            return new UnitSnapshot(
                unit.Id,
                unit.Team,
                unit.Position,
                unit.PreviousPosition,
                unit.CurrentHP,
                unit.Stats.Get(StatType.MaxHP),
                unit.CurrentShield,
                unit.CurrentResource,
                unit.Stats.Get(StatType.MaxResource),
                unit.Stats.Get(StatType.Size),
                unit.Phase,
                unit.WindupTicks,
                unit.WindupRemaining,
                unit.AttackCooldownTicks,
                target != null ? target.Id : -1,
                unit.EffectTagMask,
                unit.IsDead,
                unit.Stats.Get(StatType.AttackRange),
                unit.CanAct,
                unit.DisplacedTicksRemaining > 0,
                unit.EmpowerDamageMult > 0f,
                unit.SprintRamp,
                unit.ChargedSwing,
                unit.RecoveryTicks,
                unit.RecoveryRemaining,
                // Период нужен только тому, у кого канал вообще есть: у остальных ноль честнее числа,
                // которое показ мог бы принять за идущий поток.
                unit.AttackChannel.Exists
                    ? AttackTiming.IntervalTicks(unit.Stats.Get(StatType.AttackSpeed))
                    : 0,
                unit.AttackChannelTickRemaining);
        }
    }
}
