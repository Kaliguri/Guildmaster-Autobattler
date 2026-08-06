using System.Globalization;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>Как роль произносит свой текст. Значения USS-свойства <c>--gm-text-case</c>.</summary>
    public enum UiTextCaseMode
    {
        /// <summary>Как написано в источнике — поведение по умолчанию.</summary>
        None,

        /// <summary>Прописными.</summary>
        Upper
    }

    /// <summary>
    /// Регистр текста как СТИЛЬ, а не как содержимое строки.
    /// </summary>
    /// <remarks>
    /// <b>Зачем это вообще.</b> В UI Toolkit нет <c>text-transform</c>, поэтому капитель до 06.08.2026
    /// задавали сами буквы в разметке. Стоило это ровно того, чего и следовало ожидать: в дереве
    /// одновременно жили «ОТМЕНА» и «Отмена», «НАСТРОЙКИ» и «Настройки», «СОХРАНИТЬ» и «Сохранить» —
    /// регистр не был ничьим решением, он был случайностью каждой отдельной строки.
    ///
    /// <para><b>Почему не в локализации.</b> «Лучше разве не брать из ЛОКАЛИЗАЦИИ — капс, а сделать
    /// это как стиль, чтобы легко и удобно его добавлять, не меняя локализацию» (Макс, 06.08.2026).
    /// Капс — свойство РОЛИ («кнопка звучит громко»), а не свойство фразы: та же строка «Отмена» в
    /// подсказке обязана остаться строчной. Держать её в лок-ключе значит принимать решение о виде
    /// там, где хранится смысл, и умножать его на число локалей.</para>
    ///
    /// <para><b>Как устроено.</b> Своё custom-свойство USS <c>--gm-text-case</c>; движок его разбирает
    /// и отдаёт коду через <see cref="CustomStyleResolvedEvent"/>, а применяет контрол. Тем же
    /// приёмом у нас живут <c>--gm-plate-fill</c> и <c>--gm-veil-color</c> — механизм в проекте не
    /// новый, новое только применение к тексту.</para>
    ///
    /// <para><b>Граница.</b> Работает у контролов, которые свойство читают (<see cref="PlateButton"/>,
    /// <see cref="Chip"/>) — у них текст идёт через свой сеттер, поэтому регистр держится и при
    /// смене подписи в рантайме. Голому <see cref="Label"/> нужен <see cref="Bind"/>, и он применяет
    /// регистр по разрешению стиля: для литералов разметки этого довольно, а меняющийся текст
    /// ставится через <see cref="SetText"/>.</para>
    ///
    /// <para><b>ГОТЧА, стоившая одного прогона витрины: custom-свойства USS в UI Toolkit НЕ
    /// НАСЛЕДУЮТСЯ.</b> В CSS <c>--var</c> с родителя доходит до любого потомка, здесь — нет: каждый
    /// элемент получает только то, что назначили ЕМУ. Поэтому <c>--gm-text-case</c> на
    /// <c>.gm-button</c> виден кнопке, но не её подписи; капс до подписи доходит не по наследству, а
    /// потому, что контрол применяет его к своему <see cref="Label"/> сам. Роль, у которой нет
    /// своего контрола, обязана объявить регистр СВОИМ правилом.</para>
    /// </remarks>
    public static class UiTextCase
    {
        /// <summary>Свойство USS. Публично: его читают контролы, каждый у себя.</summary>
        public static readonly CustomStyleProperty<string> Property = new("--gm-text-case");

        /// <summary>Разбор значения свойства. Неизвестное имя — «как написано».</summary>
        /// <remarks>
        /// Молча, без предупреждения: опечатка в USS ловится витриной (образец виден в неверном
        /// регистре), а лог здесь сыпался бы на каждый пересчёт стиля.
        /// </remarks>
        public static UiTextCaseMode Parse(string raw)
        {
            switch (raw?.Trim().ToLowerInvariant())
            {
                case "upper": return UiTextCaseMode.Upper;
                default:      return UiTextCaseMode.None;
            }
        }

        /// <summary>Применить регистр к строке.</summary>
        /// <remarks>
        /// Регистр берётся по ТЕКУЩЕЙ КУЛЬТУРЕ, а не инвариантно: в турецком заглавная от «i» — «İ»,
        /// и <c>ToUpperInvariant</c> дал бы там «I», то есть другую букву. Языков у нас пока два, но
        /// ошибка эта из тех, что всплывают через год и выглядят необъяснимо.
        /// </remarks>
        public static string Apply(string source, UiTextCaseMode mode)
        {
            if (string.IsNullOrEmpty(source) || mode != UiTextCaseMode.Upper) return source;
            return source.ToUpper(CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// Привязать голый <see cref="Label"/> к правилу регистра из USS.
        /// </summary>
        /// <remarks>
        /// Исходная строка хранится в самой привязке, поэтому регистр можно снять правкой USS —
        /// текст вернётся тем, каким его написали. Привязка живёт ровно столько, сколько элемент:
        /// её держит зарегистрированный на нём коллбэк.
        /// </remarks>
        public static void Bind(Label label)
        {
            if (label == null) return;
            var binding = new LabelBinding(label);
            label.RegisterCallback<CustomStyleResolvedEvent>(binding.OnCustomStyleResolved);
        }

        /// <summary>
        /// Поставить тексту привязанного <see cref="Label"/> новое значение с учётом регистра.
        /// </summary>
        /// <remarks>
        /// Нужен там, где подпись меняется в рантайме: события «текст сменился» у
        /// <see cref="TextElement"/> нет, а <see cref="CustomStyleResolvedEvent"/> приходит по смене
        /// СТИЛЯ и на смену содержимого не реагирует.
        /// </remarks>
        public static void SetText(Label label, string text)
        {
            if (label == null) return;
            label.text = text;
            // Привязка узнает новый источник по несовпадению с тем, что записала сама.
            label.MarkDirtyRepaint();
        }

        /// <summary>
        /// Состояние одной привязки: исходник и то, что мы в элемент записали.
        /// </summary>
        /// <remarks>
        /// Сравнение с ЗАПИСАННЫМ нами — единственный способ отличить «текст сменили снаружи» от
        /// «это наш же результат». Без него привязка при первом же пересчёте стиля приняла бы
        /// прописную строку за источник, и снять регистр правкой USS стало бы нечем.
        /// </remarks>
        private sealed class LabelBinding
        {
            private readonly Label _label;
            private string _source;
            private string _rendered;

            public LabelBinding(Label label)
            {
                _label = label;
                _source = label.text;
                _rendered = label.text;
            }

            public void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
            {
                if (!ReferenceEquals(_label.text, _rendered) && _label.text != _rendered)
                {
                    _source = _label.text;
                }

                UiTextCaseMode mode = evt.customStyle.TryGetValue(Property, out string raw)
                    ? Parse(raw)
                    : UiTextCaseMode.None;

                _rendered = Apply(_source, mode);
                _label.text = _rendered;
            }
        }
    }
}
