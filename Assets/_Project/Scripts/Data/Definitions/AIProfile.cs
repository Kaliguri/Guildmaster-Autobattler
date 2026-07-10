using System;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>Режим авто-атаки: обычный урон или хил-автоатака (цель-союзник + Heal вместо урона).</summary>
    public enum AutoAttackMode
    {
        Damage = 0,
        Heal   = 1,
    }

    /// <summary>
    /// Триггер пассивного навыка (блок F, вики «13» §2.1). Всегда срабатывает при смертельном ударе.
    /// </summary>
    public enum PassiveTrigger
    {
        /// <summary>Никогда (нет пассива-реактива).</summary>
        None = 0,

        /// <summary>На любой входящий удар.</summary>
        AnyHit = 1,

        /// <summary>На удар выше <see cref="AIProfile.PassiveThresholdPct"/>% от макс HP (+ всегда при смертельном) — Защитник.</summary>
        OnHitAbovePctMaxHp = 2,

        /// <summary>Всегда (монах: реакция на собственный толчок/толчок цели).</summary>
        Always = 3,
    }

    /// <summary>
    /// «Параметризованные пресеты» AI (РЕШЕНИЕ §2.1): фиксированный набор типизированных ручек за
    /// стабильным швом <c>IUnitBrain</c>, НЕ полиморфный [SerializeReference]-граф. Лежит на
    /// <see cref="RelicData.Ai"/>; единственная реализация <c>ProfileBrain</c> его интерпретирует.
    /// Состав ручек продиктован ГД-моделью (блоки A–G). Реликвия включает свой поднабор.
    /// <para>Cast-условия живут по-способности на <see cref="AbilityData"/> (блоки C/D/E) — приходят
    /// с вертикальным срезом, не здесь.</para>
    /// </summary>
    [Serializable]
    public sealed class AIProfile
    {
        /// <summary>
        /// Дефолт порога <see cref="PassiveTrigger.OnHitAbovePctMaxHp"/> (доля макс HP цели, 0..1).
        /// Единственное объявление значения — использовать здесь, в дефолтах и в фолбэках компонентов
        /// (вики «13» §4.2 п.2). TODO пакет 2: переезжает в SimTuningConfig.
        /// </summary>
        public const float DefaultPassiveThresholdPct = 0.2f;

        [Header("Auto-attack targeting (block A)")]
        [Tooltip("Кого бьёт авто-атака. Один режим (дропдаун). Тай-брейк — дистанция, затем Id.")]
        [SerializeField] private TargetingMode _autoAttackTargeting = TargetingMode.Nearest;

        [Tooltip("Damage / Heal — инверсия цели авто-атаки на союзника (Светлый пастырь).")]
        [SerializeField] private AutoAttackMode _autoAttackMode = AutoAttackMode.Damage;

        [Tooltip("Тег для режима PreferTagged (напр. Marked у Следопыта, Frozen у Криоманта).")]
        [SerializeField] private EffectTag _targetTag = EffectTag.None;

        [Header("Retreat (block B) — B > A, зазор от дёрганья")]
        [SerializeField] private Retreat _retreat = Retreat.Disabled;

        [Header("Kite (block G) — только следопыт, T > F; требует CanAttackWhileMoving")]
        [SerializeField] private Kite _kite = Kite.Disabled;

        [Header("Passive trigger (block F)")]
        [SerializeField] private PassiveTrigger _passiveTrigger = PassiveTrigger.None;

        [Tooltip("Порог для OnHitAbovePctMaxHp — доля макс HP цели (0..1).")]
        [SerializeField] private float _passiveThresholdPct = DefaultPassiveThresholdPct;

        /// <summary>Безпараметрический — для Unity-сериализации и дефолтов (Nearest, без отступления).</summary>
        public AIProfile() { }

        /// <summary>Кодовая сборка профиля — для дефолтов харнесса/спавн-команд и тестов.</summary>
        public AIProfile(
            TargetingMode  autoAttackTargeting,
            AutoAttackMode autoAttackMode      = AutoAttackMode.Damage,
            EffectTag      targetTag           = EffectTag.None,
            Retreat        retreat             = default,
            Kite           kite                = default,
            PassiveTrigger passiveTrigger      = PassiveTrigger.None,
            float          passiveThresholdPct = DefaultPassiveThresholdPct)
        {
            _autoAttackTargeting = autoAttackTargeting;
            _autoAttackMode      = autoAttackMode;
            _targetTag           = targetTag;
            _retreat             = retreat;
            _kite                = kite;
            _passiveTrigger      = passiveTrigger;
            _passiveThresholdPct = passiveThresholdPct;
        }

        public TargetingMode  AutoAttackTargeting => _autoAttackTargeting;
        public AutoAttackMode AutoAttackMode      => _autoAttackMode;
        public EffectTag      TargetTag           => _targetTag;
        public Retreat        Retreat             => _retreat;
        public Kite           Kite                => _kite;
        public PassiveTrigger PassiveTrigger      => _passiveTrigger;
        public float          PassiveThresholdPct => _passiveThresholdPct;
    }

    /// <summary>Отступление по HP% (блок B). <c>ReturnAtHpPct</c> &gt; <c>FleeAtHpPct</c> — гистерезис от дёрганья.</summary>
    [Serializable]
    public struct Retreat
    {
        public bool  Enabled;
        [Tooltip("HP% (0..1), ниже которого юнит отступает.")]
        public float FleeAtHpPct;
        [Tooltip("HP% (0..1), выше которого возвращается в бой. Должен быть > FleeAtHpPct.")]
        public float ReturnAtHpPct;

        public static Retreat Disabled => new Retreat { Enabled = false, FleeAtHpPct = 0.25f, ReturnAtHpPct = 0.5f };
    }

    /// <summary>Кайт (блок G). <c>FallbackDist</c> &gt; <c>FleeDist</c> — зона удержания дистанции.</summary>
    [Serializable]
    public struct Kite
    {
        public bool  Enabled;
        [Tooltip("Дистанция, ближе которой начинаем отходить.")]
        public float FleeDist;
        [Tooltip("Дистанция, до которой откатываемся. Должна быть > FleeDist.")]
        public float FallbackDist;

        public static Kite Disabled => new Kite { Enabled = false, FleeDist = 1.5f, FallbackDist = 3f };
    }
}
