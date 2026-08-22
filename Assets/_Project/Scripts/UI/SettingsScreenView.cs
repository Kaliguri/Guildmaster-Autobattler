using System;
using Guildmaster.UI.Components;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Экран настроек: собирает дерево, подписывает строки и заводит табы. Про <c>SettingsViewModel</c>
    /// не знает ничего — значения и поведение вешает владелец.
    /// </summary>
    /// <remarks>
    /// <b>Почему вид уехал из роутера.</b> Сборка жила приватным методом в <c>MenuRouter</c> (132
    /// строки), поэтому стенд превью не мог её позвать и пересобирал экран заново — а кадр такого
    /// экрана показывает стенд, а не игру. Правило Макса 23.08.2026: стенд и игра собирают экран
    /// одним кодом, иначе кадра нет.
    ///
    /// <para><b>Граница ровно там, где VM.</b> Здесь всё, что можно увидеть на снимке: дерево,
    /// подписи, состав табов, растяжка по слою. Синхронизация с моделью, подписки на изменения и
    /// кнопки — у владельца: экран, который сам лезет в VM, в изоляции уже не собрать, а именно
    /// изоляция и даёт кадр.</para>
    ///
    /// <para><b>Растяжка внутри, а не снаружи.</b> Экран занимает весь слой, и это часть его вида:
    /// собранный без растяжки, он лёг бы в кадр другим размером, чем в игре.</para>
    /// </remarks>
    public sealed class SettingsScreenView
    {
        /// <summary>Корень экрана — то, что кладётся в слой.</summary>
        public VisualElement Root { get; private set; }

        /// <summary>Громкости: общая, музыка, звук.</summary>
        public SliderRow Master { get; private set; }
        public SliderRow Music { get; private set; }
        public SliderRow Sfx { get; private set; }

        /// <summary>Тумблеры страницы «Игра».</summary>
        public ToggleRow CardAnimations { get; private set; }
        public ToggleRow CardAttack { get; private set; }
        public ToggleRow TooltipDetails { get; private set; }

        /// <summary>Списки страницы «Графика». Наполняет владелец: состав зависит от дисплея.</summary>
        public SelectRow WindowMode { get; private set; }
        public SelectRow Resolution { get; private set; }
        public SelectRow RefreshRate { get; private set; }

        /// <summary>Кнопки экрана.</summary>
        public BackButton Leave { get; private set; }
        public Button Save { get; private set; }
        public Button Defaults { get; private set; }

        private Label _videoHint;

        /// <summary>
        /// Собирает экран настроек со всеми подписями.
        /// </summary>
        /// <param name="uxml">Разметка экрана.</param>
        /// <param name="localize">Ключ → строка; пустой ответ означает «нет перевода», берётся RU-запас.</param>
        public static SettingsScreenView Build(VisualTreeAsset uxml, Func<string, string> localize)
        {
            string L(string key, string ru)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? ru : v;
            }

            VisualElement tree = uxml.CloneTree();
            tree.style.position = Position.Absolute;
            tree.style.left = 0;
            tree.style.top = 0;
            tree.style.right = 0;
            tree.style.bottom = 0;

            var view = new SettingsScreenView
            {
                Root           = tree,
                Master         = tree.Q<SliderRow>("row-master"),
                Music          = tree.Q<SliderRow>("row-music"),
                Sfx            = tree.Q<SliderRow>("row-sfx"),
                CardAnimations = tree.Q<ToggleRow>("toggle-card-anim"),
                CardAttack     = tree.Q<ToggleRow>("toggle-card-attack"),
                TooltipDetails = tree.Q<ToggleRow>("toggle-tooltip-details"),
                WindowMode     = tree.Q<SelectRow>("row-window-mode"),
                Resolution     = tree.Q<SelectRow>("row-resolution"),
                RefreshRate    = tree.Q<SelectRow>("row-refresh-rate"),
                Leave          = tree.Q<BackButton>("btn-cancel"),
                Save           = tree.Q<Button>("btn-save"),
                Defaults       = tree.Q<Button>("btn-defaults"),
                _videoHint     = tree.Q<Label>("video-hint"),
            };

            if (view.Master != null) view.Master.LabelText = L("ui.settings.volume_master", "Общий");
            if (view.Music  != null) view.Music.LabelText  = L("ui.settings.volume_music", "Музыка");
            if (view.Sfx    != null) view.Sfx.LabelText    = L("ui.settings.volume_sfx", "Звук");

            if (view.CardAnimations != null)
                view.CardAnimations.LabelText = L("ui.settings.card_anim", "Анимация карточек");
            if (view.CardAttack != null)
                view.CardAttack.LabelText = L("ui.settings.card_attack", "Анимация атаки карточек");
            // §II.10.4: галка «всегда подробно». Shift при ней работает наоборот — временно даёт краткий вид.
            if (view.TooltipDetails != null)
                view.TooltipDetails.LabelText = L("ui.settings.tooltip_details", "Всегда подробные подсказки");

            if (view.WindowMode  != null) view.WindowMode.LabelText  = L("ui.settings.window_mode", "Режим окна");
            if (view.Resolution  != null) view.Resolution.LabelText  = L("ui.settings.resolution", "Разрешение");
            if (view.RefreshRate != null) view.RefreshRate.LabelText = L("ui.settings.refresh_rate", "Частота обновления");

            view.Leave?.Localize(localize);

            WireTabs(tree);
            return view;
        }

        /// <summary>
        /// Подсказка страницы «Графика»: пустая строка её прячет.
        /// </summary>
        /// <remarks>
        /// Метод, а не голая ссылка на <see cref="Label"/>: видимость держит класс
        /// <c>gm-tab-page--hidden</c>, и это знание вида — владельцу достаточно сказать, что показать.
        /// </remarks>
        public void ShowVideoHint(string text)
        {
            if (_videoHint == null) return;

            _videoHint.text = text ?? string.Empty;
            _videoHint.EnableInClassList("gm-tab-page--hidden", string.IsNullOrEmpty(text));
        }

        /// <summary>
        /// Переключение страниц настроек: активный таб и его страница показаны, остальные скрыты.
        /// </summary>
        private static void WireTabs(VisualElement screen)
        {
            var tabGame  = screen.Q<Button>("tab-game");
            var tabVideo = screen.Q<Button>("tab-video");
            var tabAudio = screen.Q<Button>("tab-audio");
            var pageGame  = screen.Q<VisualElement>("page-game");
            var pageVideo = screen.Q<VisualElement>("page-video");
            var pageAudio = screen.Q<VisualElement>("page-audio");
            if (tabGame == null || tabVideo == null || tabAudio == null) return;

            void Show(Button tab, VisualElement page)
            {
                tabGame.EnableInClassList("gm-tab--active", tab == tabGame);
                tabVideo.EnableInClassList("gm-tab--active", tab == tabVideo);
                tabAudio.EnableInClassList("gm-tab--active", tab == tabAudio);
                pageGame?.EnableInClassList("gm-tab-page--hidden", page != pageGame);
                pageVideo?.EnableInClassList("gm-tab-page--hidden", page != pageVideo);
                pageAudio?.EnableInClassList("gm-tab-page--hidden", page != pageAudio);
            }

            tabGame.clicked  += () => Show(tabGame, pageGame);
            tabVideo.clicked += () => Show(tabVideo, pageVideo);
            tabAudio.clicked += () => Show(tabAudio, pageAudio);
        }
    }
}
