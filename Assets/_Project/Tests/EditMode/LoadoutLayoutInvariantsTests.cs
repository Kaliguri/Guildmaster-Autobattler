using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;

namespace Guildmaster.Tests.EditMode
{
    /// <summary>
    /// Инварианты РАСКЛАДКИ экрана инвентаря: то, что до сих пор ловилось только глазами в билде.
    /// <para>
    /// Повод — п.7 из QA: тулбар над гридом карт не сходился краями с самими картами, и находка
    /// возвращалась три раунда подряд. Причина была арифметической (ширина колонки задана числом
    /// 642 вместо 638), а поймать её вручную трудно: 4px не видно, пока не приложишь линейку.
    /// USS посчитать это за нас не может — UI Toolkit не поддерживает calc() и математику над
    /// переменными, поэтому ширина остаётся числом, а согласованность держит ЭТОТ тест.
    /// </para>
    /// </summary>
    [TestFixture]
    public class LoadoutLayoutInvariantsTests : UITestFixture
    {
        private const string ScreenUxml = "Assets/_Project/UI/Screens/LoadoutInventoryScreen.uxml";
        private const string ThemeTss = "Assets/_Project/UI/Theme/GuildmasterRuntimeTheme.tss";
        private const string CardUxml = "Assets/_Project/UI/Screens/RelicArcanaCard.uxml";

        /// <summary>Допуск в пикселях: края обязаны совпадать, а не «примерно совпадать».</summary>
        private const float Tolerance = 0.5f;

        private VisualElement _search;
        private VisualElement _sort;
        private VisualElement[] _cards;

        /// <summary>
        /// Тема и размер панели задаются В КОНСТРУКТОРЕ: панель фикстуры создаётся до [SetUp],
        /// и присвоение в SetUp уже ни на что не влияет — экран считался бы без единого стиля
        /// (карточка выходила 1907×30 вместо 132×227).
        /// </summary>
        public LoadoutLayoutInvariantsTests()
        {
            panelSize = new UnityEngine.Vector2(1920, 1080);   // нативная канва проекта
            themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemeTss);
        }

        [SetUp]
        public void BuildScreen()
        {
            var screen = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ScreenUxml);
            var card = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(CardUxml);
            Assert.NotNull(themeStyleSheet, $"не найдена тема {ThemeTss}");
            Assert.NotNull(screen, $"не найден экран {ScreenUxml}");
            Assert.NotNull(card, $"не найден шаблон карточки {CardUxml}");

            screen.CloneTree(rootVisualElement);

            var scroll = rootVisualElement.Q<ScrollView>("relic-grid");
            Assert.NotNull(scroll, "в разметке нет #relic-grid");

            // Грид собирается ТАК ЖЕ, как во View: отдельный контейнер внутри ScrollView, а не его
            // contentContainer (в режиме Vertical ScrollView инлайном форсит column/no-wrap, и
            // перенос из USS не работает), плюс всегда видимый скроллбар — под него отведено место
            // в ширине колонки. Отступишь от этой сборки — тест начнёт мерить другую раскладку.
            scroll.mode = ScrollViewMode.Vertical;
            scroll.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
            var grid = new VisualElement();
            grid.AddToClassList("gm-loadout__grid");
            scroll.Add(grid);

            // Ровно один полный ряд: инвариант проверяет края ПЕРВОЙ и ПОСЛЕДНЕЙ карты в ряду.
            _cards = new VisualElement[4];
            for (int i = 0; i < _cards.Length; i++)
            {
                VisualElement clone = card.CloneTree();
                _cards[i] = clone.childCount > 0 ? clone[0] : clone;
                grid.Add(_cards[i]);
            }

            _search = rootVisualElement.Q<TextField>("search");
            _sort = rootVisualElement.Q<Button>("sort");
            Assert.NotNull(_search, "в разметке нет #search");
            Assert.NotNull(_sort, "в разметке нет #sort");

            simulate.FrameUpdate();   // прогон кадра: без него раскладка ещё не посчитана
        }

        [Test]
        public void Ряд_карт_помещается_в_колонку_целиком()
        {
            // Если карты не влезают в один ряд — дальше проверять края бессмысленно: перенос
            // сместит «последнюю в ряду», и тест начнёт врать вместо того, чтобы падать.
            Assert.AreEqual(_cards[0].worldBound.yMin, _cards[3].worldBound.yMin, Tolerance,
                "четыре карты не встали в один ряд — колонка уже ряда карт");
        }

        [Test]
        public void Левый_край_поиска_совпадает_с_первой_картой()
        {
            Assert.AreEqual(_cards[0].worldBound.xMin, _search.worldBound.xMin, Tolerance,
                "поиск не выровнен по левому краю грида");
        }

        [Test]
        public void Правый_край_сортировки_совпадает_с_последней_картой()
        {
            Assert.AreEqual(_cards[3].worldBound.xMax, _sort.worldBound.xMax, Tolerance,
                $"кнопка сортировки не выровнена по правому краю ряда карт. " +
                $"Карточка: {_cards[0].resolvedStyle.width}×{_cards[0].resolvedStyle.height}, " +
                $"ряд: {_cards[0].worldBound.xMin}..{_cards[3].worldBound.xMax}, " +
                $"тулбар: {_search.worldBound.xMin}..{_sort.worldBound.xMax}. " +
                $"Карты: {string.Join(" | ", System.Array.ConvertAll(_cards, c => $"{c.worldBound.xMin:F0}..{c.worldBound.xMax:F0}@y{c.worldBound.yMin:F0}"))}");
        }

        [Test]
        public void Видео_вставка_держит_16_9()
        {
            var video = rootVisualElement.Q<VisualElement>(className: "gm-loadout__video");
            Assert.NotNull(video, "в разметке нет видео-вставки");
            float w = video.resolvedStyle.width;
            float h = video.resolvedStyle.height;
            Assert.Greater(h, 0f, "у вставки нулевая высота — AspectBox не отработал");
            Assert.AreEqual(16f / 9f, w / h, 0.01f, "видео-вставка перестала быть 16:9");
        }
    }
}
