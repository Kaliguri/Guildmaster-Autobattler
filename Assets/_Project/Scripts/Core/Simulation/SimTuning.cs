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
            float kiteStrafeWeight)
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
        }

        /// <summary>Код-дефолты (исторические значения фиксированных констант — контракт баланса).</summary>
        public static readonly SimTuning Default = new SimTuning(
            bodyRadiusPerSize:         0.575f,
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
            kiteStrafeWeight:          0.35f);
    }
}
