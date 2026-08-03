using Guildmaster.Data.Definitions;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Откуда пришёл урон. Определяет, будят ли его реактивы «на удар» (шипы, щиты, уклонение):
    /// прямой удар — будит, тик DoT и ответка реактива — нет.
    /// </summary>
    public enum DamageSourceKind
    {
        /// <summary>Урон способности (в т.ч. AOE и детонации). Прямой удар.</summary>
        Ability = 0,

        /// <summary>Урон авто-атаки (мили, линия, снаряд). Прямой удар.</summary>
        AutoAttack = 1,

        /// <summary>Тик DoT. НЕ прямой удар: горение и яд не должны будить шипы и щиты каждым тиком.</summary>
        Periodic = 2,

        /// <summary>Ответка самого реактива (шипы, само-урон). НЕ прямой удар — иначе шипы порождают шипы.</summary>
        Reactive = 3,
    }

    /// <summary>
    /// Входные данные пайплайна урона. Чистая структура — никакого состояния или ссылок на сервисы
    /// (вики «10» §5.4). <see cref="DamagePipeline.Execute"/> мутирует HP/Shield цели.
    /// </summary>
    public readonly struct DamageRequest
    {
        /// <summary>Источник урона (для чтения DamageDealtEff, PhysPen/MagicPen и lifesteal).</summary>
        public readonly RuntimeUnit Source;

        /// <summary>Цель урона.</summary>
        public readonly RuntimeUnit Target;

        /// <summary>Базовый урон до модификаторов пайплайна.</summary>
        public readonly float RawDamage;

        /// <summary>
        /// Тип урона — обязателен и задаётся источником явно (реформа 2026-07-30). Несёт и школу
        /// брони (через <see cref="School"/>), и идентичность для реактивов: «Угли» копятся с
        /// <see cref="DamageType.Fire"/>, хрупкая ледяная статуя добавляет +20%
        /// <see cref="DamageType.Blunt"/>.
        /// </summary>
        public readonly DamageType Type;

        /// <summary>
        /// Школа урона — какая броня гасит удар. Не поле, а следствие <see cref="Type"/>: задать её
        /// в обход типа нельзя, поэтому «физический огонь» невыразим.
        /// </summary>
        public DamageSchool School => DamageTypes.SchoolOf(Type);

        /// <summary>Константа K из StatsConfig (mult = K / (K + effArmor)).</summary>
        public readonly float ArmorK;

        /// <summary>Откуда пришёл урон — гейт для реактивов «на удар».</summary>
        public readonly DamageSourceKind SourceKind;

        /// <summary>
        /// Множитель уязвимости ЦЕЛИ, уже вложенный в <see cref="RawDamage"/> («Угли» усиливают огонь по
        /// подожжённому). Пайплайн его не применяет — он приходит домноженным; поле нужно, чтобы результат
        /// смог сказать, сколько из нанесённого числа дали уязвимости. 1 = чистый урон.
        /// </summary>
        public readonly float Vulnerability;

        /// <summary>
        /// Разовое плоское пробивание брони поверх статов источника — для ударов, которые игнорируют
        /// часть защиты один раз («Атака из скрытности» Убийцы игнорирует 20 ед. брони). Стат
        /// <c>PhysPen</c>/<c>MagicPen</c> так не выразить: он постоянный, а это свойство удара.
        /// </summary>
        public readonly float BonusFlatPen;

        /// <summary>
        /// Разовое ПРОЦЕНТНОЕ пробивание брони поверх статов источника, долей: 0.5 = удар считает броню
        /// вдвое меньшей («Волчий разгон» наездника игнорирует половину защиты). Статами
        /// <c>PhysPenPct</c>/<c>MagicPenPct</c> так не выразить — они постоянные, а это свойство удара.
        /// </summary>
        /// <remarks>
        /// Процент и плоское пробивание НЕ взаимозаменяемы: процент отвечает «толстой» броне, плоское —
        /// тонкой, и разгон, которому карточка обещает половину защиты, плоским числом выразим только
        /// подгонкой под конкретных врагов. Складывается с процентом из статов умножением остатков
        /// (см. <c>DamagePipeline</c>), поэтому суммарное пробивание никогда не превышает 100%.
        /// </remarks>
        public readonly float BonusPctPen;

        /// <summary>
        /// Тот же удар с другим сырым уроном: свойства удара — уязвимость и оба пробивания — переезжают
        /// как есть.
        /// </summary>
        /// <remarks>
        /// Копии «руками» уже трижды теряли поля молча: конструктор длинный, хвостовые аргументы
        /// необязательные, и пропущенный подставляется ДЕФОЛТОМ, а не ошибкой компиляции. Пробивание при
        /// этом просто исчезало — удар считался по полной броне, и увидеть это можно было только замером.
        /// Поэтому копию делает сама структура: добавится поле — оно поедет во все копии разом.
        /// </remarks>
        public DamageRequest WithRawDamage(float rawDamage) =>
            new DamageRequest(Source, Target, rawDamage, Type, ArmorK, SourceKind,
                              Vulnerability, BonusFlatPen, BonusPctPen);

        /// <inheritdoc cref="WithRawDamage(float)"/>
        /// <param name="type">Школа отщеплённой половины — расщепление меняет тип, но не свойства удара.</param>
        public DamageRequest WithRawDamage(float rawDamage, DamageType type) =>
            new DamageRequest(Source, Target, rawDamage, type, ArmorK, SourceKind,
                              Vulnerability, BonusFlatPen, BonusPctPen);

        /// <summary>
        /// Тот же удар, домноженный на множители ЦЕЛИ (уязвимость × овертайм), с записанной уязвимостью.
        /// </summary>
        /// <inheritdoc cref="WithRawDamage(float)" path="/remarks"/>
        public DamageRequest ScaledForTarget(RuntimeUnit target, float scale, float vulnerability) =>
            new DamageRequest(Source, target, RawDamage * scale, Type, ArmorK, SourceKind,
                              vulnerability, BonusFlatPen, BonusPctPen);

        /// <summary>Урон стихии огня — то, что копит «Угли» и усиливается ими.</summary>
        public bool IsFire => Type == DamageType.Fire;

        /// <summary>Урон авто-атаки. «Изворотливость» убийцы уклоняется только от таких.</summary>
        public bool IsAutoAttack => SourceKind == DamageSourceKind.AutoAttack;

        /// <summary>
        /// Прямой удар — авто-атака или атакующая способность. Именно он будит реактивы «на удар»
        /// (шипы Древня, щиты): доты и ответки реактивов не будят, иначе получается каскад.
        /// </summary>
        public bool IsDirectHit => SourceKind is DamageSourceKind.AutoAttack or DamageSourceKind.Ability;

        /// <param name="type">
        /// Тип урона. Дефолта нет намеренно: каждый источник обязан назвать его явно, иначе
        /// пропуск снова стал бы невидимым (реформа 2026-07-30).
        /// </param>
        public DamageRequest(
            RuntimeUnit source,
            RuntimeUnit target,
            float rawDamage,
            DamageType type,
            float armorK,
            DamageSourceKind sourceKind = DamageSourceKind.Ability,
            float vulnerability = 1f,
            float bonusFlatPen = 0f,
            float bonusPctPen = 0f)
        {
            Source        = source;
            Target        = target;
            RawDamage     = rawDamage;
            Type          = type;
            ArmorK        = armorK;
            SourceKind    = sourceKind;
            Vulnerability = vulnerability;
            BonusFlatPen  = bonusFlatPen;
            BonusPctPen   = bonusPctPen;

            // Не фолбэк, а сигнализация: незаданный тип — дефект контента, и он должен быть слышен
            // сразу. Пайплайн отработает по физической школе (см. DamageTypes.SchoolOf), но тихо
            // это не пройдёт. Полный скан контента живёт в DamageTypeCoverageTests.
            if (type == DamageType.Undefined)
                UnityEngine.Debug.LogError(
                    $"[DamageRequest] Тип урона не задан: {source?.Unit?.Id ?? "?"} -> {target?.Unit?.Id ?? "?"}. " +
                    "Источник урона обязан объявить DamageType явно.");
        }
    }
}
