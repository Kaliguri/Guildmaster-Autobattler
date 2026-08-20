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

        /// <summary>Как долго подсказка гаснет и разгорается — совпадает с transition в теме.</summary>
        /// <remarks>
        /// Быстрее читается тревогой, а не ожиданием. Ведёт пульсацию код, потому что keyframes в
        /// UI Toolkit нет, а `transition` сам себя не перезапускает: он срабатывает на смену
        /// значения, и вернуть значение обратно должен кто-то извне.
        /// </remarks>
        private const long HintPulseMs = 1600;

        public static VisualElement Build(
            VisualTreeAsset uxml,
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

            var hint = root.Q<Label>("titlecard-hint");
            var loading = root.Q<Label>("boot-loading-label");
            var legal = root.Q<Label>("boot-legal");
            var spinner = root.Q<VisualElement>("boot-spinner");

            // Название игры здесь НЕ ставится: его несёт сам контрол вывески своими дефолтами.
            // «Happy Guildmasters» — имя собственное, одинаковое на всех языках, и ключ под него был
            // бы ключом без перевода. Меню переопределяет тексты своими ключами — там они заведены
            // исторически и трогать их незачем.
            if (hint != null) hint.text = L("ui.boot.hint", "нажмите любую клавишу");
            if (loading != null) loading.text = L("ui.boot.loading", "Загрузка");
            // Строка прав: «FMOD» и «Firelight Technologies Pty Ltd.» — требование Clause 3 их лицензии,
            // логотип в ряду его не закрывает. Текст один и тот же в EN и RU: имена компаний и год не
            // переводятся, но ключ всё равно свой — иначе строку нельзя тронуть, не тронув код.
            if (legal != null)
                legal.text = L("ui.boot.legal",
                    "© 2026 Alebardium  ·  FMOD Studio by Firelight Technologies Pty Ltd.");

            // Спиннер: кольцо с дугой в четверть (форму рисует USS рамкой, картинки нет).
            //
            // ХОД НЕРАВНОМЕРНЫЙ — разгон и торможение внутри каждого оборота (правка Макса
            // 07.08.2026: «более feel анимация загрузки»). Линейное вращение механично: оно ничем не
            // отличается от крутящейся шестерёнки и читается как «процесс идёт», а не как «игра
            // жива». Скорость гуляет по синусу от четверти до полутора базовых, оборот в среднем
            // за 0.9 с: дуга то догоняет саму себя, то отпускает — и глаз читает в этом усилие.
            //
            // Фаза считается от НАКОПЛЕННОГО угла, а не от времени: тогда рывок скорости всегда
            // приходится на одно и то же место оборота, и движение не плывёт между запусками.
            if (spinner != null)
            {
                float angle = 0f;
                spinner.schedule.Execute(() =>
                {
                    float eased = 0.25f + 0.75f * (1f + Mathf.Sin(angle * Mathf.Deg2Rad * 2f)) * 0.5f;
                    angle = (angle + 4f * eased) % 360f;
                    spinner.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));
                }).Every(16);
            }

            // Пульсация подсказки: класс снимается и вешается по таймеру, а плавность даёт transition
            // из темы. Дыхание, а не мигание: строка не гаснет полностью, иначе в нижней точке экран
            // выглядит зависшим.
            if (hint != null)
            {
                bool dim = false;
                hint.schedule.Execute(() =>
                {
                    dim = !dim;
                    hint.EnableInClassList("gm-boot__hint--dim", dim);
                }).Every(HintPulseMs);
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
