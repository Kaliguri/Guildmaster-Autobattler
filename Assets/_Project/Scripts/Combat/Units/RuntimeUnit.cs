using System.Collections.Generic;
using Guildmaster.Combat.Abilities;
using Guildmaster.Combat.Effects;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Рантайм-представление юнита на один бой. POCO — без MonoBehaviour и ScriptableObject.
    /// Создаётся через <see cref="RuntimeUnitFactory"/>; владеет <see cref="Stats"/>,
    /// текущим HP/ресурсом/щитом и состоянием движения (вики «10» §5.2).
    /// </summary>
    public sealed class RuntimeUnit
    {
        /// <summary>Уникальный идентификатор юнита в рамках текущего боя.</summary>
        public int Id;

        /// <summary>Команда: 0 = союзники (левая сторона), 1 = враги (правая).</summary>
        public int Team;

        /// <summary>Собранные статы с поддержкой слоистых модификаторов.</summary>
        public Stats Stats;

        /// <summary>Текущее здоровье. DeathSystem помечает мёртвым при ≤ 0.</summary>
        public float CurrentHP;

        /// <summary>Текущий ресурс (мана/ярость). Фаза 2.</summary>
        public float CurrentResource;

        /// <summary>Тик, с которого считается текущее секундное окно набора ресурса (потолок «единиц в секунду»).</summary>
        public int ResourceWindowStartTick;

        /// <summary>Сколько ресурса уже набрано в текущем секундном окне — сверяется с потолком юнита.</summary>
        public float ResourceGainedInWindow;

        /// <summary>Текущий щит: поглощает урон до вычета из HP.</summary>
        public float CurrentShield;

        /// <summary>Позиция центра юнита в мировых координатах (непрерывное поле, без Rigidbody).</summary>
        public Vector2 Position;

        /// <summary>Позиция на предыдущем тике — для интерполяции вида (60 fps рендер, 30 Hz сим).</summary>
        public Vector2 PreviousPosition;

        /// <summary>Текущая цель для движения/позиционирования. Пишет мозг (Фаза 3), не TargetingSystem. Null = нет цели.</summary>
        public RuntimeUnit CurrentTarget;

        // --- AI (Фаза 3, вики «13» §2.7) ---

        /// <summary>Мозг юнита: интерпретирует AIProfile. Ставит <see cref="RuntimeUnitFactory"/>. null → дефолтный мозг BrainSystem.</summary>
        public IUnitBrain Brain;

        /// <summary>Фаза стаггера AI (= Id % AiTickInterval). Юнит думает на тике, где tick % interval == BrainPhase.</summary>
        public int BrainPhase;

        /// <summary>Событийное прерывание: взведён → переоценка на ближайшем тике вне фазы. Сбрасывает BrainSystem.</summary>
        public bool BrainDirty;

        /// <summary>Намерение позиционирования (Approach/Kite/Retreat). Пишет мозг (10 Гц), читает MovementSystem (30 Гц).</summary>
        public PositioningIntent Positioning;

        /// <summary>Цель авто-атаки. Для хилера — союзник (≠ CurrentTarget). Пишет мозг, читает AutoAttackSystem.</summary>
        public RuntimeUnit AutoAttackTarget;

        /// <summary>Множитель урона следующей авто-атаки (§9.6, «Скрытность»): 0 = нет усиления. AutoAttackSystem применяет и сбрасывает (однострел).</summary>
        public float EmpowerDamageMult;

        /// <summary>Блинк «за спину» цели на усиленной атаке из скрытности (§10.5, «Скрытный убийца»): в момент
        /// удара телепорт на дальнюю сторону цели. Ставит <c>StealthComponent</c> вместе с усилением, применяет
        /// и сбрасывает <c>AutoAttackSystem</c> (в блоке снятия усиления). Монах сюда не завязан (у него свой заход).</summary>
        public bool BlinkBehindOnNextAttack;

        /// <summary>
        /// Цель захода монаха (§10.6): в кого он зашёл рывком и кого оттолкнёт на приземлении. Ставит
        /// <c>AbilitySystem</c> при касте «Шквального толчка» (позицию рывка считает под ЭТУ цель), читает и
        /// сбрасывает <c>WhirlDashLandingComponent</c> — чтобы толкать именно её, а не «ближайшего» (тот мог
        /// разъехаться). null = нет активного захода.
        /// </summary>
        public RuntimeUnit PendingEngageTarget;

        /// <summary>
        /// Тиков принудительного смещения осталось (§9.9, «Шквальный толчок»): &gt; 0 — юнит в полёте.
        /// Жёсткое состояние: движет DisplacementSystem, юнит не действует/не двигается сам, сопротивление
        /// контролю НЕ применяется (LoL-подкидывание). Владелец — DisplacementSystem.
        /// </summary>
        public int DisplacedTicksRemaining;

        // --- Авто-атака: FSM фаз на int-тиках (вики «14») ---

        /// <summary>Кулдаун автоатаки в сим-тиках. 0 = готов к атаке. Рестартится в начале замаха (якорь).</summary>
        public int AttackCooldownTicks;

        /// <summary>
        /// Фаза боевого действия — единый источник истины «занятости» (Idle/Windup/Recovery). Пишет только
        /// <c>AutoAttackSystem</c>; движение/презентация/способности читают. См. <see cref="AttackPhase"/>.
        /// </summary>
        public AttackPhase Phase;

        /// <summary>Идёт замах (windup): юнит занёс оружие, урон ещё не нанесён. Производный алиас <see cref="Phase"/>.</summary>
        public bool IsWindingUp => Phase == AttackPhase.Windup;

        /// <summary>Тиков замаха осталось до кадра контакта. Когда ≤ 0 — резолв удара.</summary>
        public int WindupRemaining;

        /// <summary>Тиков восстановления (хвост после удара) осталось. Когда ≤ 0 — переход в Idle. 0 = нет восстановления.</summary>
        public int RecoveryRemaining;

        /// <summary>Полная длительность текущего замаха в тиках (посчитана раз на старте, не пересчитывается на лету).</summary>
        public int WindupTicks;

        /// <summary>Запланированная длина хвоста-восстановления текущего свинга в тиках (доигрыш клипа +
        /// доп. секунды). Считается раз на старте замаха (как <see cref="WindupTicks"/>), применяется в резолве.</summary>
        public int RecoveryTicks;

        /// <summary>Снапшот цели на старте замаха: удар наносится по ней (если жива и в радиусе к концу замаха).</summary>
        public RuntimeUnit WindupTarget;

        /// <summary>Помечен DeathSystem — исключается из всех систем с текущего тика.</summary>
        public bool IsDead;

        /// <summary>Боевой кит юнита (реликвия/враг): тип атаки, стат-блок, эффекты, AI (вики «13» §3.1).</summary>
        public UnitData Unit;

        /// <summary>SO «Пилот»: идентичность, перки (Фаза 2/4).</summary>
        public VesselData Vessel;

        /// <summary>Школа урона кита. Без кита — физика (дефолт пайплайна).</summary>
        public DamageSchool DamageSchool => Unit != null ? Unit.DamageSchool : DamageSchool.Physical;

        /// <summary>Сродство урона кита.</summary>
        public DamageAffinity Affinity => Unit != null ? Unit.Affinity : DamageAffinity.None;

        // --- Эффекты (Фаза 2) ---

        /// <summary>Активные эффекты на юните. Мутирует только <c>EffectSystem</c> (через pending, не во время итерации).</summary>
        public readonly List<RuntimeEffect> ActiveEffects = new List<RuntimeEffect>();

        /// <summary>Битовая маска тегов активных эффектов. Обновляется при add/remove — быстрый запрос для AI (Фаза 3) и диспела.</summary>
        public EffectTag EffectTagMask;

        /// <summary>
        /// Битовое ИЛИ тегов эффектов, ЛЕТЯЩИХ в этого юнита (on-hit эффекты снарядов, ещё не легли).
        /// Таргетинг, зависящий от эффекта (PreferTagged/PreferUntagged), учитывает их наравне с
        /// <see cref="EffectTagMask"/>, чтобы не выбирать цель повторно, пока к ней уже летит, напр., «Заморозка»
        /// (иначе двойное наложение). Только чтение; правится через <see cref="AddIncomingEffect"/>/<see cref="RemoveIncomingEffect"/>.
        /// </summary>
        public EffectTag IncomingEffectTags { get; private set; }

        // Рефкаунт входящих эффектов: по одной записи-маске на КАЖДЫЙ летящий снаряд (несколько снарядов
        // одного тега в одну цель — напр. два одинаковых крио/кооп-дубля — считаются независимо). Бит в
        // IncomingEffectTags гаснет, только когда ушёл ПОСЛЕДНИЙ снаряд с ним. Список крошечный (снаряды в цель).
        private readonly List<EffectTag> _incomingReservations = new List<EffectTag>();

        /// <summary>Забронировать теги летящего в юнита снаряда (при спавне снаряда).</summary>
        public void AddIncomingEffect(EffectTag mask)
        {
            if (mask == 0) return;
            _incomingReservations.Add(mask);
            IncomingEffectTags |= mask;
        }

        /// <summary>Снять бронь одного разрешённого снаряда (попал/деспавн); бит гаснет лишь без остатка носителей.</summary>
        public void RemoveIncomingEffect(EffectTag mask)
        {
            if (mask == 0 || !_incomingReservations.Remove(mask)) return;
            EffectTag remaining = 0;
            for (int i = 0; i < _incomingReservations.Count; i++) remaining |= _incomingReservations[i];
            IncomingEffectTags = remaining;
        }

        /// <summary>Активные способности (кулдаун/ресурс). Заполняет <see cref="RuntimeUnitFactory"/> из реликвии.</summary>
        public readonly List<AbilityRuntime> Abilities = new List<AbilityRuntime>();

        // --- Флаги контроля (Фаза 2) ---
        // Пересчитываются EffectSystem из активных ControlComponent. Системы движения/атаки/каста только читают.

        /// <summary>Может действовать (атаковать/кастовать). false = оглушение/сон.</summary>
        public bool CanAct = true;

        /// <summary>Может двигаться. false = обездвиживание/корень.</summary>
        public bool CanMove = true;

        /// <summary>Может кастовать способности. false = немота.</summary>
        public bool CanCast = true;
    }
}
