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

        /// <summary>
        /// Тип урона — эхо <see cref="DamageRequest.Type"/>. Нужен потребителям целиком: вспышка и
        /// искры по типу в презентации, разбор огня и ядов в метриках стенда.
        /// </summary>
        public readonly DamageType Type;

        /// <summary>Школа урона — следствие <see cref="Type"/>, отдельным полем не хранится.</summary>
        public DamageSchool School => DamageTypes.SchoolOf(Type);

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
            DamageType type = DamageType.Undefined,
            float vulnerability = 1f,
            float mitigated = 0f)
        {
            Type          = type;
            HpDamage      = hpDamage;
            ShieldDamage  = shieldDamage;
            Mitigated     = mitigated;
            KilledTarget  = killedTarget;
            SourceKind    = sourceKind;
            Vulnerability = vulnerability;
        }
    }
}
