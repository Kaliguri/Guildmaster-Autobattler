using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;

namespace Guildmaster.Tests.EditMode.UI
{
    /// <summary>
    /// Гейт против НАЛОЖЕНИЯ элементов: ни один экран меню не имеет права рисовать содержимое поверх
    /// соседа или за кромкой своего контейнера.
    /// </summary>
    /// <remarks>
    /// <b>Наложение — не случайность разметки, а поведение движка по умолчанию.</b> В Yoga у каждого
    /// элемента <c>flex-shrink: 1</c>, а <c>overflow: visible</c>: контейнеру не хватило высоты — дети
    /// сжимаются все разом, и текст сжатого до нуля лейбла продолжает рисоваться, ложась на соседнюю
    /// кнопку. Ровно это Макс и увидел 04.08.2026: подсказка режима легла на кнопку PvP.
    /// <para><b>Глазами это ловится только там, куда посмотрели.</b> Переполнение зависит от длины
    /// текста, а тот приезжает из локализации и из данных — экран, целый на русском и с двумя домами,
    /// ломается на восьми домах или на английском. Поэтому инвариант держит тест, а не осмотр.</para>
    /// <para><b>Считаем на нативной канве 1920×1080</b> — той же, на которой отрисовывается игра.</para>
    /// </remarks>
    [TestFixture]
    public sealed class ScreenLayoutOverlapTests : UITestFixture
    {
        private const string ThemeTss = "Assets/_Project/UI/Theme/GuildmasterRuntimeTheme.tss";

        /// <summary>Допуск: доли пикселя — это округление раскладки, а не наложение.</summary>
        private const float Tolerance = 0.5f;

        public ScreenLayoutOverlapTests()
        {
            panelSize = new Vector2(1920, 1080);
            themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemeTss);
        }

        /// <summary>
        /// Голая разметка: экраны, у которых текст приходит из UXML и данных не требует.
        /// </summary>
        [TestCase("Assets/_Project/UI/Screens/PauseScreen.uxml")]
        [TestCase("Assets/_Project/UI/Screens/SettingsScreen.uxml")]
        public void Markup_HasNoOverlappingSiblings(string uxmlPath)
        {
            rootVisualElement.Clear();
            Uxml(uxmlPath).CloneTree(rootVisualElement);
            AssertNoOverlap(uxmlPath);
        }

        /// <summary>
        /// Экраны входа в игру — собранные ТАК ЖЕ, как в игре, и на худших данных, какие бывают.
        /// </summary>
        /// <remarks>
        /// Пустой UXML этой поломки не показывает: текста в разметке нет, переполнять нечем. Ровно
        /// поэтому прошлый заход и не поймал наложение — экран ломает содержимое, а не структура.
        /// </remarks>
        [Test]
        public void MainMenu_HasNoOverlappingSiblings()
        {
            rootVisualElement.Clear();
            rootVisualElement.Add(Guildmaster.UI.MainMenuScreenView.Build(
                Uxml("Assets/_Project/UI/Screens/MainMenuScreen.uxml"),
                localize: null, onCreate: null, onJoin: null, onSettings: null, onQuit: null,
                canJoin: true, community: null));
            AssertNoOverlap("MainMenuScreen");
        }

        [Test]
        public void NewGame_HasNoOverlappingSiblings()
        {
            rootVisualElement.Clear();
            rootVisualElement.Add(Guildmaster.UI.NewGameScreenView.Build(
                steamReady: false,   // худший случай: под галочкой лобби ещё и строка «Steam не запущен»
                localize: null, onPick: null, onBack: null));
            AssertNoOverlap("NewGameScreen");
        }

        [Test]
        public void GuildSelect_HasNoOverlappingSiblings()
        {
            var guilds = new List<Guildmaster.UI.GuildSelectScreenView.GuildEntry>();
            for (int i = 0; i < 8; i++)
                guilds.Add(new Guildmaster.UI.GuildSelectScreenView.GuildEntry($"g{i}", $"Гильдия {i + 1}", hasRun: true));

            rootVisualElement.Clear();
            rootVisualElement.Add(Guildmaster.UI.GuildSelectScreenView.Build(
                Uxml("Assets/_Project/UI/Screens/GuildSelectScreen.uxml"),
                guilds,
                slotLimit: 8,        // предел GameConfig: список на пределе не имеет права разъехаться
                localize: null, emblemOf: null, shadeOf: null, onPick: null, onBack: null));
            AssertNoOverlap("GuildSelectScreen");
        }

        [Test]
        public void Hub_HasNoOverlappingSiblings()
        {
            rootVisualElement.Clear();
            rootVisualElement.Add(Guildmaster.UI.HubScreenView.Build(
                Uxml("Assets/_Project/UI/Screens/HubScreen.uxml"),
                guildName: "Гильдия с очень длинным именем, какое игрок вправе себе завести",
                localize: null, onStartRun: null, canStartRun: true,
                stage: (1, 8, "act.1.title"), onLeave: () => { }));
            AssertNoOverlap("HubScreen");
        }

        private VisualTreeAsset Uxml(string path)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.NotNull(themeStyleSheet, $"не найдена тема {ThemeTss}");
            Assert.NotNull(uxml, $"не найден экран {path}");
            return uxml;
        }

        private void AssertNoOverlap(string screen)
        {
            // Без прогона кадра раскладка не посчитана вовсе: worldBound у всех NaN, и
            // проверка пересечений «проходит», ничего не проверив (ложный зелёный, 04.08.2026).
            simulate.FrameUpdate();

            var complaints = new List<string>();
            Inspect(rootVisualElement, complaints);

            Assert.IsEmpty(complaints,
                $"{screen}: элементы налезают друг на друга или вылезают за кромку контейнера. " +
                "Причина почти всегда одна — контейнеру не хватило места, дети сжались (flex-shrink: 1), " +
                "а текст сжатого продолжил рисоваться (overflow: visible).\n" +
                string.Join("\n", complaints));
        }

        /// <summary>
        /// Обойти дерево и собрать претензии.
        /// </summary>
        /// <remarks>
        /// Абсолютный элемент не участвует в СРАВНЕНИЯХ — оправа панели и картуш лежат поверх
        /// содержимого намеренно, в том и смысл абсолюта, — но внутрь него обход заходит. Прежняя
        /// версия пропускала абсолютные вместе с потомками и потому не проверяла НИЧЕГО: корень
        /// каждого экрана (<c>.gm-screen</c>) как раз абсолютный, и обход умирал на первом же шаге.
        /// Тест был зелёным ровно потому, что не смотрел (04.08.2026).
        /// </remarks>
        private static void Inspect(VisualElement parent, List<string> complaints)
        {
            var flow = new List<VisualElement>();
            for (int i = 0; i < parent.childCount; i++)
            {
                VisualElement child = parent[i];
                if (child.resolvedStyle.display == DisplayStyle.None) continue;
                if (child.resolvedStyle.position == Position.Absolute) { Inspect(child, complaints); continue; }
                flow.Add(child);
            }

            bool clips = Clips(parent);

            for (int i = 0; i < flow.Count; i++)
            {
                VisualElement child = flow[i];

                // Схлопнутый в ноль элемент — та самая мина: рисовать ему есть что, а места нет.
                // Ловим ЕГО, а не последствие: пересечение может и не случиться, если сосед пустой.
                if (child.layout.height <= Tolerance && HasText(child))
                    complaints.Add($"  {Desc(child)} сжат до нулевой высоты, но несёт текст — " +
                                   "он нарисуется поверх соседей");

                // Ребёнок ЗА кромкой своего контейнера — главный источник наложений и самый
                // незаметный: сжался контейнер, а дети остались прежними и вылезли на соседа
                // этажом выше. Сиблинги при этом не пересекаются — их-то раскладка развела.
                // Контейнер, который обрезает (ScrollView, overflow: hidden), из проверки выходит:
                // там переполнение — законный режим работы, а не авария.
                if (!Escapes(child.worldBound, parent.worldBound)) { Inspect(child, complaints); continue; }

                if (!clips)
                    complaints.Add($"  {Desc(child)} {Rect(child)} вылез за кромку " +
                                   $"{Desc(parent)} {Rect(parent)} — контейнеру не хватило места, " +
                                   "и содержимое рисуется поверх того, что лежит дальше");

                // Обрезающий контейнер спасает от каши, но не от потери: содержимое за кромкой
                // недостижимо, если добраться до него нечем. Прокрутка — есть чем; глухая коробка с
                // overflow: hidden — нет, и тогда кнопка просто исчезает без единого следа в кадре.
                // Эту аварию видно ХУЖЕ наложения, поэтому ловим её здесь же.
                else if (!Scrolls(parent))
                    complaints.Add($"  {Desc(child)} {Rect(child)} не помещается в " +
                                   $"{Desc(parent)} {Rect(parent)} и обрезан кромкой — " +
                                   "контейнер не прокручивается, значит до содержимого не добраться");

                for (int j = i + 1; j < flow.Count; j++)
                {
                    VisualElement other = flow[j];
                    if (!Overlaps(child.worldBound, other.worldBound)) continue;

                    complaints.Add($"  {Desc(child)} {Rect(child)} налезает на {Desc(other)} {Rect(other)}");
                }

                Inspect(child, complaints);
            }
        }

        /// <summary>Есть ли чем добраться до содержимого за кромкой.</summary>
        private static bool Scrolls(VisualElement element)
        {
            return element is ScrollView
                || element.ClassListContains("unity-scroll-view__content-viewport")
                || element.ClassListContains("unity-scroll-view__content-container");
        }

        /// <summary>Пересечение прямоугольников с допуском: касание кромками — не наложение.</summary>
        private static bool Overlaps(Rect a, Rect b)
        {
            return a.xMin < b.xMax - Tolerance && b.xMin < a.xMax - Tolerance
                && a.yMin < b.yMax - Tolerance && b.yMin < a.yMax - Tolerance;
        }

        /// <summary>Выходит ли <paramref name="child"/> за кромку <paramref name="parent"/>.</summary>
        private static bool Escapes(Rect child, Rect parent)
        {
            return child.yMax > parent.yMax + Tolerance || child.yMin < parent.yMin - Tolerance
                || child.xMax > parent.xMax + Tolerance || child.xMin < parent.xMin - Tolerance;
        }

        /// <summary>
        /// Обрезает ли контейнер содержимое — тогда переполнение законно и наложения не даёт.
        /// </summary>
        /// <remarks>
        /// Спрашиваем НАМЕРЕНИЕ (класс <c>gm-clip</c>), а не вычисленный стиль: <c>resolvedStyle</c>
        /// свойства <c>overflow</c> наружу не отдаёт вовсе. Класс же — единственный владелец обрезки:
        /// он её и объявляет в теме, поэтому расходиться правилу и тесту не с чем.
        /// </remarks>
        private static bool Clips(VisualElement element)
        {
            // ScrollView обрезает своим viewport'ом, но проверка идёт по КАЖДОМУ узлу дерева, и
            // промежуточные узлы прокрутки обрезающими не помечены.
            return element is ScrollView
                || element.ClassListContains(ClipClass)
                || element.ClassListContains(PanelBodyClass)
                || element.ClassListContains("unity-scroll-view__content-viewport")
                || element.ClassListContains("unity-scroll-view__content-container");
        }

        /// <summary>Класс-объявление «здесь переполнение обрезается» (см. тему, <c>components.uss</c>).</summary>
        private const string ClipClass = "gm-clip";

        /// <summary>Тело диалога обрезает по той же причине — предохранитель темы.</summary>
        private const string PanelBodyClass = "gm-panel__body";

        private static bool HasText(VisualElement element)
        {
            if (element is TextElement text) return !string.IsNullOrEmpty(text.text);
            return false;
        }

        private static string Desc(VisualElement element)
        {
            var sb = new StringBuilder();
            sb.Append(element.GetType().Name);
            if (!string.IsNullOrEmpty(element.name)) sb.Append('#').Append(element.name);
            foreach (string cls in element.GetClasses()) { sb.Append('.').Append(cls); break; }
            return sb.ToString();
        }

        private static string Rect(VisualElement element)
        {
            Rect r = element.worldBound;
            return $"[y {r.yMin:0}..{r.yMax:0}]";
        }
    }
}
