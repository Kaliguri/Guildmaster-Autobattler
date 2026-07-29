namespace Guildmaster.Combat.Tape
{
    /// <summary>Что случилось в бою. Порядок значений не сериализуется — лента живёт только в памяти.</summary>
    public enum TapeEventKind
    {
        UnitSpawned,
        UnitDied,

        /// <summary>Урон нанесён; подробности (HP/щит/школа/добил ли) — в <c>BattleTape.GetDamage(PayloadIndex)</c>.</summary>
        DamageDealt,

        Healed,

        /// <summary>Удар полностью отменён («Изворотливость») — показ рисует «evade».</summary>
        AttackEvaded,

        AttackStarted,
        AttackInterrupted,

        /// <summary>Зона удара (линия/круг) — геометрия в <c>BattleTape.GetAreaHit(PayloadIndex)</c>.</summary>
        AreaHit,

        /// <summary>Бой кончился; исход — в <c>BattleTape.GetOutcome(PayloadIndex)</c>. Приходит показу
        /// с лагом: игрок узнаёт исход тогда, когда его увидит, а не когда сим его посчитал.</summary>
        BattleEnded,

        /// <summary>Dev-рестарт боя на месте. Лента при этом чистится целиком.</summary>
        BattleReset,

        /// <summary>Способность скастована: <c>SourceId</c> — кастер.</summary>
        AbilityCast,

        /// <summary>Начата подготовка или канал: <c>SourceId</c> — кастер, <c>Amount</c> — секунды
        /// подготовки. Показ держит подводку ровно это время, потому что удар придёт в её конце.</summary>
        AbilityCastStarted,

        /// <summary>Каст оборван, не доиграв (контроль, полёт, потеря цели): <c>SourceId</c> — кастер.
        /// Подводка обязана погаснуть — иначе на экране остаётся обещание, которого не будет.</summary>
        AbilityCastInterrupted,

        /// <summary>Эффект наложен: <c>TargetId</c> — носитель, определение — в <c>GetEffect(PayloadIndex)</c>.</summary>
        EffectApplied,

        /// <summary>Эффект спал: <c>TargetId</c> — носитель, определение — в <c>GetEffect(PayloadIndex)</c>.</summary>
        EffectEnded,
    }

    /// <summary>
    /// Одно событие боя со штампом тика. Показ отдаёт события, когда доходит до их тика; режиссура
    /// (подводки, slowmo «чуть раньше») читает те, до которых показ ещё не дошёл — в этом весь смысл
    /// лага: то, что было бы предикцией, становится знанием.
    /// <para><b>Почему не ссылки на <c>RuntimeUnit</c>:</b> событие приезжает из прошлого, а живой
    /// юнит уже в будущем. Поэтому здесь только id и готовые числа, а состояние показ берёт из
    /// снимка того же тика.</para>
    /// <para><b>Как читать поля по виду события:</b></para>
    /// <list type="table">
    /// <item><term>UnitSpawned / UnitDied / AttackInterrupted</term><description><c>SourceId</c> — юнит.</description></item>
    /// <item><term>DamageDealt</term><description><c>SourceId</c> → <c>TargetId</c>, подробности по <c>PayloadIndex</c>.</description></item>
    /// <item><term>Healed</term><description><c>SourceId</c> → <c>TargetId</c>, <c>Amount</c> — величина.</description></item>
    /// <item><term>AttackEvaded</term><description><c>TargetId</c> — тот, кто уклонился.</description></item>
    /// <item><term>AttackStarted</term><description><c>SourceId</c> → <c>TargetId</c>.</description></item>
    /// <item><term>AreaHit</term><description>геометрия по <c>PayloadIndex</c>.</description></item>
    /// <item><term>BattleEnded</term><description>исход по <c>PayloadIndex</c>.</description></item>
    /// <item><term>BattleReset</term><description>полей нет.</description></item>
    /// </list>
    /// </summary>
    public readonly struct TapeEvent
    {
        public readonly TapeEventKind Kind;

        /// <summary>Тик симуляции, на котором событие случилось.</summary>
        public readonly int Tick;

        public readonly int SourceId;
        public readonly int TargetId;

        /// <summary>Числовая величина события, если она у него одна (лечение).</summary>
        public readonly float Amount;

        /// <summary>Мелкий enum события, если он есть (исход боя).</summary>
        public readonly int Flags;

        /// <summary>Индекс тяжёлого payload'а в своём списке ленты, или <c>-1</c>.</summary>
        public readonly int PayloadIndex;

        public TapeEvent(
            TapeEventKind kind, int tick, int sourceId = -1, int targetId = -1,
            float amount = 0f, int flags = 0, int payloadIndex = -1)
        {
            Kind         = kind;
            Tick         = tick;
            SourceId     = sourceId;
            TargetId     = targetId;
            Amount       = amount;
            Flags        = flags;
            PayloadIndex = payloadIndex;
        }
    }

    /// <summary>Подробности удара, на которые ссылается <see cref="TapeEventKind.DamageDealt"/>.</summary>
    public readonly struct TapeDamage
    {
        public readonly DamageResult Result;

        public TapeDamage(in DamageResult result) => Result = result;
    }
}
