namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Тип урона — ЕДИНСТВЕННАЯ ось, которую задаёт автор контента. Каждый источник урона
    /// (автоатака юнита, каждая способность, каждый бьющий компонент эффекта) объявляет свой тип
    /// <b>явно</b>; школа брони из типа выводится функцией <see cref="DamageTypes.SchoolOf"/> и
    /// отдельным полем нигде не живёт.
    /// <para><b>Почему одна ось, а не четыре.</b> До 2026-07-30 тип складывался из школы, физ-подтипа,
    /// магической стихии и сродства — четырёх независимых полей. Модель позволяла выразить
    /// бессмыслицу («физический огонь»), гасила её молча в конструкторе и, главное, допускала
    /// «просто физический» урон: поле подтипа оставалось дефолтным, и никто этого не замечал —
    /// взрыв костей Некроманта так полгода не попадал в хрупкость ледяной статуи. Вердикт Макса:
    /// тип обязателен везде, школа — его следствие. Развилка целиком — запись журнала
    /// <c>2026-07-30-damage-type-is-one-axis</c>.</para>
    /// <para><b>Значения нумерованы с зазорами по школам</b> (1.., 10.., 20..): в YAML-ассетах виден
    /// голый int, и по десятку сразу читается ветка. Существующие номера не менять — ассеты хранят
    /// число, а не имя.</para>
    /// </summary>
    /// <remarks>
    /// <b><see cref="Undefined"/> невалиден и означает «автор забыл».</b> Он занимает 0 намеренно:
    /// это дефолт любого незаполненного C#-поля и любого нового поля в старом ассете, поэтому пропуск
    /// шумит сразу, а не работает молча как «просто физика». Ловится в двух местах:
    /// <c>DamageRequest</c> бьёт ошибкой в консоль, а <c>DamageTypeCoverageTests</c> сканирует весь
    /// контент и падает, если <see cref="Undefined"/> остался у источника, который наносит урон.
    /// <para><b>Наследования типа от юнита НЕ существует</b> (вердикт Макса 2026-07-30): у автоатаки
    /// свой тип, у каждой способности — свой. Прежний <c>Inherit</c> снят вместе с
    /// <c>*Override</c>-перечислениями: он был единственным способом не назвать тип и при этом
    /// выглядеть законно.</para>
    /// </remarks>
    public enum DamageType
    {
        /// <summary>Не задан — дефект контента, а не значение. См. <c>remarks</c> типа.</summary>
        Undefined = 0,

        // --- Физическая школа: гасится физбронёй ---

        /// <summary>Дробящий. Промороженная цель получает от него +20% (хрупкость, верхняя ступень холода).</summary>
        Blunt = 1,

        /// <summary>Режущий.</summary>
        Slash = 2,

        /// <summary>Колющий.</summary>
        Pierce = 3,

        /// <summary>
        /// Кровотечение — урон открытой раны, а не оружия. Отдельный тип, потому что школа у него
        /// фиксированно физическая, а природа своя: как <see cref="Pierce"/> он попадал бы под
        /// «уязвимость к колющему» и не давал бы выразить «нежить не кровоточит».
        /// </summary>
        Bleed = 4,

        /// <summary>
        /// Яд физический (отравленный клинок, споры). Яд — единственная природа урона, чья школа
        /// НЕ выводится однозначно: канон требует «школа по конкретному яду, Физическая или
        /// Магическая, но не Чистый». Поэтому он занимает два значения — исключение живёт в списке
        /// типов, где его видно, а не ветвлением в коде, где его забудут.
        /// </summary>
        PoisonPhysical = 5,

        // --- Магическая школа: гасится магбронёй (одной на все стихии) ---

        /// <summary>Огонь. Копит «Угли» и усиливается ими.</summary>
        Fire = 10,

        /// <summary>Лёд. Кладёт стаки «Изморози».</summary>
        Ice = 11,

        /// <summary>Молния.</summary>
        Lightning = 12,

        /// <summary>
        /// Аркана — магия без стихии. Это законный ответ на «магический урон, но не огонь и не лёд»:
        /// поэтому «магия без элемента» в новой модели невыразима.
        /// </summary>
        Arcane = 13,

        /// <summary>Яд магический (ядовитое облако заклинателя). Пара к <see cref="PoisonPhysical"/>.</summary>
        PoisonMagical = 14,

        // --- Чистая школа: мимо любой брони ---

        /// <summary>
        /// Свет. По канону идёт мимо брони — потому и редок. Свой глагол: очищение и урон,
        /// часть которого лечит.
        /// </summary>
        Light = 20,

        /// <summary>Тьма. Мимо брони; свой глагол — большая голая мощь без условий.</summary>
        Dark = 21,

        /// <summary>
        /// Чистый без окраски — для урона, который обязан пройти мимо брони, но не является ни
        /// Светом, ни Тьмой (Раскол ледяной статуи бьёт долей максимального HP).
        /// </summary>
        Pure = 22,
    }

    /// <summary>
    /// Правила типа урона. Единственный владелец соответствия «тип → школа»: пайплайну школа нужна,
    /// чтобы выбрать броню, но задавать её отдельно нельзя — только вывести отсюда.
    /// </summary>
    public static class DamageTypes
    {
        /// <summary>
        /// Все валидные типы (без <see cref="DamageType.Undefined"/>) — порядок стабилен и совпадает
        /// с объявлением. Нужен тестам тотальности и редакторным выпадашкам.
        /// </summary>
        public static readonly DamageType[] All =
        {
            DamageType.Blunt, DamageType.Slash, DamageType.Pierce, DamageType.Bleed,
            DamageType.PoisonPhysical,
            DamageType.Fire, DamageType.Ice, DamageType.Lightning, DamageType.Arcane,
            DamageType.PoisonMagical,
            DamageType.Light, DamageType.Dark, DamageType.Pure,
        };

        /// <summary>
        /// Школа брони, которой гасится этот тип. Тотальна по построению: новый тип, забытый в этом
        /// switch, роняет <c>DamageTypeCoverageTests</c>, а не тихо становится физическим.
        /// </summary>
        /// <param name="type">Тип урона источника.</param>
        /// <returns>Школа; для <see cref="DamageType.Undefined"/> — <see cref="DamageSchool.Physical"/>
        /// как наименее ломающий фолбэк, но сам факт уже отловлен вызывающим.</returns>
        public static DamageSchool SchoolOf(DamageType type)
        {
            switch (type)
            {
                case DamageType.Blunt:
                case DamageType.Slash:
                case DamageType.Pierce:
                case DamageType.Bleed:
                case DamageType.PoisonPhysical:
                    return DamageSchool.Physical;

                case DamageType.Fire:
                case DamageType.Ice:
                case DamageType.Lightning:
                case DamageType.Arcane:
                case DamageType.PoisonMagical:
                    return DamageSchool.Magical;

                case DamageType.Light:
                case DamageType.Dark:
                case DamageType.Pure:
                    return DamageSchool.True;

                default:
                    return DamageSchool.Physical;
            }
        }

        /// <summary>Гасится физбронёй.</summary>
        public static bool IsPhysical(DamageType type) => SchoolOf(type) == DamageSchool.Physical;

        /// <summary>Гасится магбронёй.</summary>
        public static bool IsMagical(DamageType type) => SchoolOf(type) == DamageSchool.Magical;

        /// <summary>Идёт мимо брони.</summary>
        public static bool IsTrue(DamageType type) => SchoolOf(type) == DamageSchool.True;

        /// <summary>
        /// Ядовитая природа — обе школьные половины разом. Нужна потребителям, которым важен яд как
        /// таковой, чтобы им не приходилось помнить про два значения.
        /// </summary>
        public static bool IsPoison(DamageType type)
            => type == DamageType.PoisonPhysical || type == DamageType.PoisonMagical;

        /// <summary>Задан ли тип вообще. Полная проверка контента живёт в тестах покрытия.</summary>
        public static bool IsDefined(DamageType type) => type != DamageType.Undefined;

        /// <summary>
        /// Подходит ли входящий урон под фильтр защитного эффекта. Единственный владелец правила: щиты
        /// по типу, резисты и «копится с огня» спрашивают именно здесь, поэтому «широкий» и «узкий»
        /// фильтр не могут разойтись поведением.
        /// </summary>
        /// <param name="filter">Тип, на который настроен эффект.</param>
        /// <param name="wholeSchool">
        /// <c>true</c> — ловить всю школу <paramref name="filter"/> («Аркановый щит» держит любую магию);
        /// <c>false</c> — только сам тип («Огненный вард» держит лишь Огонь). Так фильтр «вся школа»
        /// выражается без отдельного поля школы, которое могло бы разойтись с типом.
        /// </param>
        /// <param name="actual">Тип пришедшего урона.</param>
        public static bool Matches(DamageType filter, bool wholeSchool, DamageType actual)
            => wholeSchool ? SchoolOf(filter) == SchoolOf(actual) : filter == actual;
    }
}
