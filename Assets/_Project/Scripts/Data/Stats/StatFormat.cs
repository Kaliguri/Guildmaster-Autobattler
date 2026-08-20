using System.Globalization;
using System.Text;

namespace Guildmaster.Data.Stats
{
    /// <summary>
    /// Подписи единиц измерения. Приходят из локализации, а не из кода: «%» одинаков везде,
    /// но «с» / «/с» переводятся. Фолбэк <see cref="Ru"/> — та же страховка, что code-фолбэки
    /// экранов на случай незаведённого ключа.
    /// </summary>
    public readonly struct UnitLabels
    {
        public readonly string Percent;
        public readonly string Seconds;
        public readonly string PerSecond;

        public UnitLabels(string percent, string seconds, string perSecond)
        {
            Percent = percent;
            Seconds = seconds;
            PerSecond = perSecond;
        }

        /// <summary>Русский фолбэк для дева и тестов.</summary>
        public static UnitLabels Ru => new UnitLabels("%", "с", "/с");
    }

    /// <summary>
    /// Стат-значение, подготовленное к показу: разбор плюс уже ЛОКАЛИЗОВАННЫЕ имена источников.
    /// </summary>
    /// <remarks>
    /// Имена приходят готовыми, а не ключами, чтобы форматирование не тянуло за собой
    /// локализацию: слой описаний резолвит ключи один раз, форматтер остаётся без состояния
    /// и без зависимостей — его можно звать из тестов и из Smart String одинаково.
    /// </remarks>
    public readonly struct FormattedStat
    {
        public readonly StatValue Value;

        /// <summary>Локализованные имена источников, параллельно <see cref="StatValue.Terms"/>; <c>null</c> у безымянных.</summary>
        public readonly string[] SourceNames;

        /// <summary>Показывать разложение, а не только итог.</summary>
        public readonly bool Detailed;

        /// <summary>
        /// Строку увидит элемент с rich text — число можно выделить полужирным. Флагом, а не всегда:
        /// в поле без rich text теги вылезли бы в текст как «&lt;b&gt;47&lt;/b&gt;».
        /// </summary>
        public readonly bool Rich;

        /// <summary>
        /// Подписи единиц. Едут внутри значения, а не берутся форматтером снаружи: Smart Format
        /// создаёт свои форматтеры сам (сериализует их в настройках локализации), и дотянуться
        /// оттуда до сервиса нечем — значит всё нужное должно приехать в аргументе.
        /// </summary>
        public readonly UnitLabels Units;

        public FormattedStat(StatValue value, string[] sourceNames, bool detailed, UnitLabels units,
            bool rich = false)
        {
            Value = value;
            SourceNames = sourceNames ?? System.Array.Empty<string>();
            Detailed = detailed;
            Units = units;
            Rich = rich;
        }
    }

    /// <summary>
    /// Превращение разобранного стата в строку (план UI-реворка §II.10.1, §II.10.4).
    /// Единственное место, где решается, как выглядит число — иначе одна и та же величина
    /// на разных экранах покажется по-разному.
    /// </summary>
    public static class StatFormat
    {
        /// <summary>Источников больше — разложение схлопывается в «база + бонусы» (§II.10.4).</summary>
        public const int MaxDetailedTerms = 4;

        /// <summary>
        /// Неразрывный пробел между числом и единицей: «25 %» не должно разорваться переносом
        /// строки. Записан escape-последовательностью намеренно — невидимый символ в исходнике
        /// даёт строки, которые выглядят одинаково и при этом не равны (на чём тесты уже спотыкались).
        /// </summary>
        public const string Nbsp = "\u00A0";

        /// <summary>Итоговое значение с единицей измерения: «47», «25 %», «1.2/с», «×1.15».</summary>
        public static string Value(StatValue value, UnitLabels units)
            => Scalar(value.Final, value.Kind, units);

        /// <summary>
        /// Готовая к показу строка: краткая («47») или с разбором
        /// («30 + 12 (Пылающий клинок) + 5 (Ярость) = 47»).
        /// </summary>
        public static string Describe(in FormattedStat stat)
        {
            UnitLabels units = stat.Units;
            StatValue v = stat.Value;
            // Итог — то, ради чего фразу читают: в rich text он выделяется полужирным, чтобы глаз
            // цеплялся за число, а не за слова вокруг него.
            if (!stat.Detailed || !v.IsModified) return Emphasize(Scalar(v.Final, v.Kind, units), stat.Rich);

            var sb = new StringBuilder();
            sb.Append(Scalar(v.Base, v.Kind, units));

            // Слишком длинный разбор нечитаем — игрок не сканирует шесть слагаемых глазами
            // (претензия к расширенным тултипам LoL). Схлопываем в одну надбавку.
            if (v.Terms.Length > MaxDetailedTerms)
            {
                AppendSigned(sb, v.Bonus, v.Kind, units);
            }
            else
            {
                for (int i = 0; i < v.Terms.Length; i++)
                {
                    AppendSigned(sb, v.Terms[i].Contribution, v.Kind, units);
                    string name = i < stat.SourceNames.Length ? stat.SourceNames[i] : null;
                    if (!string.IsNullOrEmpty(name)) sb.Append(" (").Append(name).Append(')');
                }
            }

            sb.Append(" = ").Append(Emphasize(Scalar(v.Final, v.Kind, units), stat.Rich));
            return sb.ToString();
        }

        private static string Emphasize(string value, bool rich) => rich ? "<b>" + value + "</b>" : value;

        private static void AppendSigned(StringBuilder sb, float delta, ValueKind kind, UnitLabels units)
        {
            sb.Append(delta < 0f ? " - " : " + ");
            sb.Append(Scalar(System.Math.Abs(delta), kind, units));
        }

        private static string Scalar(float value, ValueKind kind, UnitLabels units)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            switch (kind)
            {
                case ValueKind.Percent:
                    return Trim(value * 100f, c) + Nbsp + units.Percent;
                case ValueKind.Multiplier:
                    // Два знака: у множителя третья значащая цифра — это проценты эффекта.
                    // ×1.15, округлённый до ×1.1, врёт игроку на пять процентов урона.
                    return "×" + Trim(value, c, 2);
                case ValueKind.Seconds:
                    return Trim(value, c) + Nbsp + units.Seconds;
                case ValueKind.PerSecond:
                    return Trim(value, c) + units.PerSecond;
                case ValueKind.Count:
                    return ((int)System.Math.Round(value)).ToString(c);
                default:
                    return Trim(value, c);
            }
        }

        /// <summary>Округление до <paramref name="decimals"/> знаков без хвостовых нулей: «47», «1.2», «1.15».</summary>
        private static string Trim(float value, CultureInfo c, int decimals = 1)
        {
            float rounded = (float)System.Math.Round(value, decimals);
            return rounded == (int)rounded
                ? ((int)rounded).ToString(c)
                : rounded.ToString(decimals >= 2 ? "0.##" : "0.#", c);
        }
    }
}
