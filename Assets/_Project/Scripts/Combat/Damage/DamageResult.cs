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

        /// <summary>
        /// Откуда пришёл урон (авто-атака/способность/DoT/ответка). Эхо <see cref="DamageRequest.SourceKind"/> —
        /// чтобы потребители события (метрики стенда, презентация) могли раскладывать урон по источнику, не меняя
        /// сигнатуру <c>OnDamageDealt</c> (шов sim→presentation остаётся неизменным).
        /// </summary>
        public readonly DamageSourceKind SourceKind;

        /// <summary>Суммарный урон (HP + щит).</summary>
        public float TotalDamage => HpDamage + ShieldDamage;

        public DamageResult(float hpDamage, float shieldDamage, bool killedTarget,
            DamageSourceKind sourceKind = DamageSourceKind.Ability)
        {
            HpDamage     = hpDamage;
            ShieldDamage = shieldDamage;
            KilledTarget = killedTarget;
            SourceKind   = sourceKind;
        }
    }
}
