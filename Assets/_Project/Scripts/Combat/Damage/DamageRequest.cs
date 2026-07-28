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

        /// <summary>Школа урона — определяет, какая броня используется (Physical/Magical/True).</summary>
        public readonly DamageSchool School;

        /// <summary>Константа K из StatsConfig (mult = K / (K + effArmor)).</summary>
        public readonly float ArmorK;

        /// <summary>Откуда пришёл урон — гейт для реактивов «на удар».</summary>
        public readonly DamageSourceKind SourceKind;

        /// <summary>Сродство урона (Яд/Свет/Тьма) — метка идентичности источника, на число урона НЕ влияет.</summary>
        public readonly DamageAffinity Affinity;

        /// <summary>
        /// Магический элемент (Огонь/Лёд/Молния/Аркана) при школе <see cref="DamageSchool.Magical"/>.
        /// Броню не делит — она одна на всю магию (ГДД «Статы» §Школа vs сродство), но несёт
        /// идентичность стихии дальше по цепочке: в боевое событие и оттуда в реактивы, которым
        /// важно отличить огонь от прочей магии («Угли» копятся только с огня).
        /// </summary>
        public readonly MagicElement Element;

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

        /// <summary>Урон стихии огня — то, что копит «Угли» и усиливается ими.</summary>
        public bool IsFire => School == DamageSchool.Magical && Element == MagicElement.Fire;

        /// <summary>Урон авто-атаки. «Изворотливость» убийцы уклоняется только от таких.</summary>
        public bool IsAutoAttack => SourceKind == DamageSourceKind.AutoAttack;

        /// <summary>
        /// Прямой удар — авто-атака или атакующая способность. Именно он будит реактивы «на удар»
        /// (шипы Древня, щиты): доты и ответки реактивов не будят, иначе получается каскад.
        /// </summary>
        public bool IsDirectHit => SourceKind is DamageSourceKind.AutoAttack or DamageSourceKind.Ability;

        public DamageRequest(
            RuntimeUnit source,
            RuntimeUnit target,
            float rawDamage,
            DamageSchool school,
            float armorK,
            DamageSourceKind sourceKind = DamageSourceKind.Ability,
            DamageAffinity affinity = DamageAffinity.None,
            MagicElement element = MagicElement.None,
            float vulnerability = 1f,
            float bonusFlatPen = 0f)
        {
            Source        = source;
            Target        = target;
            RawDamage     = rawDamage;
            School        = school;
            ArmorK        = armorK;
            SourceKind    = sourceKind;
            Affinity      = affinity;
            Element       = school == DamageSchool.Magical ? element : MagicElement.None;
            Vulnerability = vulnerability;
            BonusFlatPen  = bonusFlatPen;
        }
    }
}
