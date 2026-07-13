using UnityEngine;

namespace Guildmaster.Presentation.Design
{
    /// <summary>
    /// Единый конфиг «сочности» боя (design-система): все параметры impact-слоя в одном ассете, чтобы
    /// дизайнер крутил их без кода — включая ФОРМЫ (кривая возврата slowmo). Потребители тянут значения
    /// отсюда, а не хардкодят: <c>UnitView</c> (вспышка/сплющивание), <c>CombatPresenter</c> (hitstop,
    /// финишер), <c>ScreenShake</c> (тряска), <c>CombatFeelDirector</c> (slowmo/шейк по событиям).
    /// <para>VFX-секция добавится, когда подключим партиклы (пока YAGNI).</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Design/Combat Feel Config", fileName = "CombatFeelConfig")]
    public sealed class CombatFeelConfig : ScriptableObject
    {
        // --- Реакция на попадание (UnitView) ---
        [Header("Hit — вспышка")]
        [SerializeField] private Color _flashColor = Color.white;
        [SerializeField] private float _flashDuration = 0.25f;

        [Header("Hit — сплющивание")]
        [Tooltip("Сила: X растягивается / Y сжимается на эту долю (0.4 = ±40%).")]
        [SerializeField] private float _squashAmount = 0.4f;
        [SerializeField] private float _squashDuration = 0.25f;

        // --- Hitstop (CombatPresenter) ---
        [Header("Hitstop — локальная заморозка участников удара")]
        [SerializeField] private float _hitstopMin = 0.02f;
        [SerializeField] private float _hitstopMax = 0.09f;
        [Tooltip("Доля HP-урона от MaxHP цели, при которой hitstop максимален.")]
        [SerializeField, Range(0.01f, 1f)] private float _hitstopFullFrac = 0.25f;

        // --- Screenshake — форма (ScreenShake extension) ---
        [Header("Screenshake — форма")]
        [Tooltip("Смещение как доля обзора камеры (orthoSize) при интенсивности 1.")]
        [SerializeField] private float _shakePositionFraction = 0.08f;
        [Tooltip("Крен (roll) в градусах при интенсивности 1.")]
        [SerializeField] private float _shakeRotationStrength = 4f;
        [SerializeField] private float _shakeFrequency = 26f;
        [Tooltip("Скорость затухания амплитуды в секунду. Меньше = дольше трясёт.")]
        [SerializeField] private float _shakeDecayPerSec = 2f;

        // --- Screenshake — интенсивность по событию (CombatFeelDirector) ---
        [Header("Screenshake — интенсивность по событию (0..1)")]
        [SerializeField, Range(0f, 1f)] private float _killShake = 0.55f;
        [SerializeField, Range(0f, 1f)] private float _battleEndShake = 0.75f;
        [Tooltip("Порог доли урона от MaxHP: ниже — тяжёлый удар не трясёт.")]
        [SerializeField, Range(0f, 1f)] private float _heavyHitFrac = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _heavyShakeMin = 0.2f;
        [SerializeField, Range(0f, 1f)] private float _heavyShakeMax = 0.5f;

        // --- Slowmo — добивающий удар (CombatFeelDirector) ---
        [Header("Slowmo — добивающий удар (kill)")]
        [Tooltip("Во сколько замедлить мир на добивающий удар.")]
        [SerializeField, Range(0.01f, 1f)] private float _killSlowFactor = 0.4f;
        [Tooltip("За сколько секунд вернуться к норме (без удержания).")]
        [SerializeField] private float _killSlowRelease = 0.5f;
        [Tooltip("Секунд между kill-slowmo — на толпе киллов много.")]
        [SerializeField] private float _killSlowCooldown = 2f;

        // --- Slowmo — конец боя, финишер-момент (CombatFeelDirector + TimeScaleService) ---
        [Header("Slowmo — конец боя (финишер-момент)")]
        [Tooltip("Глубина slowmo на конце боя (0.1 = мир почти замер).")]
        [SerializeField, Range(0.01f, 1f)] private float _battleEndFactor = 0.1f;
        [Tooltip("Держим глубокое slowmo столько секунд, потом отпускаем.")]
        [SerializeField] private float _battleEndHold = 4f;
        [Tooltip("За сколько секунд возвращаемся к норме после удержания.")]
        [SerializeField] private float _battleEndRelease = 2f;
        [Tooltip("Форма возврата: нормализованное время (0→1) → доля возврата к норме (0→1).")]
        [SerializeField] private AnimationCurve _battleEndReleaseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        // --- Getters ---
        public Color FlashColor       => _flashColor;
        public float FlashDuration    => _flashDuration;
        public float SquashAmount     => _squashAmount;
        public float SquashDuration   => _squashDuration;

        public float HitstopMin       => _hitstopMin;
        public float HitstopMax       => _hitstopMax;
        public float HitstopFullFrac  => _hitstopFullFrac;

        public float ShakePositionFraction => _shakePositionFraction;
        public float ShakeRotationStrength => _shakeRotationStrength;
        public float ShakeFrequency        => _shakeFrequency;
        public float ShakeDecayPerSec      => _shakeDecayPerSec;

        public float KillShake        => _killShake;
        public float BattleEndShake    => _battleEndShake;
        public float HeavyHitFrac      => _heavyHitFrac;
        public float HeavyShakeMin     => _heavyShakeMin;
        public float HeavyShakeMax     => _heavyShakeMax;

        public float KillSlowFactor    => _killSlowFactor;
        public float KillSlowRelease   => _killSlowRelease;
        public float KillSlowCooldown  => _killSlowCooldown;

        public float BattleEndFactor   => _battleEndFactor;
        public float BattleEndHold     => _battleEndHold;
        public float BattleEndRelease  => _battleEndRelease;
        public AnimationCurve BattleEndReleaseCurve => _battleEndReleaseCurve;

        /// <summary>Сколько всего длится финишер-момент (удержание + возврат) — финишер держит кадр столько же.</summary>
        public float FinisherHoldSeconds => _battleEndHold + _battleEndRelease;
    }
}
