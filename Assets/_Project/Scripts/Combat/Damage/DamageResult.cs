namespace Guildmaster.Combat
{
    /// <summary>
    /// Результат пайплайна урона после применения к цели.
    /// Возвращается <see cref="DamagePipeline.Execute"/> для последующих триггеров
    /// (lifesteal, шипы — Фаза 2).
    /// </summary>
    public readonly struct DamageResult
    {
        /// <summary>Урон, вычтенный из HP (после поглощения щитом).</summary>
        public readonly float HpDamage;

        /// <summary>Урон, поглощённый щитом.</summary>
        public readonly float ShieldDamage;

        /// <summary>Цель погибла в результате этого удара.</summary>
        public readonly bool KilledTarget;

        /// <summary>Суммарный урон (HP + щит).</summary>
        public float TotalDamage => HpDamage + ShieldDamage;

        public DamageResult(float hpDamage, float shieldDamage, bool killedTarget)
        {
            HpDamage     = hpDamage;
            ShieldDamage = shieldDamage;
            KilledTarget = killedTarget;
        }
    }
}
