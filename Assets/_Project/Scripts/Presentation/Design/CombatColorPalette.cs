using UnityEngine;

namespace Guildmaster.Presentation.Design
{
    /// <summary>
    /// Цвета боевого UI: полосы HP по принадлежности юнита к СМОТРЯЩЕМУ (свои — одним цветом, чужие —
    /// другим) и полоса щита. Единственный вход презентации за этими цветами; хардкодить их на префабе
    /// нельзя (T-12/T-13 — тема, которая возвращалась дважды).
    /// <para><b>Своих значений здесь больше нет.</b> Цвет живёт в палитре проекта
    /// (<c>UI/Theme/tokens.*.uss</c> → <see cref="Guildmaster.Data.Definitions.GuildmasterPalette"/>),
    /// а этот ассет только называет роли. Иначе бой и интерфейс расходятся молча — ровно так уже
    /// случилось с картой акта, где три цвета уехали от токенов, которые сами же называли.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Design/Combat Color Palette", fileName = "CombatColorPalette")]
    public sealed class CombatColorPalette : ScriptableObject
    {
        [Tooltip("Снимок токенов дизайн-системы: отсюда берутся цвета полос HP и щита " +
                 "(--gm-color-combat-*). Пересобрать — Alebardium → Дизайн-система → Пересобрать палитру.")]
        [SerializeField] private Guildmaster.Data.Definitions.GuildmasterPalette _palette;

        [Header("Яркость свечения юнита")]
        [Tooltip("Во сколько раз главный цвет ярче базы. База в палитре — LDR (яркость 1), а порог bloom " +
                 "стоит на 1.0, поэтому светящийся цвет обязан жить выше единицы. Значения больше 1 в " +
                 "палитру не едут: это сила свечения, а не оттенок.")]
        [SerializeField] private float _mainBrightness = 2.0f;

        [Tooltip("Множитель насыщенного конца палитры разброса (конец 1). Чуть ярче главного цвета: искра " +
                 "в самом цвете юнита должна быть заметнее его снаряда.")]
        [SerializeField] private float _spreadBrightness = 2.2f;

        [Tooltip("Множитель пересвета — начала палитры (конец 0), самой светлой точки роя.")]
        [SerializeField] private float _overbrightBrightness = 2.6f;

        [Tooltip("Насколько пересвет смешан с белым (0.75 = три четверти белого). Пересвет — это тот же " +
                 "оттенок, потерявший насыщенность, а не абстрактный белый: тёплый юнит пересвечивает " +
                 "тёпло-белым, холодный — холодно-белым.")]
        [Range(0f, 1f)] [SerializeField] private float _overbrightWhiteMix = 0.75f;

        // Градиенты разброса собираются один раз на РОЛЬ, а не на юнита: Gradient — класс, и собирать его
        // на каждом ударе значило бы мусорить в бою. Ключ — роль, потому что от юнита цвет уже не зависит.
        private System.Collections.Generic.Dictionary<Data.Definitions.UnitTone, Gradient> _spreads;

        /// <summary>Полоса щита (absorb-пул над HP). Одна на всех, принадлежность не меняет её.</summary>
        public Color Shield => Role("--gm-color-combat-shield");

        // Сырых AllyHp/EnemyHp наружу нет: цвет по принадлежности отдаёт HealthBarColor, и он
        // единственный способ его получить — иначе у одного факта снова два входа (T-12/T-13).

        /// <summary>Цвет HP-бара по признаку «союзник ли юнит для смотрящего».</summary>
        public Color HealthBarColor(bool isAllyOfViewer) =>
            Role(isAllyOfViewer ? "--gm-color-combat-hp-ally" : "--gm-color-combat-hp-enemy");

        /// <summary>
        /// ГЛАВНЫЙ цвет свечения юнита — там, где цвет ровно один: тело снаряда, его след, контур каста.
        /// База берётся по роли из палитры и поднимается в HDR: ровно 1.0 порог bloom не пробивает.
        /// </summary>
        public Color UnitMain(Data.Definitions.UnitTone tone) =>
            Brighten(Data.Definitions.UnitColorRoles.Tone(_palette, tone), _mainBrightness);

        /// <summary>
        /// ДИАПАЗОН разброса частиц юнита: конец 0 — пересвет в его же оттенке, конец 1 — насыщенный цвет.
        /// Частица берёт случайное значение между ними, и рой выходит живым, а не одинаково белым.
        /// <para>Читается ТОЛЬКО по концам (и частицы, и шейдер осколков берут <c>Evaluate(0/1)</c>) —
        /// промежуточные ключи ставить бессмысленно.</para>
        /// </summary>
        public Gradient UnitSpread(Data.Definitions.UnitTone tone)
        {
            _spreads ??= new System.Collections.Generic.Dictionary<Data.Definitions.UnitTone, Gradient>();
            if (_spreads.TryGetValue(tone, out Gradient cached)) return cached;

            Color basis = Data.Definitions.UnitColorRoles.Tone(_palette, tone);
            Color saturated = Brighten(basis, _spreadBrightness);
            Color overbright = Brighten(Color.Lerp(basis, Color.white, _overbrightWhiteMix),
                                       _overbrightBrightness);

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(overbright, 0f), new GradientColorKey(saturated, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

            _spreads[tone] = gradient;
            return gradient;
        }

        /// <summary>
        /// Цвет тела по ступени приглушения. <c>None</c> — белый, то есть арт как нарисован; тинт
        /// умножается на спрайт и служит ровно одному: развести тех, кто делит один арт.
        /// </summary>
        public Color BodyTint(Data.Definitions.BodyShade shade) =>
            Data.Definitions.UnitColorRoles.Shade(_palette, shade);

        // Яркость накручивается по КАНАЛАМ, альфа остаётся своей: альфа у нас несёт прозрачность тела
        // (стелс), и умножать её вместе с цветом значило бы делать юнита невидимым за компанию.
        private static Color Brighten(Color basis, float factor) =>
            new Color(basis.r * factor, basis.g * factor, basis.b * factor, basis.a);

        /// <summary>
        /// Цвет роли из палитры. Пустая ссылка или неизвестное имя — баг разводки: говорим вслух и
        /// отдаём пурпур. Тихо подставлять «похожий» цвет нельзя: полосы HP — то, по чему игрок читает
        /// бой, и неверный цвет здесь врёт про принадлежность юнита.
        /// </summary>
        private Color Role(string token)
        {
            if (_palette == null)
            {
                Debug.LogError($"[CombatColorPalette] - палитра не назначена, цвет '{token}' взять неоткуда " +
                               $"(ассет {name}).");
                return Color.magenta;
            }

            if (_palette.TryGet(token, out Color color)) return color;

            Debug.LogError($"[CombatColorPalette] - в палитре нет роли '{token}'. Пересобери снимок: " +
                           "Alebardium → Дизайн-система → Пересобрать палитру.");
            return Color.magenta;
        }
    }
}
