namespace Guildmaster.Core.Simulation
{
    /// <summary>
    /// Иммутабельный снапшот балансных тюнинг-ручек симуляции, снятый со <c>SimTuningConfig</c> на старте
    /// боя (вики «13» §3.4, §4). Из тика читается ТОЛЬКО этот снапшот — не SO (детерминизм: правка SO в
    /// play mode применяется к идущему бою лишь явным re-bake, помечающим бой tainted).
    /// <para>
    /// <see cref="Default"/> — единый код-сид этих значений: сюда же смотрят свежесозданный
    /// <c>SimTuningConfig</c>, dev-гизмо презентации и тесты-страховки неизменности баланса.
    /// </para>
    /// <para>
    /// НЕ здесь (осознанные отклонения от §3.4, шов готов принять их позже): <c>TickRate</c>/
    /// <c>AiTickRate</c> — структурные (детерминизм, назначение BrainPhase; см. <see cref="SimConstants"/>);
    /// <c>MaxAttackAnimTicks</c>/<c>MinWindupTicks</c> — в чистом статик-хелпере AttackTiming (широкие
    /// юнит-тесты); <c>DefaultPassiveThresholdPct</c> — compile-time дефолт сериализованного поля AIProfile.
    /// </para>
    /// </summary>
    public readonly struct SimTuning
    {
        // --- Разделение тел (SeparationSystem) ---
        public readonly float BodyRadiusPerSize;
        public readonly float SeparationStrength;
        public readonly int   SeparationIterations;
        public readonly float SeparationSameTeamScale;

        // --- Снаряды ---
        public readonly float ProjectileHitRadiusFactor;
        public readonly float ProjectileDespawnMargin;

        // --- AI / поиск целей ---
        public readonly float KiteFleeFactor;      // fallback-полоса кайта: flee = range × это
        public readonly float GlobalSearchRadius;  // «без ограничения дальности» на масштабе арены

        // --- Побег (FleeSteering): единая математика Retreat/Kite-flee ---
        // Направление побега = взвешенная сумма: отталкивание от центроида врагов (Threat) + притяжение
        // к своему тылу (Home, по Team) + превентивное избегание стен (Wall). Веса безразмерные (складываются
        // до нормализации). Home < Threat — тыл лишь подкручивает, не разворачивает от реальной угрозы.
        public readonly float FleeThreatWeight;    // вес отталкивания от врагов
        public readonly float FleeHomeWeight;      // вес притяжения к своему тылу
        public readonly float FleeWallWeight;      // вес отталкивания от стены (в пределах FleeWallMargin)
        public readonly float FleeWallMargin;      // дистанция до стены (м), с которой включается избегание
        public readonly float FleeThreatRadius;    // радиус (м) сбора врагов в центроид угрозы (иначе — ближайший)
        public readonly float KiteStrafeWeight;    // вес бокового ухода кайтера (дуга вместо пятящегося отхода)

        // --- Смещение (DisplacementSystem, ГДД «Смещение») ---
        // Скорость полёта фиксирована, поэтому ДЛИТЕЛЬНОСТЬ полёта считается из дистанции: дальний
        // толчок держит цель в контроль-иммунном оглушении дольше (решение 2026-07-28). Отдельного
        // параметра «сколько тиков лететь» намеренно нет — иначе у одного свойства два владельца.
        public readonly float DisplaceSpeedPerSecond;  // ДЕФОЛТНАЯ скорость полёта, мировых единиц в секунду
                                                       // (источник может задать свою в DisplaceRequest)
        public readonly float CannonballWidthMult;     // во сколько раз коридор «ядра» шире заданной ширины
        public readonly float WallImpactDamageMult;    // доля урона толчка, добиваемая при удар о край арены
        public readonly float WallImpactStunSeconds;   // сколько цель лежит после удара о край арены

        // --- Овертайм (правило анти-затягивания, ГДД «Боевая система») ---
        // Растёт ТОЛЬКО наносимый урон. Лечение, щиты и реген не трогаем намеренно: клинч «танк+хил
        // против танк+хил» ломается ровно тем, что урон уезжает вверх, а сустейн остаётся плоским.
        // Замер 2026-07-28: медиана боя 20-29 с, так что до порога доживает только настоящий клинч —
        // это предохранитель для хвоста, а не механика на каждый бой.
        public readonly float OvertimeStartSeconds;   // с какой секунды боя включается рампа
        public readonly float OvertimeDamagePerSecond; // прибавка к урону за каждую секунду сверх порога (0.05 = +5%)

        // --- Спринт (рывок на дальнем подходе) ---
        // Ускорение живёт В СИМУЛЯЦИИ, а не в презентации: иначе бегущая анимация обгонит позицию, и
        // юнит поедет ногами по воздуху. Порог — ЗАЗОР сверх досягаемости, а не сырая дистанция до цели:
        // у стрелка с досягаемостью 8 сырой порог «дальше трёх метров» держал бы спринт включённым всегда,
        // хотя он уже на позиции. Гистерезис (вход шире выхода) — против мигания на коротких перебежках.
        // Разгон намеренно НЕ мгновенный: юнит сперва идёт обычным шагом и только потом переходит на бег.
        // Прибавка, включающаяся щелчком, читается как телепорт скорости — и вместе с ней щёлкает клип.
        public readonly float SprintSpeedMult;   // множитель скорости в спринте (1.3 = +30%)
        public readonly float SprintEnterGap;    // зазор сверх досягаемости, с которого начинается разбег
        public readonly float SprintExitGap;     // зазор, на котором разбег заканчивается (< enter)
        public readonly float SprintWalkSeconds; // сколько идёт обычным шагом, прежде чем начать разгон
        public readonly float SprintRampSeconds; // за сколько разгон набирает полную прибавку

        // --- Рекаст (атака вне очереди) ---
        // Рекаст УСКОРЯЕТ фазы, а не снимает их (решение Макса 2026-07-31): снятая фаза убирает окно, в
        // которое противник успевает ответить, — ускоренная лишь сокращает его. Скорости две, хотя пока
        // равны: доигрыш и замах подкручиваются под разное («отпустил оружие» против читаемости телеграфа),
        // и одна ручка на двоих значила бы, что настроить можно только оба сразу.
        public readonly float RecastRecoverySpeed;  // во сколько раз быстрее доигрыш ОБОРВАННОЙ атаки
        public readonly float RecastWindupSpeed;    // во сколько раз быстрее замах удара, вышедшего по рекасту

        public SimTuning(
            float bodyRadiusPerSize,
            float separationStrength,
            int   separationIterations,
            float separationSameTeamScale,
            float projectileHitRadiusFactor,
            float projectileDespawnMargin,
            float kiteFleeFactor,
            float globalSearchRadius,
            float fleeThreatWeight,
            float fleeHomeWeight,
            float fleeWallWeight,
            float fleeWallMargin,
            float fleeThreatRadius,
            float kiteStrafeWeight,
            float displaceSpeedPerSecond,
            float cannonballWidthMult,
            float wallImpactDamageMult,
            float wallImpactStunSeconds,
            float overtimeStartSeconds,
            float overtimeDamagePerSecond,
            float sprintSpeedMult,
            float sprintEnterGap,
            float sprintExitGap,
            float sprintWalkSeconds,
            float sprintRampSeconds,
            float recastRecoverySpeed,
            float recastWindupSpeed)
        {
            BodyRadiusPerSize         = bodyRadiusPerSize;
            SeparationStrength        = separationStrength;
            SeparationIterations      = separationIterations;
            SeparationSameTeamScale   = separationSameTeamScale;
            ProjectileHitRadiusFactor = projectileHitRadiusFactor;
            ProjectileDespawnMargin   = projectileDespawnMargin;
            KiteFleeFactor            = kiteFleeFactor;
            GlobalSearchRadius        = globalSearchRadius;
            FleeThreatWeight          = fleeThreatWeight;
            FleeHomeWeight            = fleeHomeWeight;
            FleeWallWeight            = fleeWallWeight;
            FleeWallMargin            = fleeWallMargin;
            FleeThreatRadius          = fleeThreatRadius;
            KiteStrafeWeight          = kiteStrafeWeight;
            DisplaceSpeedPerSecond    = displaceSpeedPerSecond;
            CannonballWidthMult       = cannonballWidthMult;
            WallImpactDamageMult      = wallImpactDamageMult;
            WallImpactStunSeconds     = wallImpactStunSeconds;
            OvertimeStartSeconds      = overtimeStartSeconds;
            OvertimeDamagePerSecond   = overtimeDamagePerSecond;
            SprintSpeedMult           = sprintSpeedMult;
            SprintEnterGap            = sprintEnterGap;
            SprintExitGap             = sprintExitGap;
            SprintWalkSeconds         = sprintWalkSeconds;
            SprintRampSeconds         = sprintRampSeconds;
            RecastRecoverySpeed       = recastRecoverySpeed;
            RecastWindupSpeed         = recastWindupSpeed;
        }

        /// <summary>
        /// Доля разгона [0..1] после <paramref name="wantTicks"/> тиков непрерывного намерения бежать:
        /// ноль всю «прогулочную» часть, потом линейный набор до единицы. Формула живёт здесь, а не в
        /// движении, потому что её же читают тесты и dev-инструменты — второй копии быть не должно.
        /// </summary>
        public float SprintRampAt(int wantTicks)
        {
            // Границы считаются в ТИКАХ, а не в секундах: тик, попавший ровно на конец прогулочной части,
            // на float-арифметике оказывался то до неё, то после (30 × (1/30) больше единицы), и юнит
            // получал одну тридцатимиллионную разгона — формально «уже бежит».
            int walkTicks = Ticks(SprintWalkSeconds);
            int over = wantTicks - walkTicks;
            if (over <= 0) return 0f;

            int rampTicks = Ticks(SprintRampSeconds);
            if (rampTicks <= 0) return 1f;

            float ramp = (float)over / rampTicks;
            return ramp >= 1f ? 1f : ramp;
        }

        private static int Ticks(float seconds)
        {
            if (seconds <= 0f) return 0;
            int ticks = (int)System.Math.Round(seconds * SimConstants.TickRate, System.MidpointRounding.AwayFromZero);
            return ticks < 0 ? 0 : ticks;
        }

        /// <summary>
        /// Сколько тиков длится полёт: дистанция ÷ скорость. <paramref name="speedPerSecond"/> ≤ 0 —
        /// берётся общий дефолт <see cref="DisplaceSpeedPerSecond"/>, поэтому источник с иным характером
        /// толчка (медленный тяжёлый бросок) задаёт свою скорость, а не свою длительность.
        /// Минимум один тик: нулевой полёт не поднял бы событие конца смещения, на котором висят реактивы.
        /// </summary>
        public int DisplaceTicks(float distance, float speedPerSecond = 0f)
        {
            float speed = speedPerSecond > 0f ? speedPerSecond : DisplaceSpeedPerSecond;
            if (speed <= 0f) return 1;
            float seconds = distance / speed;
            int ticks = (int)(seconds * SimConstants.TickRate + 0.5f);
            return ticks < 1 ? 1 : ticks;
        }

        /// <summary>
        /// Множитель наносимого урона на данной секунде боя: 1 до порога, дальше линейный рост.
        /// Лечения и щитов НЕ касается — в этом весь смысл правила.
        /// </summary>
        public float OvertimeDamageMultiplier(float elapsedSeconds)
        {
            if (OvertimeDamagePerSecond <= 0f) return 1f;
            float over = elapsedSeconds - OvertimeStartSeconds;
            return over <= 0f ? 1f : 1f + over * OvertimeDamagePerSecond;
        }

        /// <summary>Код-дефолты (исторические значения фиксированных констант — контракт баланса).</summary>
        public static readonly SimTuning Default = new SimTuning(
            bodyRadiusPerSize:         0.3f,
            separationStrength:        0.5f,
            separationIterations:      1,
            separationSameTeamScale:   0.35f,
            projectileHitRadiusFactor: 0.25f,
            projectileDespawnMargin:   5f,
            kiteFleeFactor:            0.6f,
            globalSearchRadius:        500f,
            fleeThreatWeight:          1f,
            fleeHomeWeight:            0.5f,
            fleeWallWeight:            1.5f,
            fleeWallMargin:            2.5f,
            fleeThreatRadius:          6f,
            kiteStrafeWeight:          0.35f,
            displaceSpeedPerSecond:    10f,
            cannonballWidthMult:       1.25f,
            wallImpactDamageMult:      1f,
            wallImpactStunSeconds:     1f,
            overtimeStartSeconds:      90f,
            overtimeDamagePerSecond:   0.05f,
            sprintSpeedMult:           1.3f,
            sprintEnterGap:            1f,
            sprintExitGap:             0.3f,
            sprintWalkSeconds:         1f,
            sprintRampSeconds:         0.5f,
            recastRecoverySpeed:       2f,
            recastWindupSpeed:         2f);
    }
}
