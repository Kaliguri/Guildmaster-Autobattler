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
        [Header("Палитра")]
        [Tooltip("Снимок токенов дизайн-системы: отсюда берутся ВСЕ цвета фидбэка (--gm-color-combat-*). " +
                 "Своих Color-полей у конфига нет — он называет роли, как MapStyle и CombatColorPalette. " +
                 "Пересобрать — Alebardium → Дизайн-система → Пересобрать палитру.")]
        [SerializeField] private GuildmasterPalette _palette;

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
        [Tooltip("Свечение части-источника на касте (реф SAO): оружие/конечность наливается светом. " +
                 "Выключено — телеграф каста остаётся на контуре силуэта.")]
        [SerializeField] private bool _enableCastGlow = true;

        [Tooltip("Свечение ЩИТА в момент, когда он поглотил удар: та же вспышка света, что у каста, " +
                 "но цветом защиты. Нет щита в руках — ничего не светится.")]
        [SerializeField] private bool _enableBlockGlow = true;

        // Эти пять существовали БЕЗ тумблера: выключить их можно было только правкой кода, а «список
        // переключателей» без них отвечал на вопрос «всё ли включено» неправдой (заказ Макса 30.07).
        // Перепись входов держит FeelToggleCoverageTests — новый эффект без тумблера уронит тест.
        [Tooltip("Вспышка тела при получении урона. Основа читаемости удара — выключать только для замеров.")]
        [SerializeField] private bool _enableHitFlash = true;
        [Tooltip("Сплющивание тела в момент попадания (поверх вспышки).")]
        [SerializeField] private bool _enableHitSquash = true;
        [Tooltip("Подсветка тела на телеграфе: подводка к тому, что ЕЩЁ не случилось (щит «Оплота»).")]
        [SerializeField] private bool _enableTelegraphFlash = true;
        [Tooltip("Поза гвардии: щит поднимается ДО появления барьера, слоем поверх бега или свинга.")]
        [SerializeField] private bool _enableGuardPose = true;
        [Tooltip("Разлёт тела на осколки в конце смерти. Выключено — тело просто исчезает.")]
        [SerializeField] private bool _enableDeathShatter = true;

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

        // Цвета вспышек по школе/сродству здесь БОЛЬШЕ НЕ ХРАНЯТСЯ: они живут в палитре проекта
        // (--gm-color-combat-flash-*), а конфиг только называет роли. До 30.07.2026 те же шесть цветов
        // лежали и здесь, и в токенах — совпадали ровно потому, что их никто не правил.

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
        // Цвет — роль --gm-color-combat-heal из палитры.
        [Tooltip("Сила вспышки лечения: заметно слабее удара — лечение греет, а не бьёт.")]
        [SerializeField, Range(0.1f, 1f)] private float _healFlashPeak = 0.5f;

        [Header("Micro Feel — cast outline")]
        [Tooltip("Длительность контура на теле при касте, сек. 0 = контура нет.")]
        [SerializeField, Range(0f, 1.5f)] private float _castOutlineDuration = 0.45f;
        [Tooltip("Плотность контура на пике.")]
        [SerializeField, Range(0f, 1f)] private float _castOutlineStrength = 0.9f;

        [Header("Micro Feel — cast glow (свечение части-источника, реф SAO)")]
        [Tooltip("Сила свечения на пике (умножается на _GlowAmount шейдера тела).")]
        [SerializeField, Range(0f, 1f)] private float _castGlowStrength = 1f;
        [Tooltip("HDR-множитель цвета юнита под bloom. Порог bloom = 1.0, поэтому LDR-цвет юнита свечения не " +
                 "даёт — ровно как у осколков (×2.75) и вспышки смерти (×1.8). Ниже ~1.5 не опускать: не пробьёт.")]
        [SerializeField, Range(1f, 5f)] private float _castGlowBloomIntensity = 2.5f;
        [Tooltip("Форма нарастания свечения за время каста (0→1 нормализованное время каста → 0→1 сила). " +
                 "Пик к концу — свет копится к выпуску приёма.")]
        [SerializeField] private AnimationCurve _castGlowChargeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("За сколько секунд свечение гаснет после выпуска приёма (спад с пика в ноль).")]
        [SerializeField, Range(0.02f, 0.6f)] private float _castGlowRelease = 0.18f;
        [Tooltip("Короткий подъём для МГНОВЕННОГО свечения (пассив без каста, пульс оружия у длительного " +
                 "баффа): всполох вместо наливающегося заряда.")]
        [SerializeField, Range(0.01f, 0.3f)] private float _castGlowPulseRise = 0.06f;
        [Tooltip("Сколько свечения ложится РОВНО, не считаясь с артом (_GlowShapeKeep шейдера). 1 = плоская " +
                 "заливка: на пике клинок теряет форму и читается силуэтом. 0 = свет строго по яркости " +
                 "пикселя: форма и грани видны, но тёмный арт светится слабо.")]
        [SerializeField, Range(0f, 1f)] private float _castGlowFlatness = 0.35f;

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
        // Фолбэк-цвет (когда School Flash выключен) — роль --gm-color-combat-flash-neutral.
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
        [Tooltip("Размер осколка в ДОЛЯХ РОСТА юнита (0.1 = кусок в десятую часть тела). Мера длины, а не " +
                 "пикселей исходника: части скелетного юнита нарисованы в разном разрешении, и общий " +
                 "«чанк в N пикселей» дробил их на куски несопоставимого размера.")]
        [SerializeField, Range(0.02f, 0.5f)] private float _shatterShardSize = 0.12f;
        [Tooltip("Сила ПСЕВДО-3D кувыркания осколков (переворот вокруг случайной оси). 0 = плоско, больше = активнее кувыркаются.")]
        [SerializeField] private float _shatterTumble = 9f;
        [Tooltip("Восходящий дрейф: смещает разлёт вверх-и-наружу (0 = строго радиально, больше = осколки уходят вверх).")]
        [SerializeField] private float _shatterUpBias = 0.6f;
        [Tooltip("За сколько секунд пересвет спадает и осколки становятся видны как угольки.")]
        [SerializeField] private float _shatterFlashOut = 0.12f;
        [Tooltip("Во сколько раз «около-белые» осколки ярче базы (роль --gm-color-combat-overbright). " +
                 "Выше 1 — потому что порог bloom стоит на 1.0 и ровно белый не светится. Остальные " +
                 "осколки красятся оттенком ПАВШЕГО юнита.")]
        [SerializeField] private float _shardWhiteBrightness = 2.75f;
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
        [Tooltip("Яркость пересвета перед расколом (та же роль --gm-color-combat-overbright, что у осколков, " +
                 "но своя сила). Отдельно от вспышки удара: hit-flash обязан оставаться в пределах экрана, " +
                 "а смерть должна ПРОБИВАТЬ порог bloom — иначе «яркий белый» просто белый.")]
        [SerializeField] private float _deathFlashBrightness = 1.8f;

        [Tooltip("Играть клип смерти (падение тела) перед голограммой. Выкл = юнит развоплощается стоя, " +
                 "на том кадре, где его достали.")]
        [SerializeField] private bool _playDeathClip;

        [Header("Death — стадия голограммы (перед вспышкой)")]
        [Tooltip("Длительность голограммы, сек. 0 = стадия выключена (сразу белая вспышка).")]
        [SerializeField] private float _deathHologramDuration = 0.3f;
        // Цвет тела в голограмме — роль --gm-color-combat-hologram (цифровой циан развоплощения).
        [Tooltip("Прозрачность тела в голограмме (контур остаётся плотным).")]
        [SerializeField, Range(0.05f, 1f)] private float _hologramBodyAlpha = 0.45f;
        [Tooltip("Шаг скан-линий в ПИКСЕЛЯХ спрайта.")]
        [SerializeField, Range(1f, 12f)] private float _hologramScanScale = 3f;
        [Tooltip("Глубина скан-линий (0 = ровная заливка без полос).")]
        [SerializeField, Range(0f, 1f)] private float _hologramScanAmount = 0.35f;

        // --- Slowmo — добивающий удар (CombatFeelDirector) ---
        [Header("Slowmo — добивающий удар (kill)")]
        [Tooltip("За сколько секунд ДО смертельного удара начинать замедление. Работает благодаря лаге " +
                 "показа (лента боя): «раньше» неоткуда взять, если о смерти узнаёшь в тот же кадр. " +
                 "0 = замедление щёлкает в момент удара, как было до ленты.")]
        [SerializeField, Range(0f, 1f)] private float _killSlowLeadSeconds = 0.35f;
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

        [Header("VFX — hit-spark по весу удара (МНОЖИТЕЛИ к VfxData.SizeUnits)")]
        [Tooltip("Множитель РАЗМЕРА искр на лёгком ударе. Ниже 0.5 не опускать: базовый размер задан так, " +
                 "чтобы искра читалась, и втрое урезанная перестаёт быть видимой в боевом кадре.")]
        [SerializeField, Range(0.3f, 1f)] private float _vfxHitSizeMultMin = 0.7f;
        [Tooltip("Множитель РАЗМЕРА искр на тяжёлом ударе (доля ≥ HeavyHitFrac).")]
        [SerializeField, Range(0.3f, 2f)] private float _vfxHitSizeMultMax = 1f;
        [Tooltip("Сколько искр даёт удар, не снявший ни одного HP: нижняя граница формулы количества.")]
        [SerializeField, Range(0f, 40f)] private int _hitSparkBase = 6;

        [Tooltip("Сколько искр добавляет ПОЛНАЯ доля HP: количество идёт пропорцией урону. " +
                 "Канон: 6 + доля_HP × 120.")]
        [SerializeField, Range(10f, 300f)] private int _hitSparkPerHp = 120;

        [Tooltip("ПОТОЛОК числа искр на удар. Обязателен: без него удар кита превратится в стену.")]
        [SerializeField, Range(8f, 200f)] private int _hitSparkCap = 48;

        [Tooltip("Сколько искр в бёрстах ПРЕФАБА — знаменатель, которым абсолютное количество переводится " +
                 "в множитель. Врёт это число — врёт вся формула количества, поэтому оно живёт рядом с ней, " +
                 "а не выводится из префаба на лету.")]
        [SerializeField, Range(1f, 200f)] private int _hitSparkPrefabCount = 24;

        [Tooltip("Всплеск при касте способности за ману — «смотри, я сейчас выдам». Цвет берётся из палитры " +
                 "самого юнита (UnitData), поэтому один префаб служит всем.")]
        [SerializeField] private VfxData _vfxCastBurst;

        // --- Форма удара: главный знак попадания (серп / веретено / звезда / линия-всполох) ---
        [Header("VFX — форма удара")]
        [Tooltip("Форма попадания: серп режущего, веретено колющего, звезда дробящего, линия-всполох выстрела. " +
                 "Выключено — удар остаётся вспышкой, искрами и цифрой, как до 01.08.2026.")]
        [SerializeField] private bool _enableHitForm = true;

        [Tooltip("Заблокированный удар в тело не вошёл: форма ТОРМОЗИТ о щит вместо прохода насквозь. " +
                 "Выключено — форма всегда проходит навылет. Оба поведения сравниваются в бою.")]
        [SerializeField] private bool _enableHitFormBreakOnShield = true;

        [Tooltip("Префаб формы (quad с шейдером Guildmaster/Vfx/HitForm).")]
        [SerializeField] private VfxData _vfxHitForm;

        [Tooltip("Рост юнита-человека (H) в мировых единицах — мера, в которой заданы все размеры формы. " +
                 "Сменится сетка спрайтов — поменяется одно это число, а не четыре архетипа.")]
        [SerializeField] private float _hitFormUnitHeight = 1f;

        [Tooltip("Сколько живёт форма, сек. Канон: 4–5 кадров на 30 Гц, целиком ПОСЛЕ момента контакта.")]
        [SerializeField] private float _hitFormLife = 0.16f;

        [Tooltip("За какую долю жизни ГОЛОВА росчерка проходит форму насквозь. Меньше — резче прочерк.")]
        [SerializeField, Range(0.05f, 1f)] private float _hitFormGrowShare = 0.3f;

        [Tooltip("На сколько (в долях жизни) ХВОСТ росчерка отстаёт от головы. Он и гасит форму, догоняя " +
                 "её: удар выглядит движением «оттуда сюда», а не полосой, которая вспыхнула и растаяла. " +
                 "Больше отставание — длиннее видимый росчерк.")]
        [SerializeField, Range(0.05f, 1f)] private float _hitFormTailLag = 0.4f;

        [Tooltip("Какую долю толщины занимает белый пересвет ядра.")]
        [SerializeField, Range(0f, 1f)] private float _hitFormCoreWidth = 0.55f;

        [Tooltip("Яркость пересвета в ядре формы. Ядро — самое светлое место удара и потому пробивает " +
                 "bloom сильнее каймы; красить его элементом нельзя, иначе оба цвета потухнут.")]
        [SerializeField, Range(1f, 6f)] private float _hitFormCoreBrightness = 3.2f;

        [Tooltip("Размер формы на ТЯЖЁЛОМ ударе (доля ≥ Heavy Hit Frac) — множитель к числам архетипа. " +
                 "Потолок обязателен: без него удар кита выдал бы стену вместо эффекта.")]
        [SerializeField, Range(1f, 3f)] private float _hitFormSizeMax = 1.35f;

        [Tooltip("Размер формы на самом лёгком ударе — множитель к числам архетипа.")]
        [SerializeField, Range(0.2f, 1f)] private float _hitFormSizeMin = 0.55f;

        [Tooltip("Тёмная ОБВОДКА снаружи формы. Не путать с каймой: та живёт внутри формы и несёт цвет " +
                 "элемента, а обводка цвета не имеет вовсе — она перекрывает кадр чёрным. Выключено — " +
                 "форма остаётся бесконтурным свечением, как до 05.08.2026.")]
        [SerializeField] private bool _enableHitFormLine = true;

        [Tooltip("Ширина обводки в долях H. Едет вместе с размером удара. Заметно больше, чем кажется " +
                 "нужным на статичном превью: bloom растекается с ядра и подъедает тонкий контур.")]
        [SerializeField, Range(0f, 0.06f)] private float _hitFormLineWidthH = 0.018f;

        // --- Порезы: тело помнит бой ---
        [Header("VFX — порезы на теле")]
        [Tooltip("Каждое попадание оставляет светящуюся красную прореху; хил заживляет самые старые. " +
                 "Выключено — тело о ранах не помнит, здоровье читается только полосой.")]
        [SerializeField] private bool _enableBodyCuts = true;

        [Tooltip("Полуширина линии пореза в мировых единицах. Порез гладкий, не пиксельный: тонкий " +
                 "светящийся штрих читается там, где царапина в один пиксель потерялась бы.")]
        [SerializeField] private float _cutWidthUnits = 0.012f;

        [Tooltip("Длина пореза на самом лёгком ударе, доли H.")]
        [SerializeField] private float _cutLengthMinH = 0.09f;

        [Tooltip("Длина пореза на тяжёлом ударе (доля ≥ Heavy Hit Frac), доли H.")]
        [SerializeField] private float _cutLengthMaxH = 0.2f;

        [Tooltip("Яркость свечения пореза: во сколько раз цвет из палитры ярче базы. Порез светится " +
                 "слабее удара — он состояние, а не событие, и звенеть ему не положено.")]
        [SerializeField, Range(1f, 4f)] private float _cutBrightness = 1.6f;

        [Header("VFX — дуга за клинком (первая стадия удара)")]
        [Tooltip("Сектор от плеча, заметающий пройденный угол. Живёт на взмахе и рисуется ВСЕГДА, даже " +
                 "на промахе: дуга говорит «клинок прошёл здесь», и это правда в любом исходе. " +
                 "Выключено — взмах остаётся немым, форма на попадании работает.")]
        [SerializeField] private bool _enableSwingArc = true;

        [Tooltip("Префаб дуги (quad с шейдером Guildmaster/Vfx/SwingArc).")]
        [SerializeField] private VfxData _vfxSwingArc;

        [Tooltip("С какой доли радиуса начинается свечение: у самого плеча его нет — там рука, а не след.")]
        [SerializeField, Range(0f, 0.9f)] private float _swingArcInnerShare = 0.4f;

        [Tooltip("Насколько быстро гаснет хвост дуги. Больше — короче видимый след за клинком.")]
        [SerializeField, Range(0.2f, 4f)] private float _swingArcTailBias = 1.6f;

        [Tooltip("Сколько дуга догорает после конца взмаха, сек. Канон: около двух кадров.")]
        [SerializeField] private float _swingArcFadeOut = 0.07f;

        [Tooltip("Яркость дуги относительно свечения каста. 1 = как свет каста; ниже — дуга тусклее. " +
                 "Своя ручка, потому что дуга идёт на КАЖДЫЙ взмах: то, что читается один раз за каст, " +
                 "в непрерывной серии ударов пересвечивает бой.")]
        [SerializeField, Range(0.05f, 1f)] private float _swingArcBrightness = 0.35f;

        [Tooltip("Длина следа в ГРАДУСАХ: сколько последнего пути клинка видно. Начало дуги едет за " +
                 "клинком, поэтому непрерывный взмах (поток «Вихря») рисует один тянущийся след, а не " +
                 "замкнутый круг. Больше 360 замкнёт кольцо — сектор перекроет сам себя.")]
        [SerializeField, Range(45f, 360f)] private float _swingArcMaxSpanDeg = 270f;

        [SerializeField] private HitFormArchetypeConfig _hitFormSlash = HitFormArchetypeConfig.Slash();
        [SerializeField] private HitFormArchetypeConfig _hitFormPierce = HitFormArchetypeConfig.Pierce();
        [SerializeField] private HitFormArchetypeConfig _hitFormBlunt = HitFormArchetypeConfig.Blunt();
        [SerializeField] private HitFormArchetypeConfig _hitFormBolt = HitFormArchetypeConfig.Bolt();

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
        public bool  EnableCastGlow          => _enableCastGlow;
        public bool  EnableBlockGlow         => _enableBlockGlow;
        public bool  EnableHitFlash          => _enableHitFlash;
        public bool  EnableHitSquash         => _enableHitSquash;
        public bool  EnableTelegraphFlash    => _enableTelegraphFlash;
        public bool  EnableGuardPose         => _enableGuardPose;
        public bool  EnableDeathShatter      => _enableDeathShatter;

        public Color HealFlashColor      => Role("--gm-color-combat-heal");
        public float HealFlashPeak       => _healFlashPeak;
        public float CastOutlineDuration => _castOutlineDuration;
        public float CastOutlineStrength => _castOutlineStrength;
        public float CastGlowStrength       => _castGlowStrength;
        public float CastGlowBloomIntensity => _castGlowBloomIntensity;
        public AnimationCurve CastGlowChargeCurve => _castGlowChargeCurve;
        public float CastGlowRelease     => _castGlowRelease;
        public float CastGlowPulseRise   => _castGlowPulseRise;
        public float CastGlowFlatness    => _castGlowFlatness;
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

        public Color FlashColor       => Role("--gm-color-combat-flash-neutral");
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

        /// <summary>
        /// Масштаб боевой цифры по доле HP-урона от MaxHP цели: 1 на царапине, <c>NumberMaxScale</c> на
        /// ударе в <c>NumberFullFrac</c> и тяжелее. Кривая КОРНЕВАЯ, а не прямая: в бою почти все удары
        /// лежат в нижней трети шкалы, и на прямой они выглядели бы одинаково мелкими — весь запас размера
        /// доставался бы редкому киту. Корень отдаёт половину роста уже на четверти порога, так что разница
        /// «поцарапал / врезал» читается там, где она реально происходит.
        /// </summary>
        public float EvaluateNumberScale(float hpDamageFrac)
        {
            float t = Mathf.Clamp01(hpDamageFrac / Mathf.Max(1e-4f, _numberFullFrac));
            return Mathf.Lerp(1f, _numberMaxScale, Mathf.Sqrt(t));
        }

        /// <summary>Доля HP-урона, на которой цифра достигает максимума — точка насыщения кривой выше.</summary>
        public float NumberFullFrac => _numberFullFrac;

        public float ShatterFlashIn    => _shatterFlashIn;
        public float ShatterDuration   => _shatterDuration;
        public float ShatterExplode    => _shatterExplode;
        public float ShatterGravity    => _shatterGravity;
        public float ShatterSpin       => _shatterSpin;
        public float ShatterSpread     => _shatterSpread;
        public float ShatterShardSize  => _shatterShardSize;
        public float ShatterTumble     => _shatterTumble;
        public float ShatterUpBias     => _shatterUpBias;
        public float ShatterFlashOut   => _shatterFlashOut;
        public Color ShardWhiteColor   => Overbright(_shardWhiteBrightness);
        public float ShardWhiteShare   => _shardWhiteShare;
        public float ShatterFadePower  => _shatterFadePower;
        public float ShatterLifeVariance => _shatterLifeVariance;
        public float ShatterGlow       => _shatterGlow;
        public float ShatterEmberBoost => _shatterEmberBoost;
        public float ShatterLuma       => _shatterLuma;
        public float ShatterHold       => _shatterHold;
        public float ShatterMinTimeScale => _shatterMinTimeScale;

        public Color DeathFlashColor       => Overbright(_deathFlashBrightness);
        public bool  PlayDeathClip         => _playDeathClip;
        public float DeathHologramDuration => _deathHologramDuration;
        public Color HologramColor         => Role("--gm-color-combat-hologram");
        public float HologramBodyAlpha     => _hologramBodyAlpha;
        public float HologramScanScale     => _hologramScanScale;
        public float HologramScanAmount    => _hologramScanAmount;

        public VfxData VfxHitSpark    => _vfxHitSpark;
        public VfxData VfxMuzzle      => _vfxMuzzle;
        public VfxData VfxImpactDust  => _vfxImpactDust;
        public VfxData VfxContactDust => _vfxContactDust;
        public VfxData VfxHeal        => _vfxHeal;

        public VfxData VfxCastBurst   => _vfxCastBurst;

        public bool    EnableHitForm              => _enableHitForm;
        public bool    EnableHitFormBreakOnShield => _enableHitFormBreakOnShield;
        public bool    EnableHitFormLine          => _enableHitFormLine;
        public float   HitFormLineWidthH          => _hitFormLineWidthH;
        public VfxData VfxHitForm                 => _vfxHitForm;
        public float   HitFormUnitHeight          => _hitFormUnitHeight;
        public float   HitFormLife                => _hitFormLife;
        public float   HitFormGrowShare           => _hitFormGrowShare;
        public float   HitFormTailLag             => _hitFormTailLag;
        public float   HitFormCoreWidth           => _hitFormCoreWidth;

        /// <summary>Цвет пересвета в ядре формы — тот же холодный пересвет, что у осколков и смерти.</summary>
        public Color   HitFormCoreColor           => Overbright(_hitFormCoreBrightness);

        public bool  EnableBodyCuts => _enableBodyCuts;
        public float CutWidthUnits  => _cutWidthUnits;

        /// <summary>
        /// Цвет пореза: роль вскрытого из палитры, поднятая множителем яркости. HDR живёт здесь, а не в
        /// токенах — там оттенок, тут сила свечения.
        /// </summary>
        public Color CutColor
        {
            get
            {
                Color basis = Role("--gm-color-combat-cut");
                return new Color(basis.r * _cutBrightness, basis.g * _cutBrightness, basis.b * _cutBrightness, basis.a);
            }
        }

        /// <summary>Длина пореза в мировых единицах по весу удара: тяжёлый вскрывает шире.</summary>
        public float EvaluateCutLength(float hpDamageFrac)
        {
            float t = Mathf.Clamp01(hpDamageFrac / Mathf.Max(1e-4f, _heavyHitFrac));
            return Mathf.Lerp(_cutLengthMinH, _cutLengthMaxH, t) * Mathf.Max(0.01f, _hitFormUnitHeight);
        }

        public bool    EnableSwingArc      => _enableSwingArc;
        public VfxData VfxSwingArc         => _vfxSwingArc;
        public float   SwingArcInnerShare  => _swingArcInnerShare;
        public float   SwingArcTailBias    => _swingArcTailBias;
        public float   SwingArcFadeOut     => _swingArcFadeOut;
        public float   SwingArcBrightness  => _swingArcBrightness;
        public float   SwingArcMaxSpanDeg  => _swingArcMaxSpanDeg;

        /// <summary>
        /// Правило генерации архетипа. Тотальна по построению: новый архетип, забытый здесь, вернёт
        /// режущего — но добавить его молча не выйдет, потому что <c>HitFormKind</c> закрыт каноном
        /// («форма говорит, КАК доставили»), и пятый архетип потребует решения Макса, а не строки в switch.
        /// </summary>
        public HitFormArchetypeConfig HitFormArchetype(Effects.HitFormKind kind) => kind switch
        {
            Effects.HitFormKind.Pierce => _hitFormPierce,
            Effects.HitFormKind.Blunt  => _hitFormBlunt,
            Effects.HitFormKind.Bolt   => _hitFormBolt,
            _                          => _hitFormSlash,
        };

        /// <summary>
        /// Множитель РАЗМЕРА формы по доле снятого HP. Непрерывная зависимость с потолком: категорий
        /// «лёгкий» и «тяжёлый» у формы нет, но и стены на удар кита быть не должно.
        /// </summary>
        public float EvaluateHitFormSize(float hpDamageFrac)
        {
            float t = Mathf.Clamp01(hpDamageFrac / Mathf.Max(1e-4f, _heavyHitFrac));
            return Mathf.Lerp(_hitFormSizeMin, _hitFormSizeMax, t);
        }

        /// <summary>
        /// Множитель РАЗМЕРА hit-spark по доле HP-урона от MaxHP (HeavyHitFrac = полная сила).
        /// Единственный рантайм-множитель размера эффектов; база — <c>VfxData.SizeUnits</c>.
        /// </summary>
        public float EvaluateHitVfxSizeMultiplier(float hpDamageFrac)
        {
            float t = Mathf.Clamp01(hpDamageFrac / Mathf.Max(1e-4f, _heavyHitFrac));
            return Mathf.Lerp(_vfxHitSizeMultMin, _vfxHitSizeMultMax, t);
        }

        /// <summary>
        /// Сколько искр даёт удар: <c>база + доля_HP × шаг</c>, но не больше потолка.
        /// </summary>
        /// <remarks>
        /// Щедрость здесь осознанная (Макс, 31.07.2026): искры мелкие и живут доли секунды, поэтому их
        /// должно быть много. Потолок при этом обязателен — без него удар кита на десять тысяч выдал бы
        /// стену вместо эффекта.
        /// </remarks>
        public int EvaluateHitSparkCount(float hpDamageFrac)
        {
            float raw = _hitSparkBase + Mathf.Clamp01(hpDamageFrac) * _hitSparkPerHp;
            return Mathf.Clamp(Mathf.RoundToInt(raw), 0, _hitSparkCap);
        }

        /// <summary>
        /// Множитель бёрстов префаба, дающий нужное АБСОЛЮТНОЕ количество искр. Пропорцию между потоками
        /// (быстрые к медленным) держит префаб, абсолют приходит отсюда — ровно как с размером.
        /// </summary>
        public float EvaluateHitVfxCount(float hpDamageFrac) =>
            EvaluateHitSparkCount(hpDamageFrac) / (float)Mathf.Max(1, _hitSparkPrefabCount);

        public float KillSlowLeadSeconds => _killSlowLeadSeconds;
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
        /// Цвет hit-flash по типу урона: у типов со своим «вкусом» (яд, свет, тьма) он свой, у остальных —
        /// цвет их школы. При выключенном School Flash — <see cref="FlashColor"/>. Здесь выбирается РОЛЬ,
        /// значение приходит из палитры проекта.
        /// </summary>
        /// <remarks>
        /// Оба яда светят одинаково: игрок читает «отравили», а не «отравили физически». Кровотечение
        /// пока идёт цветом физической школы — своей роли в палитре у него нет, и заводить её должен
        /// Макс, а не молчаливый фолбэк.
        /// </remarks>
        public Color ResolveHitFlashColor(DamageType type)
        {
            if (!_enableSchoolFlash) return FlashColor;

            switch (type)
            {
                case DamageType.PoisonPhysical:
                case DamageType.PoisonMagical:
                    return Role("--gm-color-combat-flash-poison");
                case DamageType.Light:
                    return Role("--gm-color-combat-flash-light");
                case DamageType.Dark:
                    return Role("--gm-color-combat-flash-dark");
            }

            return DamageTypes.SchoolOf(type) switch
            {
                DamageSchool.Magical => Role("--gm-color-combat-flash-magical"),
                DamageSchool.True    => Role("--gm-color-combat-flash-true"),
                _                    => Role("--gm-color-combat-flash-physical"),
            };
        }

        /// <summary>
        /// Цвет роли из палитры проекта. Пустая ссылка или неизвестное имя — баг разводки, а не повод
        /// подставить «похожий» цвет: фидбэк говорит игроку, ЧТО именно произошло, и неверный цвет здесь
        /// врёт про школу урона. Поэтому — пурпур и красная строка, как в остальной презентации.
        /// </summary>
        private Color Role(string token)
        {
            if (_palette == null)
            {
                Debug.LogError($"[CombatFeelConfig] палитра не назначена, цвет '{token}' взять неоткуда " +
                               $"(ассет {name}).");
                return Color.magenta;
            }

            if (_palette.TryGet(token, out Color color)) return color;

            Debug.LogError($"[CombatFeelConfig] в палитре нет роли '{token}'. Пересобери снимок: " +
                           "Alebardium → Дизайн-система → Пересобрать палитру.");
            return Color.magenta;
        }

        // Холодный пересвет: ОДИН оттенок из палитры, поднятый до нужной силы. Две яркости (осколки и
        // вспышка смерти) — это два разных события, а не два цвета, поэтому в палитре они не двоятся.
        // Альфа не множится: её читают шейдеры как прозрачность, а не как свет.
        private Color Overbright(float brightness)
        {
            Color basis = Role("--gm-color-combat-overbright");
            return new Color(basis.r * brightness, basis.g * brightness, basis.b * brightness, basis.a);
        }
    }
}
