using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Вывеска игры: надстрочник между двумя линейками, крупное слово под ними, метка стадии.
    /// </summary>
    /// <remarks>
    /// <b>Зачем контрол.</b> Композиция выточена для главного меню (кегли, разрядка, обводка,
    /// оптические компенсации разрядки — всё выверено кадрами 05.08.2026), но жила разметкой
    /// ВНУТРИ меню. Бут-экран, которому та же вывеска нужна не меньше, набирал название сырым
    /// <see cref="Label"/> и не задавал ему гарнитуру вовсе — то есть показывал название игры тем
    /// же интерфейсным гротеском, каким подписаны поля в настройках. Заказ Макса 07.08.2026:
    /// «сделай нашу композицию вообще как этакий "префаб"… чтобы переиспользовать как в меню так и
    /// тут».
    ///
    /// <para><b>Градиент вешает контрол, а не экран.</b> Свойства градиента текста в UI Toolkit нет
    /// вовсе, единственный путь — rich-text тег внутри самого текста. Пока это делал View, каждый
    /// новый экран с вывеской обязан был помнить про тег; забыв его, он получал ту же вывеску без
    /// градиента и не узнавал об этом никак. Три условия, без любого из которых градиент молча не
    /// появится (проверено кадром 05.08.2026): пресет лежит в <c>Resources/Text Color Gradients/</c>;
    /// цвет элемента белый, потому что градиент УМНОЖАЕТСЯ на цвет (роль
    /// <c>--gm-color-text-gradient-base</c>); внешний <c>&lt;color&gt;</c> обязателен при включённой
    /// обводке — с <c>outline-width &gt; 0</c> голый тег градиента не применяется, это
    /// <see href="https://issuetracker.unity3d.com/issues/ui-toolkit-color-gradient-not-applying-to-label-text-when-outline-width-is-set-to-more-than-0">
    /// зарегистрированный баг Unity</see>.</para>
    ///
    /// <para><b>Тексты подаются снаружи.</b> Надстрочник и слово живут РАЗНЫМИ лок-ключами: у них
    /// разные кегли и разрядка, и перевод, склеенный в одну строку, разложить обратно нечем.</para>
    /// </remarks>
    [UxmlElement]
    public partial class Wordmark : VisualElement
    {
        /// <summary>Пресет градиента из <c>Resources/Text Color Gradients/</c>.</summary>
        private const string GradientPreset = "WordmarkPlate";

        private readonly Label _over;
        private readonly Label _main;
        private readonly Label _stage;

        private string _overText  = "HAPPY";
        private string _mainText  = "GUILDMASTERS";
        private string _stageText = string.Empty;

        /// <summary>Надстрочник — слово между линейками. Одевается в градиент автоматически.</summary>
        [UxmlAttribute]
        public string Over
        {
            get => _overText;
            set { _overText = value ?? string.Empty; _over.text = Gradient(_overText); }
        }

        /// <summary>Главное слово вывески. Градиента не берёт: оно и так самое яркое в блоке.</summary>
        [UxmlAttribute]
        public string Main
        {
            get => _mainText;
            set { _mainText = value ?? string.Empty; _main.text = _mainText; }
        }

        /// <summary>
        /// Метка стадии («DEMO», «ALPHA»). Пустая — строка не занимает места.
        /// </summary>
        /// <remarks>
        /// Это статус ИГРЫ, а не версия сборки: номер билда живёт своим штампом в углу экрана.
        /// </remarks>
        [UxmlAttribute]
        public string Stage
        {
            get => _stageText;
            set
            {
                _stageText = value ?? string.Empty;
                _stage.text = Gradient(_stageText);
                _stage.EnableInClassList("gm-wordmark__stage--empty", string.IsNullOrEmpty(_stageText));
            }
        }

        public Wordmark()
        {
            AddToClassList("gm-wordmark");
            // Вывеска — надпись, а не кнопка: указатель сквозь неё проходит.
            pickingMode = PickingMode.Ignore;

            var band = new VisualElement { name = "band", pickingMode = PickingMode.Ignore };
            band.AddToClassList("gm-wordmark__band");
            band.Add(Rule());

            _over = new Label { name = "over" };
            _over.AddToClassList("gm-wordmark__over");
            band.Add(_over);

            band.Add(Rule());
            Add(band);

            _main = new Label { name = "main" };
            _main.AddToClassList("gm-text-display");
            _main.AddToClassList("gm-wordmark__main");
            Add(_main);

            _stage = new Label { name = "stage" };
            _stage.AddToClassList("gm-wordmark__stage");
            Add(_stage);

            // Дефолты прогоняются через сеттеры, иначе градиент не наденется и метка стадии не
            // спрячется: вся эта работа живёт в них, а не в объявлении полей.
            Over  = _overText;
            Main  = _mainText;
            Stage = _stageText;
        }

        private static VisualElement Rule()
        {
            var rule = new VisualElement { pickingMode = PickingMode.Ignore };
            rule.AddToClassList("gm-wordmark__rule");
            return rule;
        }

        private static string Gradient(string text)
            => string.IsNullOrEmpty(text)
                ? string.Empty
                : $"<color=#FFFFFF><gradient=\"{GradientPreset}\">{text}</gradient></color>";
    }
}
