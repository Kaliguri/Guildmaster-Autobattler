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

        /// <summary>
        /// Тиков принудительного смещения осталось (§9.9, «Шквальный толчок»): &gt; 0 — юнит в полёте.
        /// Жёсткое состояние: движет DisplacementSystem, юнит не действует/не двигается сам, сопротивление
        /// контролю НЕ применяется (LoL-подкидывание). Владелец — DisplacementSystem.
        /// </summary>
        public int DisplacedTicksRemaining;

        // --- Авто-атака: двухфазный windup на int-тиках (вики «14») ---

        /// <summary>Кулдаун автоатаки в сим-тиках. 0 = готов к атаке. Рестартится в начале замаха (якорь).</summary>
        public int AttackCooldownTicks;

        /// <summary>Идёт замах (windup): юнит занёс оружие, урон ещё не нанесён. Рутит движение (MovementSystem).</summary>
        public bool IsWindingUp;

        /// <summary>Тиков замаха осталось до кадра контакта. Когда ≤ 0 — резолв удара.</summary>
        public int WindupRemaining;

        /// <summary>Полная длительность текущего замаха в тиках (посчитана раз на старте, не пересчитывается на лету).</summary>
        public int WindupTicks;

        /// <summary>Снапшот цели на старте замаха: удар наносится по ней (если жива и в радиусе к концу замаха).</summary>
        public RuntimeUnit WindupTarget;

        /// <summary>Помечен DeathSystem — исключается из всех систем с текущего тика.</summary>
        public bool IsDead;

        /// <summary>SO «Чемпион»: тип атаки, стат-блок, эффекты (Фаза 2).</summary>
        public RelicData Relic;

        /// <summary>SO «Пилот»: идентичность, перки (Фаза 2/4).</summary>
        public VesselData Vessel;

        // --- Эффекты (Фаза 2) ---

        /// <summary>Активные эффекты на юните. Мутирует только <c>EffectSystem</c> (через pending, не во время итерации).</summary>
        public readonly List<RuntimeEffect> ActiveEffects = new List<RuntimeEffect>();

        /// <summary>Битовая маска тегов активных эффектов. Обновляется при add/remove — быстрый запрос для AI (Фаза 3) и диспела.</summary>
        public EffectTag EffectTagMask;

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
