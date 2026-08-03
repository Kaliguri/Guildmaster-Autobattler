using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка boot/loading-экрана: лого-lockup (печать + «Happy Guildmasters»), ряд атрибуций внизу,
    /// «Загрузка» + вращающийся знак справа. ЛЮБАЯ клавиша или клик закрывают экран с плавным уходом
    /// (переход в главное меню). Реф — экран загрузки Ravenswatch (временное оформление).
    /// <para>Спиннер декоративен: загрузка мира (<c>GameBootstrap.BootAsync</c>) проходит ДО показа,
    /// поэтому настоящего прогресса тут нет — знак крутится, пока игрок не нажмёт.</para>
    /// </summary>
    public static class TitleCardScreenView
    {
        // Совпадает с transition-duration класса .gm-boot--out в components.uss: сперва гаснем, потом
        // отдаём управление — главное меню строится уже под погасшим экраном, без рывка.
        private const long FadeOutMs = 350;

        public static VisualElement Build(
            VisualTreeAsset uxml,
            Sprite seal,
            Func<string, string> localize,
            Action onDismiss)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var sealEl = root.Q<VisualElement>("titlecard-seal");
            var title = root.Q<Label>("titlecard-title");
            var hint = root.Q<Label>("titlecard-hint");
            var loading = root.Q<Label>("boot-loading-label");
            var legal = root.Q<Label>("boot-legal");
            var spinner = root.Q<VisualElement>("boot-spinner");

            if (sealEl != null && seal != null)
                sealEl.style.backgroundImage = new StyleBackground(seal);

            if (title != null) title.text = L("ui.boot.title", "Happy Guildmasters");
            if (hint != null) hint.text = L("ui.boot.hint", "нажмите любую клавишу");
            if (loading != null) loading.text = L("ui.boot.loading", "Загрузка");
            // Строка прав: «FMOD» и «Firelight Technologies Pty Ltd.» — требование Clause 3 их лицензии,
            // логотип в ряду его не закрывает. Текст один и тот же в EN и RU: имена компаний и год не
            // переводятся, но ключ всё равно свой — иначе строку нельзя тронуть, не тронув код.
            if (legal != null)
                legal.text = L("ui.boot.legal",
                    "© 2026 Alebardium  ·  FMOD Studio by Firelight Technologies Pty Ltd.");

            // Спиннер: непрерывное вращение кольца (форму рисует USS рамкой, картинки нет). 2° за кадр
            // при 16 мс — оборот за 0.8 с; schedule сам умирает вместе с элементом при закрытии панели.
            if (spinner != null)
            {
                float angle = 0f;
                spinner.schedule.Execute(() =>
                {
                    angle = (angle + 2f) % 360f;
                    spinner.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));
                }).Every(16);
            }

            bool dismissed = false;
            void Dismiss()
            {
                if (dismissed) return;
                dismissed = true;
                // Гаснем классом (transition живёт в USS), а управление отдаём после ухода: меню
                // появится уже под чёрным кадром, а не выпрыгнет на месте титула.
                root.AddToClassList("gm-boot--out");
                root.schedule.Execute(() => onDismiss?.Invoke()).StartingIn(FadeOutMs);
            }

            // Закрывает ЛЮБОЙ ввод: клик, тап и любая клавиша. Клавиши приходят только в фокус —
            // поэтому берём фокус, как только элемент попал на панель.
            root.RegisterCallback<PointerDownEvent>(_ => Dismiss());
            root.RegisterCallback<ClickEvent>(_ => Dismiss());
            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(_ => Dismiss());
            root.RegisterCallback<AttachToPanelEvent>(_ => root.Focus());

            return root;
        }
    }
}
