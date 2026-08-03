using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Оттенок, которым светит юнит: снаряд, его след, контур каста, искры удара, осколки смерти.
    /// <para>Юнит хранит РОЛЬ, а не цвет. Значение живёт в палитре проекта
    /// (<c>UI/Theme/tokens.*.uss</c> → <see cref="GuildmasterPalette"/>), поэтому смена оттенка правится
    /// в одной строке и подхватывается всеми носителями. Ролей меньше, чем юнитов, и это намеренно:
    /// повторять оттенок разрешено (решение Макса 2026-07-30) — героя от врага отличает полоса HP, а не
    /// цвет искр, а роль на каждого героя превратила бы палитру во второй реестр контента.</para>
    /// <para>Имена — по СМЫСЛУ цвета. Кому какой достался — дизайн-решение, живёт в
    /// <c>gdd/10-vision/vfx-color</c> §Ростер.</para>
    /// </summary>
    public enum UnitTone
    {
        /// <summary>Огонь: горячий оранжевый, самый «физический» огонь ростера.</summary>
        Fire = 0,
        /// <summary>Золото щита: тёплое, но не огонь.</summary>
        Gold = 1,
        /// <summary>Свет: почти белый, низкая насыщенность и есть характер.</summary>
        Light = 2,
        /// <summary>Лайм выстрела: ядовитая зелень, холоднее травы.</summary>
        Lime = 3,
        /// <summary>Живая зелень: дерево, а не яд.</summary>
        Grass = 4,
        /// <summary>Изумруд лечения, с уходом в бирюзу.</summary>
        Emerald = 5,
        /// <summary>Ветер: холодный, но не лёд.</summary>
        Wind = 6,
        /// <summary>Лёд: глубже ветра.</summary>
        Frost = 7,
        /// <summary>Сталь: приглушённо, металл не звенит цветом.</summary>
        Steel = 8,
        /// <summary>Тень: единственный край спектра, читается мгновенно.</summary>
        Shadow = 9,
        /// <summary>Яд: зелень с кислотой.</summary>
        Venom = 10,
        /// <summary>Болото: приглушённая зелень.</summary>
        Bog = 11,
        /// <summary>Тёплое железо: тяжелее пыли, ближе к оружию.</summary>
        Iron = 12,
        /// <summary>Пыльная охра: носитель никого не изображает.</summary>
        Dust = 13,
        /// <summary>Тёплая нейтраль: болванка.</summary>
        NeutralWarm = 14,
        /// <summary>Холодная нейтраль: почти бесцветный, не читается как боец.</summary>
        NeutralCool = 15,
    }

    /// <summary>
    /// Приглушение тела (тинт). Нужно РОВНО там, где один спрайт носят несколько юнитов: один из группы
    /// остаётся <see cref="None"/> и показывает арт как есть, остальные берут ступень.
    /// <para>Тинт УМНОЖАЕТСЯ на готовый цветной арт, поэтому умеет только затемнять и уводить оттенок —
    /// перекрасить им персонажа нельзя (это работа Palette Remapper). Ступеней три: больше на цветном
    /// арте всё равно не различить. Правило сторожит <c>UnitTintPolicyTests</c>.</para>
    /// <para><b>Ступень выбирается по цвету арта, а не по вкусу:</b> умножение гасит только те каналы,
    /// которые в арте есть. На красном воине <see cref="Tan"/> не виден вовсе — его зелёный и синий и так
    /// низкие, а красный ступень почти не трогает; там работает <see cref="Ash"/>.</para>
    /// </summary>
    public enum BodyShade
    {
        /// <summary>Не красим: арт как его нарисовали.</summary>
        None = 0,
        /// <summary>Холодное затемнение — единственная ступень, заметная на ЛЮБОМ арте.</summary>
        Ash = 1,
        /// <summary>Зеленца: гасит красный и синий, зелёный оставляет.</summary>
        Verdant = 2,
        /// <summary>Жёлто-бурый: работает на зелёных и жёлтых артах, на красном — нет.</summary>
        Tan = 3,
    }

    /// <summary>
    /// Мост между ролью в данных и цветом в палитре. Единственное место, где имя USS-токена связано с
    /// элементом перечисления: разъехаться они могут только здесь, и ровно это проверяет
    /// <c>UnitTintPolicyTests.EveryRole_ExistsInThePalette</c>.
    /// </summary>
    public static class UnitColorRoles
    {
        /// <summary>Имя роли в палитре для оттенка юнита.</summary>
        public static string TokenOf(UnitTone tone) => tone switch
        {
            UnitTone.Fire        => "--gm-color-unit-fire",
            UnitTone.Gold        => "--gm-color-unit-gold",
            UnitTone.Light       => "--gm-color-unit-light",
            UnitTone.Lime        => "--gm-color-unit-lime",
            UnitTone.Grass       => "--gm-color-unit-grass",
            UnitTone.Emerald     => "--gm-color-unit-emerald",
            UnitTone.Wind        => "--gm-color-unit-wind",
            UnitTone.Frost       => "--gm-color-unit-frost",
            UnitTone.Steel       => "--gm-color-unit-steel",
            UnitTone.Shadow      => "--gm-color-unit-shadow",
            UnitTone.Venom       => "--gm-color-unit-venom",
            UnitTone.Bog         => "--gm-color-unit-bog",
            UnitTone.Iron        => "--gm-color-unit-iron",
            UnitTone.Dust        => "--gm-color-unit-dust",
            UnitTone.NeutralWarm => "--gm-color-unit-neutral-warm",
            UnitTone.NeutralCool => "--gm-color-unit-neutral-cool",
            _                    => null,
        };

        /// <summary>Имя роли в палитре для ступени приглушения. <see cref="BodyShade.None"/> роли не имеет.</summary>
        public static string TokenOf(BodyShade shade) => shade switch
        {
            BodyShade.Ash     => "--gm-color-unit-dim-ash",
            BodyShade.Verdant => "--gm-color-unit-dim-verdant",
            BodyShade.Tan     => "--gm-color-unit-dim-tan",
            _                 => null,
        };

        /// <summary>
        /// База оттенка из палитры — LDR, при яркости 1. HDR-яркость накручивает потребитель
        /// (<c>CombatColorPalette</c>) множителями: в палитру значения больше единицы не едут, потому что
        /// это уже не оттенок, а сила свечения.
        /// <para>Палитры нет или роль в ней отсутствует — это баг разводки, а не повод подставить
        /// «похожий» цвет: говорим вслух и отдаём пурпур, как в остальной презентации.</para>
        /// </summary>
        public static Color Tone(GuildmasterPalette palette, UnitTone tone) =>
            Resolve(palette, TokenOf(tone), $"оттенок {tone}");

        /// <summary>
        /// Цвет ступени приглушения. <see cref="BodyShade.None"/> — белый, то есть «не красим»: это
        /// штатный ответ, а не отсутствие цвета, поэтому палитра для него не нужна.
        /// </summary>
        public static Color Shade(GuildmasterPalette palette, BodyShade shade) =>
            shade == BodyShade.None ? Color.white : Resolve(palette, TokenOf(shade), $"приглушение {shade}");

        /// <summary>
        /// ЦВЕТ ТЕЛА юнита — одно правило на всех, кто это тело рисует: бой, карточка, витрина. Своя
        /// ступень приглушения, если автор её выставил; иначе — собственный оттенок юнита.
        /// </summary>
        /// <remarks>
        /// Правило живёт здесь, а не у потребителей, потому что потребителей уже двое (боевой
        /// <c>CombatColorPalette</c> и карточка <c>RelicCardVisualRig</c>), а вопрос у них один: «каким
        /// цветом это тело». Разъехавшись, они дали бы юниту два разных цвета в бою и в инвентаре — и
        /// поймать это можно было бы только глазами, переключаясь между экранами.
        /// <para>Ступень главнее тона: она адресная правка автора, а тон — общее правило.</para>
        /// </remarks>
        public static Color Body(GuildmasterPalette palette, BodyShade shade, UnitTone tone) =>
            shade != BodyShade.None ? Shade(palette, shade) : Tone(palette, tone);

        private static Color Resolve(GuildmasterPalette palette, string token, string what)
        {
            if (palette == null)
            {
                Debug.LogError($"[UnitColorRoles] палитра не назначена, {what} взять неоткуда.");
                return Color.magenta;
            }

            if (token == null)
            {
                Debug.LogError($"[UnitColorRoles] у роли «{what}» нет имени токена — перечисление обогнало " +
                               "таблицу TokenOf.");
                return Color.magenta;
            }

            if (palette.TryGet(token, out Color color)) return color;

            Debug.LogError($"[UnitColorRoles] в палитре нет роли '{token}' ({what}). Пересобери снимок: " +
                           "Alebardium → Дизайн-система → Пересобрать палитру.");
            return Color.magenta;
        }
    }
}
