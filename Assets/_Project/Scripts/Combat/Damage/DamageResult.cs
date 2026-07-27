using Guildmaster.Data.Definitions;

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

        /// <summary>
        /// Урон, срезанный до тела: броня, пробивание и обе эффективности. Разница между тем, что
        /// замахнулись нанести, и тем, что дошло.
        /// </summary>
        /// <remarks>
        /// Нужен, чтобы стенд отвечал на вопрос «чем боец не умер»: танк на броне и танк под хилером
        /// живут одинаково долго, а чинить их надо разное. В <see cref="TotalDamage"/> НЕ входит —
        /// это то, чего не случилось.
        /// </remarks>
        public readonly float Mitigated;

        /// <summary>Цель погибла в результате этого удара.</summary>
        public readonly bool KilledTarget;

        /// <summary>
        /// Откуда пришёл урон (авто-атака/способность/DoT/ответка). Эхо <see cref="DamageRequest.SourceKind"/> —
        /// чтобы потребители события (метрики стенда, презентация) могли раскладывать урон по источнику, не меняя
        /// сигнатуру <c>OnDamageDealt</c> (шов sim→presentation остаётся неизменным).
        /// </summary>
        public readonly DamageSourceKind SourceKind;

        /// <summary>Школа урона — эхо <see cref="DamageRequest.School"/> (hit-flash по школе в презентации).</summary>
        public readonly DamageSchool School;

        /// <summary>Сродство урона — эхо <see cref="DamageRequest.Affinity"/> (тинт вспышки Poison/Light/Dark).</summary>
        public readonly DamageAffinity Affinity;

        /// <summary>
        /// Стихия урона — эхо <see cref="DamageRequest.Element"/>. Броню не делит (она одна на всю
        /// магию), но нужна потребителям: искры по стихии в презентации, разбор огня в метриках.
        /// </summary>
        public readonly MagicElement Element;

        /// <summary>
        /// Множитель уязвимости цели, вложенный в этот удар — эхо <see cref="DamageRequest.Vulnerability"/>.
        /// 1 = уязвимостей не было.
        /// </summary>
        public readonly float Vulnerability;

        /// <summary>Суммарный урон (HP + щит).</summary>
        public float TotalDamage => HpDamage + ShieldDamage;

        /// <summary>
        /// Урон нанесён ПРЯМЫМ попаданием — ударом или заклинанием (а не тиком DoT и не ответкой).
        /// Зеркало <see cref="DamageRequest.IsDirectHit"/>: у прямого попадания есть автор, момент и
        /// сторона, поэтому только он имеет право на направленный фидбэк (искры, отброс, выпад).
        /// </summary>
        public bool IsDirectHit => SourceKind is DamageSourceKind.AutoAttack or DamageSourceKind.Ability;

        /// <summary>
        /// Сколько из <see cref="TotalDamage"/> добавили уязвимости цели («Угли»). Не отдельное слагаемое,
        /// а доля внутри уже посчитанного числа: удар без уязвимостей был бы на столько слабее.
        /// </summary>
        public float VulnerabilityBonus => Vulnerability > 1f ? TotalDamage * (1f - 1f / Vulnerability) : 0f;

        public DamageResult(float hpDamage, float shieldDamage, bool killedTarget,
            DamageSourceKind sourceKind = DamageSourceKind.Ability,
            DamageSchool school = DamageSchool.Physical,
            DamageAffinity affinity = DamageAffinity.None,
            MagicElement element = MagicElement.None,
            float vulnerability = 1f,
            float mitigated = 0f)
        {
            Element       = element;
            HpDamage      = hpDamage;
            ShieldDamage  = shieldDamage;
            Mitigated     = mitigated;
            KilledTarget  = killedTarget;
            SourceKind    = sourceKind;
            School        = school;
            Affinity      = affinity;
            Vulnerability = vulnerability;
        }
    }
}
