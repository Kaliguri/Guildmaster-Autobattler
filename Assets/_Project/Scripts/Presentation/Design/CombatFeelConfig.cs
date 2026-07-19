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

        // --- Боевые цифры — масштаб по величине урона (CombatPresenter → FloatingText) ---
        [Header("Numbers — масштаб цифры по величине удара")]
        [Tooltip("Во сколько раз крупнее цифра HP-урона на тяжёлом ударе (доля урона >= NumberFullFrac). " +
                 "Поп/разлёт/тайминг цифры настраиваются на самом префабе FloatingText.")]
        [SerializeField, Range(1f, 3f)] private float _numberMaxScale = 1.7f;
        [Tooltip("Доля HP-урона от MaxHP цели, при которой цифра достигает NumberMaxScale.")]
        [SerializeField, Range(0.01f, 1f)] private float _numberFullFrac = 0.25f;

        // --- Смерть — вспышка + разлёт спрайта на осколки (DeathShatter/UnitView) ---
        [Header("Death shatter — вспышка + разлёт на треугольники")]
        [Tooltip("За сколько секунд спрайт вспыхивает в белый ПЕРЕД расколом.")]
        [SerializeField] private float _shatterFlashIn = 0.08f;
        [Tooltip("Длительность разлёта осколков, сек (дольше = медленнее дрейф и плавнее исчезновение).")]
        [SerializeField] private float _shatterDuration = 0.75f;
        [Tooltip("Дальность разлёта в ВЫСОТАХ спрайта (1 = осколки уходят примерно на рост персонажа).")]
        [SerializeField] private float _shatterExplode = 1.2f;
        [Tooltip("Гравитация осколков в высотах спрайта (0 = без падения вниз, просто дрейф наружу).")]
        [SerializeField] private float _shatterGravity = 0f;
        [Tooltip("Скорость вращения осколков (рад за раскол). Меньше = медленнее крутятся.")]
        [SerializeField] private float _shatterSpin = 3f;
        [Tooltip("Разброс направлений от радиального (рад) — больше = летят «во все стороны», не строго от центра.")]
        [SerializeField] private float _shatterSpread = 1.2f;
        [Tooltip("Размер чанка-осколка в ИСХОДНЫХ пикселях спрайта (меньше = мельче куски, больше = крупнее блоки).")]
        [SerializeField, Range(2, 16)] private int _shatterBlockPixels = 6;
        [Tooltip("Сила ПСЕВДО-3D кувыркания осколков (переворот вокруг случайной оси). 0 = плоско, больше = активнее кувыркаются.")]
        [SerializeField] private float _shatterTumble = 9f;
        [Tooltip("Восходящий дрейф: смещает разлёт вверх-и-наружу (0 = строго радиально, больше = осколки уходят вверх).")]
        [SerializeField] private float _shatterUpBias = 0.6f;
        [Tooltip("За сколько секунд impact-вспышка спадает обратно (осколки возвращают исходный цвет юнита перед выцветанием).")]
        [SerializeField] private float _shatterFlashOut = 0.12f;
        [Tooltip("Цвет выцветания осколков к концу (SAO-бирюза). HDR — яркость >1 ловит bloom.")]
        [ColorUsage(true, true)] [SerializeField] private Color _shatterEmberColor = new Color(0.25f, 0.9f, 1f, 1f);
        [Tooltip("Множитель яркости цвета выцветания (emissive под bloom). 1 = как есть, больше = сильнее светятся.")]
        [SerializeField] private float _shatterEmberBoost = 2f;
        [Tooltip("С какого прогресса разлёта (0..1) осколки начинают выцветать в ember-цвет.")]
        [SerializeField, Range(0f, 1f)] private float _shatterEmberStart = 0.4f;

        // --- Slowmo — добивающий удар (CombatFeelDirector) ---
        [Header("Slowmo — добивающий удар (kill)")]
        [Tooltip("Во сколько замедлить мир на добивающий удар.")]
        [SerializeField, Range(0.01f, 1f)] private float _killSlowFactor = 0.4f;
        [Tooltip("За сколько секунд вернуться к норме (без удержания).")]
        [SerializeField] private float _killSlowRelease = 0.5f;
        [Tooltip("Секунд между kill-slowmo — на толпе киллов много.")]
        [SerializeField] private float _killSlowCooldown = 2f;

        // --- Slowmo — финишер-концовка, ступени (CombatFeelDirector + TimeScaleService) ---
        // Таймлайн финального удара: полная пауза → slowmo анимации смерти → сильное slowmo разлёта → возврат.
        [Header("Slowmo — финишер-концовка (ступени)")]
        [Tooltip("Ступень 1 — полная пауза (timeScale 0) на финальном ударе, пока держится хит-эффект, сек.")]
        [SerializeField] private float _finisherPause = 1f;
        [Tooltip("Ступень 2 — slowmo анимации смерти (0.5 = полускорость).")]
        [SerializeField, Range(0.01f, 1f)] private float _finisherDeathFactor = 0.5f;
        [Tooltip("Ступень 2 — сколько держать slowmo смерти, сек.")]
        [SerializeField] private float _finisherDeathDuration = 1.5f;
        [Tooltip("Ступень 3 — сильное slowmo во время разлёта осколков (0.1 = почти стоп).")]
        [SerializeField, Range(0.01f, 1f)] private float _finisherShatterFactor = 0.1f;
        [Tooltip("Ступень 3 — сколько держать сильное slowmo разлёта, сек.")]
        [SerializeField] private float _finisherShatterDuration = 3f;
        [Tooltip("Ступень 4 — за сколько секунд плавно вернуться к норме.")]
        [SerializeField] private float _finisherReturn = 2f;
        [Tooltip("Форма возврата: нормализованное время (0→1) → доля возврата к норме (0→1).")]
        [SerializeField] private AnimationCurve _finisherReturnCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        // --- VFX — пиксельные брызги (CombatVfx / PixelBurst) ---
        [Header("VFX — hit-spark (попадание, в HitPoint)")]
        [SerializeField] private PixelBurstPreset _hitSpark = new PixelBurstPreset
            { Color = new Color(1f, 0.95f, 0.7f), Count = 14, Speed = 0.9f, Size = 0.06f, Life = 0.35f, Gravity = 0.5f, SpreadDeg = 360f };
        [Header("VFX — muzzle (выстрел, в ShotPoint)")]
        [SerializeField] private PixelBurstPreset _muzzle = new PixelBurstPreset
            { Color = new Color(1f, 0.85f, 0.4f), Count = 8, Speed = 0.6f, Size = 0.06f, Life = 0.18f, Gravity = 0f, SpreadDeg = 55f };
        [Header("VFX — impact dust (мили-удар, у ног цели)")]
        [SerializeField] private PixelBurstPreset _impactDust = new PixelBurstPreset
            { Color = new Color(0.75f, 0.68f, 0.55f), Count = 8, Speed = 0.5f, Size = 0.07f, Life = 0.5f, Gravity = 0.4f, SpreadDeg = 130f };
        [Header("VFX — heal (лечение, в HitPoint)")]
        [SerializeField] private PixelBurstPreset _heal = new PixelBurstPreset
            { Color = new Color(0.55f, 1f, 0.6f), Count = 10, Speed = 0.6f, Size = 0.05f, Life = 0.55f, Gravity = -0.4f, SpreadDeg = 45f };

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

        public float NumberMaxScale    => _numberMaxScale;
        public float NumberFullFrac    => _numberFullFrac;

        public float ShatterFlashIn    => _shatterFlashIn;
        public float ShatterDuration   => _shatterDuration;
        public float ShatterExplode    => _shatterExplode;
        public float ShatterGravity    => _shatterGravity;
        public float ShatterSpin       => _shatterSpin;
        public float ShatterSpread     => _shatterSpread;
        public int   ShatterBlockPixels => _shatterBlockPixels;
        public float ShatterTumble     => _shatterTumble;
        public float ShatterUpBias     => _shatterUpBias;
        public float ShatterFlashOut   => _shatterFlashOut;
        public Color ShatterEmberColor => _shatterEmberColor;
        public float ShatterEmberBoost => _shatterEmberBoost;
        public float ShatterEmberStart => _shatterEmberStart;

        public PixelBurstPreset HitSpark   => _hitSpark;
        public PixelBurstPreset Muzzle     => _muzzle;
        public PixelBurstPreset ImpactDust => _impactDust;
        public PixelBurstPreset Heal       => _heal;

        public float KillSlowFactor    => _killSlowFactor;
        public float KillSlowRelease   => _killSlowRelease;
        public float KillSlowCooldown  => _killSlowCooldown;

        public float FinisherPause          => _finisherPause;
        public float FinisherDeathFactor    => _finisherDeathFactor;
        public float FinisherDeathDuration  => _finisherDeathDuration;
        public float FinisherShatterFactor  => _finisherShatterFactor;
        public float FinisherShatterDuration => _finisherShatterDuration;
        public float FinisherReturn         => _finisherReturn;
        public AnimationCurve FinisherReturnCurve => _finisherReturnCurve;

        /// <summary>Полная длительность финишер-таймлайна (пауза + death + shatter + возврат) — финишер держит кадр столько же.</summary>
        public float FinisherHoldSeconds => _finisherPause + _finisherDeathDuration + _finisherShatterDuration + _finisherReturn;
    }
}
