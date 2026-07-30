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

        /// <summary>
        /// Насколько юнит разогнался [0..1]: 0 — обычный шаг, 1 — полная прибавка скорости разбега. Пишет и
        /// гасит <c>MovementSystem</c> по гистерезису зазора до цели — это состояние СИМУЛЯЦИИ, а не показа:
        /// презентация только читает долю, чтобы подмешать клип бега. Ускорять юнита анимацией нельзя —
        /// ноги обгонят позицию.
        /// <para>Доля, а не признак, потому что разгон занимает время: юнит сперва идёт шагом
        /// (<c>SprintWalkSeconds</c>), потом набирает скорость (<c>SprintRampSeconds</c>). Прибавка,
        /// включающаяся щелчком, читается как телепорт — и вместе с ней щёлкает клип.</para>
        /// </summary>
        public float SprintRamp;

        /// <summary>
        /// Сколько тиков подряд юнит ХОЧЕТ бежать. Отдельно от <see cref="SprintRamp"/>: намерение держит
        /// гистерезис зазора, а доля растёт из его непрерывности. Любой перерыв (замах, контроль, потеря
        /// цели) обнуляет счётчик, и разгон начинается заново — иначе юнит копил бы разбег стоя.
        /// </summary>
        public int SprintWantTicks;

        /// <summary>Разгон начался — показ переключается на клип бега. Тот же факт, что <see cref="SprintRamp"/>.</summary>
        public bool IsSprinting => SprintRamp > 0f;

        /// <summary>Сбросить разбег целиком: и набранную долю, и накопленное намерение.</summary>
        public void StopSprint()
        {
            SprintRamp      = 0f;
            SprintWantTicks = 0;
        }

        /// <summary>
        /// Юнит добежал разбегом и ещё не ударил: следующий свинг — удар с разбега (свой замах из
        /// <see cref="Data.Definitions.UnitData.ChargeAttackWindupMult"/>, свой клип у показа). Взводит
        /// <c>MovementSystem</c> в момент, когда разбег кончился прибытием, тратит первый же замах.
        /// Разбег, оборванный не прибытием (цель умерла, юнита обездвижили), заряда не оставляет.
        /// </summary>
        public bool ChargedAttackReady;

        /// <summary>
        /// ТЕКУЩИЙ свинг идёт с разбега — держится весь замах и хвост, а не мгновение перед ними.
        /// <para>
        /// Отдельное поле от <see cref="ChargedAttackReady"/> потому, что заряд тратится в тот же тик, в
        /// котором взводится: движение ставит его при прибытии, авто-атака тем же тиком входит в замах и
        /// гасит. Снимок ленты снимается ПОСЛЕ тика, поэтому показ видел бы только <c>false</c> и играл бы
        /// разбег обычной атакой — «удар в рывке не срабатывает».
        /// </para>
        /// </summary>
        public bool ChargedSwing;

        /// <summary>Цель авто-атаки. Для хилера — союзник (≠ CurrentTarget). Пишет мозг, читает AutoAttackSystem.</summary>
        public RuntimeUnit AutoAttackTarget;

        /// <summary>Множитель урона следующей авто-атаки (§9.6, «Скрытность»): 0 = нет усиления. AutoAttackSystem применяет и сбрасывает (однострел).</summary>
        public float EmpowerDamageMult;

        /// <summary>Разовое плоское пробивание брони следующей авто-атаки («Атака из скрытности» игнорирует
        /// 20 ед. брони): 0 = нет. Ставится вместе с <see cref="EmpowerDamageMult"/>, тратится тем же ударом.</summary>
        public float EmpowerFlatPen;

        /// <summary>
        /// Дистанция отбрасывания цели усиленной авто-атакой («Восходящий удар» Монаха воды): 0 = удар не
        /// толкает. Ставится вместе с <see cref="EmpowerDamageMult"/> и тратится тем же ударом.
        /// </summary>
        public float EmpowerKnockback;

        /// <summary>
        /// По какому тегу снимается эффект, взведший усиление. У Убийцы это «Скрытность» (удар выводит
        /// его из тени), у периодических зарядов — свой тег, иначе усиленный удар срывал бы стелс,
        /// которого нет.
        /// </summary>
        /// <remarks>
        /// Тег, а не ссылка на эффект: усиление ставит КОМПОНЕНТ, а он stateless и шарится между
        /// носителями — своего экземпляра эффекта у него нет. Тег же уже есть у любого эффекта и
        /// проверяется битовой маской, то есть даром.
        /// </remarks>
        public Data.Definitions.EffectTag EmpowerConsumeTag = Data.Definitions.EffectTag.Stealth;

        /// <summary>
        /// Множитель длины замаха СЛЕДУЮЩЕЙ авто-атаки: 0 = обычный замах. Взводит комбо, которому нужен
        /// удар «вне очереди» ускоренной анимацией («Вихревой заход» монаха, §10.6); тратит вход в замах,
        /// как и <see cref="EmpowerDamageMult"/>.
        /// <para>Отдельно от <see cref="Data.Definitions.UnitData.ChargeAttackWindupMult"/> потому, что тот
        /// принадлежит КИТУ (удар с разбега — свойство реликвии), а этот — конкретному взведённому комбо.</para>
        /// </summary>
        public float NextWindupMult;

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

        // --- Каст и канал способности: свой FSM на int-тиках (M3) ---
        // Владелец перехода — AbilitySystem, и только он. Фазы авто-атаки (Phase) сюда НЕ расширены
        // намеренно: у них другой владелец (AutoAttackSystem), а поле с двумя владельцами — дефект.
        // Движение, авто-атака и презентация читают производные IsCasting / IsChanneling / IsCastBusy.

        /// <summary>Индекс кастуемой способности в <see cref="Abilities"/>, или <c>-1</c> = каст не идёт.</summary>
        public int CastingAbilityIndex = -1;

        /// <summary>Тиков подготовки осталось. 0 при идущем касте = подготовка кончилась (канал или применение).</summary>
        public int CastRemaining;

        /// <summary>Полная длительность подготовки в тиках — знаменатель прогресса для показа.</summary>
        public int CastTicks;

        /// <summary>Тиков канала осталось (0 = канала нет или он кончился).</summary>
        public int ChannelRemaining;

        /// <summary>Полная длительность канала в тиках — знаменатель прогресса для показа.</summary>
        public int ChannelTicks;

        /// <summary>Тиков до следующего срабатывания канала (0 = сработает в этом тике).</summary>
        public int ChannelTickRemaining;

        /// <summary>Снапшот цели на старте каста. Умерла к завершению — цель перевыбирается (решение Макса).</summary>
        public RuntimeUnit CastTarget;

        /// <summary>Идёт подготовка: способность заявлена, применение ещё не наступило.</summary>
        public bool IsCasting => CastingAbilityIndex >= 0 && CastRemaining > 0;

        /// <summary>Держится канал: подготовка позади, нагрузка срабатывает периодически.</summary>
        public bool IsChanneling => CastingAbilityIndex >= 0 && CastRemaining <= 0 && ChannelRemaining > 0;

        /// <summary>Занят кастом — подготовкой или каналом. То, что читают движение и авто-атака.</summary>
        public bool IsCastBusy => CastingAbilityIndex >= 0;

        // --- Призыв (M10). Владелец полей — SummonSystem; AbilitySystem их только заполняет при спавне. ---

        /// <summary>Кто призвал этого юнита. null = юнит пришёл из расстановки, а не из боя.</summary>
        public RuntimeUnit Summoner;

        /// <summary>Id способности, которая его призвала — по нему считается лимит живых призывов.</summary>
        public string SummonAbilityId;

        /// <summary>
        /// Тиков жизни осталось. 0 = призыв бессрочный (живёт до конца боя или своей смерти) — это
        /// нормальный случай, срок жизни объявляет сама способность.
        /// </summary>
        public int SummonLifetimeRemaining;

        /// <summary>Умирает вместе с призывателем (решение по каждому призыву своё, поле ассета).</summary>
        public bool DiesWithSummoner;

        /// <summary>Юнит призван в бою, а не выставлен расстановкой.</summary>
        public bool IsSummon => Summoner != null;

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

        /// <summary>
        /// Дееспособность на НАЧАЛО тика — по ней гейтятся реактивы, требующие действия
        /// (<see cref="Effects.IRequiresAgencyComponent"/>). Снимок, а не живой <see cref="CanAct"/>, потому
        /// что живой меняется ВНУТРИ тика: контроль пересчитывается синхронно в момент наложения эффекта, а
        /// урон проходит раньше фазы эффектов. Читая живой флаг, pre-damage начинал зависеть от того, чей
        /// атакующий стоит раньше в списке юнитов, — и зеркальные команды расходились на первом же стане
        /// (поймано <c>MirrorMatchTests</c> 2026-07-29).
        /// <para>Следствие, и оно правильное: стан, прилетевший ТЕМ ЖЕ тиком, что удар, щит поднять не
        /// мешает — ровно как «Оплот» ловит тот самый удар, который его разбудил.</para>
        /// </summary>
        public bool CanActAtTickStart = true;

        /// <summary>Может двигаться. false = обездвиживание/корень.</summary>
        public bool CanMove = true;

        /// <summary>Может кастовать способности. false = немота.</summary>
        public bool CanCast = true;
    }
}
