using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Presentation.Design
{
    /// <summary>
    /// Единый конфиг «сочности» боя (design-система): все параметры impact-слоя в одном ассете, чтобы
    /// дизайнер крутил их без кода — включая ФОРМЫ (кривая возврата slowmo). Потребители тянут значения
    /// отсюда, а не хардкодят: <c>UnitView</c> (вспышка/сплющивание), <c>CombatPresenter</c> (hitstop,
    /// финишер), <c>ScreenShake</c> (тряска), <c>CombatFeelDirector</c> (slowmo/шейк по событиям).
    /// <para>Микро-feel (пыль/nudge/flip/дыхание/тинт вспышки) — тумблеры в секции
    /// «Micro Feel — toggles»: выключил здесь = эффект мёртв везде, без правок кода.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Design/Combat Feel Config", fileName = "CombatFeelConfig")]
    public sealed class CombatFeelConfig : ScriptableObject
    {
        // --- Micro Feel — единая точка выключения ---
        [Header("Micro Feel — toggles (выключи здесь = эффект мёртв везде)")]
        [Tooltip("Пыль у ног при старте/стопе бега (Vfx ContactDust на FeetPoint).")]
        [SerializeField] private bool _enableContactDust = true;
        [Tooltip("Цель чуть уезжает от удара на hitstop и возвращается (только презентация).")]
        [SerializeField] private bool _enableHitNudge = true;
        [Tooltip("Разворот через сплющивание по X вместо мгновенного flipX.")]
        [SerializeField] private bool _enableFacingFlipSquash = true;
        [Tooltip("Микро-вздрог при смене цели авто-атаки.")]
        [SerializeField] private bool _enableTargetAcquireTell = true;
        [Tooltip("Idle-дыхание: лёгкий пульс масштаба в стойке.")]
        [SerializeField] private bool _enableIdleBreath = true;
        [Tooltip("Цвет hit-flash по школе/сродству урона (иначе — Flash Color ниже).")]
        [SerializeField] private bool _enableSchoolFlash = true;
        [Tooltip("Микро-оттяг атакующего назад в начале замаха.")]
        [SerializeField] private bool _enableAttackAnticipation = true;
        [Tooltip("Микро-рывок атакующего к цели в момент импакта.")]
        [SerializeField] private bool _enableAttackerLunge = true;
        [Tooltip("Короткий белый силуэт/hold вспышки на 1–2 кадра импакта.")]
        [SerializeField] private bool _enableImpactFrame = true;
        [Tooltip("Перед shatter: белеет/дрожит ~0.1с (телеграф смерти).")]
        [SerializeField] private bool _enableDeathAnticipation = true;
        [Tooltip("Боевые цифры летят по дуге с гравитацией (а не строго вверх).")]
        [SerializeField] private bool _enableFloatingTextArc = true;
        [Tooltip("Микро-punch масштаба HP-бара при уроне (trail-ghost уже в HealthBarView).")]
        [SerializeField] private bool _enableHpBarPunch = true;
        [Tooltip("Мягкая вспышка на теле при лечении: хил читался только цифрой, тело на него не отвечало.")]
        [SerializeField] private bool _enableHealFlash = true;

        [Header("Micro Feel — contact dust")]
        [Tooltip("Минимальный интервал между пылью на одном юните, сек.")]
        [SerializeField] private float _contactDustCooldown = 0.35f;

        [Header("Micro Feel — hit nudge")]
        [Tooltip("Насколько цель уезжает от удара, мировые ед.")]
        [SerializeField] private float _hitNudgeDistance = 0.08f;
        [Tooltip("Длительность отъезда+возврата, сек (unscaled).")]
        [SerializeField] private float _hitNudgeDuration = 0.12f;

        [Header("Micro Feel — facing flip squash")]
        [Tooltip("Сила сплющивания по X при развороте (0.35 = сжать до 65%).")]
        [SerializeField, Range(0.05f, 0.9f)] private float _facingFlipSquashAmount = 0.35f;
        [Tooltip("Длительность разворота-сплющивания, сек.")]
        [SerializeField] private float _facingFlipDuration = 0.1f;

        [Header("Micro Feel — target acquire tell")]
        [Tooltip("Сила микро-вздрога (доля масштаба).")]
        [SerializeField, Range(0.01f, 0.3f)] private float _targetAcquireTwitch = 0.06f;
        [SerializeField] private float _targetAcquireDuration = 0.08f;

        [Header("Micro Feel — idle breath")]
        [Tooltip("Амплитуда пульса масштаба (±доля).")]
        [SerializeField, Range(0.005f, 0.08f)] private float _idleBreathAmplitude = 0.02f;
        [Tooltip("Период одного вдоха-выдоха, сек.")]
        [SerializeField] private float _idleBreathPeriod = 2.2f;

        [Header("Micro Feel — school flash colors")]
        [SerializeField] private Color _flashPhysical = Color.white;
        [SerializeField] private Color _flashElemental = new Color(1f, 0.55f, 0.25f, 1f);
        [SerializeField] private Color _flashTrue = new Color(1f, 0.95f, 0.55f, 1f);
        [SerializeField] private Color _flashPoison = new Color(0.45f, 1f, 0.4f, 1f);
        [SerializeField] private Color _flashLight = new Color(1f, 0.95f, 0.75f, 1f);
        [SerializeField] private Color _flashDark = new Color(0.55f, 0.35f, 0.85f, 1f);

        [Header("Micro Feel — anticipation / lunge")]
        [Tooltip("Оттяг назад от цели в начале замаха, мировые ед.")]
        [SerializeField] private float _anticipationDistance = 0.06f;
        [Tooltip("Длительность оттяга, сек (unscaled).")]
        [SerializeField] private float _anticipationDuration = 0.1f;
        [Tooltip("Рывок к цели на импакте, мировые ед.")]
        [SerializeField] private float _lungeDistance = 0.1f;
        [Tooltip("Длительность рывка+возврата, сек (unscaled).")]
        [SerializeField] private float _lungeDuration = 0.1f;

        [Header("Micro Feel — impact frame")]
        [Tooltip("Сколько держать вспышку на пике перед спадом, сек (≈1–2 кадра).")]
        [SerializeField] private float _impactFrameHold = 0.04f;

        [Header("Micro Feel — death anticipation")]
        [Tooltip("Пауза белого силуэта/дрожи перед shatter, сек.")]
        [SerializeField] private float _deathAnticipateDuration = 0.1f;
        [Tooltip("Сила дрожи масштаба в anticipation смерти.")]
        [SerializeField, Range(0.01f, 0.2f)] private float _deathAnticipateShake = 0.06f;

        [Header("Micro Feel — floating text arc")]
        [Tooltip("Гравитация дуги цифры (мировые ед/с²). 0 = строго вверх.")]
        [SerializeField] private float _numberArcGravity = 2.2f;

        [Header("Micro Feel — heal flash")]
        [Tooltip("Цвет вспышки при лечении.")]
        [SerializeField] private Color _healFlashColor = new Color(0.55f, 1f, 0.65f, 1f);
        [Tooltip("Сила вспышки лечения: заметно слабее удара — лечение греет, а не бьёт.")]
        [SerializeField, Range(0.1f, 1f)] private float _healFlashPeak = 0.5f;

        [Header("Micro Feel — cast outline")]
        [Tooltip("Длительность контура на теле при касте, сек. 0 = контура нет.")]
        [SerializeField, Range(0f, 1.5f)] private float _castOutlineDuration = 0.45f;
        [Tooltip("Плотность контура на пике.")]
        [SerializeField, Range(0f, 1f)] private float _castOutlineStrength = 0.9f;

        [Header("Micro Feel — low HP pulse")]
        [Tooltip("Доля HP, ниже которой полоса тревожно дышит светом. 0 = пульса нет.")]
        [SerializeField, Range(0f, 0.6f)] private float _lowHpThreshold = 0.25f;
        [Tooltip("Период дыхания, сек.")]
        [SerializeField, Range(0.2f, 3f)] private float _lowHpPulsePeriod = 0.85f;
        [Tooltip("Сила подсветки на пике у самой смерти (доля яркости).")]
        [SerializeField, Range(0f, 1.5f)] private float _lowHpPulseAmount = 0.5f;

        [Header("Micro Feel — HP bar punch")]
        [Tooltip("Пик перелёта масштаба бара при уроне.")]
        [SerializeField, Range(0.02f, 0.25f)] private float _hpBarPunchAmount = 0.08f;
        [SerializeField] private float _hpBarPunchDuration = 0.12f;

        // --- Реакция на попадание (UnitView) ---
        [Header("Hit — вспышка")]
        [Tooltip("Фолбэк-цвет вспышки, если School Flash выключен.")]
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
        [Tooltip("Кривая веса hitstop по нормализованной доле урона (0..1 → 0..1). Linear = прежнее поведение.")]
        [SerializeField] private AnimationCurve _hitstopWeightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

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
        [Tooltip("За сколько секунд пересвет спадает и осколки становятся видны как угольки.")]
        [SerializeField] private float _shatterFlashOut = 0.12f;
        [Tooltip("Цвет «около-белых» осколков (цифровой пересвет). HDR — яркость >1 ловит bloom. " +
                 "Остальные осколки красятся палитрой ПАВШЕГО юнита из его UnitData.")]
        [ColorUsage(true, true)] [SerializeField] private Color _shardWhiteColor = new Color(1.6f, 1.62f, 1.7f, 1f);
        [Tooltip("Какая доля осколков около-белая; остальные — цвета павшего.")]
        [SerializeField, Range(0f, 1f)] private float _shardWhiteShare = 0.5f;
        [Tooltip("Форма гашения: <1 — осколок держит яркость почти весь путь и тухнет в конце; 1 = линейно.")]
        [SerializeField, Range(0.15f, 3f)] private float _shatterFadePower = 0.35f;
        [Tooltip("Разброс скорости угасания между осколками (0 = все гаснут разом, ровно и неживо).")]
        [SerializeField, Range(0f, 0.8f)] private float _shatterLifeVariance = 0.35f;
        [Tooltip("Аддитивность уголька: 1 = догорающий осколок светит ПОВЕРХ фона, 0 = обычная прозрачность.")]
        [SerializeField, Range(0f, 1f)] private float _shatterGlow = 1f;
        [Tooltip("Множитель яркости цвета выцветания (emissive под bloom). 1 = как есть, больше = сильнее светятся.")]
        [SerializeField] private float _shatterEmberBoost = 2f;
        [Tooltip("Сколько фактуры спрайта остаётся в ЯРКОСТИ осколка (цвет всегда наш). 0 = ровное свечение.")]
        [SerializeField, Range(0f, 1f)] private float _shatterLuma = 0.35f;
        [Tooltip("Микро-hold перед разлётом: осколки «кристаллизуются», сек (0 = без hold).")]
        [SerializeField] private float _shatterHold = 0.05f;
        [Tooltip("Пол шкалы времени для разлёта: насколько сильно финишер-slowmo вправе его замедлять. " +
                 "1 = разлёт вообще не замедляется, 0.1 = тянется вслед за самым сильным slowmo.")]
        [SerializeField, Range(0.05f, 1f)] private float _shatterMinTimeScale = 0.4f;

        // --- Смерть — стадия голограммы (UnitView + SH_Sprite_HitFlash) ---
        // Тело сначала теряет плотность и становится «данными», и только потом вспыхивает и рассыпается.
        // Без этой стадии юнит белел мгновенно, и раскол читался как поломка спрайта, а не как развоплощение.
        [Tooltip("Цвет пересвета перед расколом. Отдельно от Flash Color: hit-flash обязан оставаться в " +
                 "пределах экрана, а смерть должна ПРОБИВАТЬ порог bloom — иначе «яркий белый» просто белый.")]
        [ColorUsage(true, true)] [SerializeField] private Color _deathFlashColor = new Color(2.5f, 2.5f, 2.6f, 1f);

        [Tooltip("Играть клип смерти (падение тела) перед голограммой. Выкл = юнит развоплощается стоя, " +
                 "на том кадре, где его достали.")]
        [SerializeField] private bool _playDeathClip;

        [Header("Death — стадия голограммы (перед вспышкой)")]
        [Tooltip("Длительность голограммы, сек. 0 = стадия выключена (сразу белая вспышка).")]
        [SerializeField] private float _deathHologramDuration = 0.3f;
        [Tooltip("Цвет тела в голограмме. HDR — яркость >1 ловит bloom.")]
        [ColorUsage(true, true)] [SerializeField] private Color _hologramColor = new Color(0.3f, 0.95f, 1f, 1f);
        [Tooltip("Прозрачность тела в голограмме (контур остаётся плотным).")]
        [SerializeField, Range(0.05f, 1f)] private float _hologramBodyAlpha = 0.45f;
        [Tooltip("Шаг скан-линий в ПИКСЕЛЯХ спрайта.")]
        [SerializeField, Range(1f, 12f)] private float _hologramScanScale = 3f;
        [Tooltip("Глубина скан-линий (0 = ровная заливка без полос).")]
        [SerializeField, Range(0f, 1f)] private float _hologramScanAmount = 0.35f;

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

        // --- VFX — префабы через VfxData (SO → префаб → пул → сокет) ---
        [Header("VFX — prefab refs (VfxData)")]
        [Tooltip("Искры попадания в HitPoint.")]
        [SerializeField] private VfxData _vfxHitSpark;
        [Tooltip("Вспышка выстрела в ShotPoint.")]
        [SerializeField] private VfxData _vfxMuzzle;
        [Tooltip("Пыль у ног цели на мили-ударе.")]
        [SerializeField] private VfxData _vfxImpactDust;
        [Tooltip("Пыль у ног при старте/стопе бега.")]
        [SerializeField] private VfxData _vfxContactDust;
        [Tooltip("Искры лечения в HitPoint.")]
        [SerializeField] private VfxData _vfxHeal;

        [Header("VFX — hit-spark intensity by damage weight")]
        [Tooltip("Множитель scale hit-spark на лёгком ударе (доля урона → 0).")]
        [SerializeField, Range(0.05f, 1f)] private float _vfxHitIntensityMin = 0.35f;
        [Tooltip("Множитель scale hit-spark на тяжёлом ударе (доля ≥ HeavyHitFrac).")]
        [SerializeField, Range(0.05f, 2f)] private float _vfxHitIntensityMax = 1f;
        [Tooltip("Множитель КОЛИЧЕСТВА искр на лёгком ударе. Сила удара читается частотой, а не размером.")]
        [SerializeField, Range(0.05f, 1f)] private float _vfxHitCountMin = 0.4f;
        [Tooltip("Множитель количества искр на тяжёлом ударе (доля ≥ HeavyHitFrac).")]
        [SerializeField, Range(0.5f, 4f)] private float _vfxHitCountMax = 2.2f;

        [Tooltip("Всплеск при касте способности за ману — «смотри, я сейчас выдам». Цвет берётся из палитры " +
                 "самого юнита (UnitData), поэтому один префаб служит всем.")]
        [SerializeField] private VfxData _vfxCastBurst;

        // --- Getters ---
        public bool  EnableContactDust       => _enableContactDust;
        public bool  EnableHitNudge          => _enableHitNudge;
        public bool  EnableFacingFlipSquash  => _enableFacingFlipSquash;
        public bool  EnableTargetAcquireTell => _enableTargetAcquireTell;
        public bool  EnableIdleBreath        => _enableIdleBreath;
        public bool  EnableAttackAnticipation => _enableAttackAnticipation;
        public bool  EnableAttackerLunge     => _enableAttackerLunge;
        public bool  EnableImpactFrame       => _enableImpactFrame;
        public bool  EnableDeathAnticipation => _enableDeathAnticipation;
        public bool  EnableFloatingTextArc   => _enableFloatingTextArc;
        public bool  EnableHpBarPunch        => _enableHpBarPunch;
        public bool  EnableHealFlash         => _enableHealFlash;

        public Color HealFlashColor      => _healFlashColor;
        public float HealFlashPeak       => _healFlashPeak;
        public float CastOutlineDuration => _castOutlineDuration;
        public float CastOutlineStrength => _castOutlineStrength;
        public float LowHpThreshold      => _lowHpThreshold;
        public float LowHpPulsePeriod    => _lowHpPulsePeriod;
        public float LowHpPulseAmount    => _lowHpPulseAmount;

        public float ContactDustCooldown     => _contactDustCooldown;
        public float HitNudgeDistance        => _hitNudgeDistance;
        public float HitNudgeDuration        => _hitNudgeDuration;
        public float FacingFlipSquashAmount  => _facingFlipSquashAmount;
        public float FacingFlipDuration      => _facingFlipDuration;
        public float TargetAcquireTwitch     => _targetAcquireTwitch;
        public float TargetAcquireDuration   => _targetAcquireDuration;
        public float IdleBreathAmplitude     => _idleBreathAmplitude;
        public float IdleBreathPeriod        => _idleBreathPeriod;
        public float AnticipationDistance    => _anticipationDistance;
        public float AnticipationDuration    => _anticipationDuration;
        public float LungeDistance           => _lungeDistance;
        public float LungeDuration           => _lungeDuration;
        public float ImpactFrameHold         => _impactFrameHold;
        public float DeathAnticipateDuration => _deathAnticipateDuration;
        public float DeathAnticipateShake    => _deathAnticipateShake;
        public float NumberArcGravity        => _numberArcGravity;
        public float HpBarPunchAmount        => _hpBarPunchAmount;
        public float HpBarPunchDuration      => _hpBarPunchDuration;

        public Color FlashColor       => _flashColor;
        public float FlashDuration    => _flashDuration;
        public float SquashAmount     => _squashAmount;
        public float SquashDuration   => _squashDuration;

        // Сырых чисел hitstop наружу нет: их складывает EvaluateHitstopSeconds, и он единственный,
        // кто знает формулу. Геттеры-дубли не звал никто (аудит 2026-07-26, волна 2). Флаг вспышки
        // по школе тоже читается только здесь — снаружи её выбирает EvaluateFlashColor.

        /// <summary>Hitstop в секундах по доле HP-урона от MaxHP (кривая веса + lerp min..max).</summary>
        public float EvaluateHitstopSeconds(float hpDamageFrac)
        {
            float t = Mathf.Clamp01(hpDamageFrac / Mathf.Max(1e-4f, _hitstopFullFrac));
            float w = _hitstopWeightCurve != null ? Mathf.Clamp01(_hitstopWeightCurve.Evaluate(t)) : t;
            return Mathf.Lerp(_hitstopMin, _hitstopMax, w);
        }

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
        public Color ShardWhiteColor   => _shardWhiteColor;
        public float ShardWhiteShare   => _shardWhiteShare;
        public float ShatterFadePower  => _shatterFadePower;
        public float ShatterLifeVariance => _shatterLifeVariance;
        public float ShatterGlow       => _shatterGlow;
        public float ShatterEmberBoost => _shatterEmberBoost;
        public float ShatterLuma       => _shatterLuma;
        public float ShatterHold       => _shatterHold;
        public float ShatterMinTimeScale => _shatterMinTimeScale;

        public Color DeathFlashColor       => _deathFlashColor;
        public bool  PlayDeathClip         => _playDeathClip;
        public float DeathHologramDuration => _deathHologramDuration;
        public Color HologramColor         => _hologramColor;
        public float HologramBodyAlpha     => _hologramBodyAlpha;
        public float HologramScanScale     => _hologramScanScale;
        public float HologramScanAmount    => _hologramScanAmount;

        public VfxData VfxHitSpark    => _vfxHitSpark;
        public VfxData VfxMuzzle      => _vfxMuzzle;
        public VfxData VfxImpactDust  => _vfxImpactDust;
        public VfxData VfxContactDust => _vfxContactDust;
        public VfxData VfxHeal        => _vfxHeal;

        public VfxData VfxCastBurst   => _vfxCastBurst;

        /// <summary>Множитель scale hit-spark по доле HP-урона от MaxHP (HeavyHitFrac = полная сила).</summary>
        public float EvaluateHitVfxIntensity(float hpDamageFrac)
        {
            float t = Mathf.Clamp01(hpDamageFrac / Mathf.Max(1e-4f, _heavyHitFrac));
            return Mathf.Lerp(_vfxHitIntensityMin, _vfxHitIntensityMax, t);
        }

        /// <summary>Множитель КОЛИЧЕСТВА искр по доле HP-урона: чем тяжелее удар, тем гуще осколки.</summary>
        public float EvaluateHitVfxCount(float hpDamageFrac)
        {
            float t = Mathf.Clamp01(hpDamageFrac / Mathf.Max(1e-4f, _heavyHitFrac));
            return Mathf.Lerp(_vfxHitCountMin, _vfxHitCountMax, t);
        }

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

        /// <summary>
        /// Цвет hit-flash: сродство перекрывает школу; при выключенном School Flash — <see cref="FlashColor"/>.
        /// </summary>
        public Color ResolveHitFlashColor(DamageSchool school, DamageAffinity affinity)
        {
            if (!_enableSchoolFlash) return _flashColor;
            if (affinity != DamageAffinity.None)
            {
                return affinity switch
                {
                    DamageAffinity.Poison => _flashPoison,
                    DamageAffinity.Light  => _flashLight,
                    DamageAffinity.Dark   => _flashDark,
                    _                     => _flashColor,
                };
            }

            return school switch
            {
                DamageSchool.Magical => _flashElemental,
                DamageSchool.True    => _flashTrue,
                _                    => _flashPhysical,
            };
        }
    }
}
