using System;
using System.Collections.Generic;
using Guildmaster.Core.Flow;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Окно-предупреждение: что случилось, почему и что сказала система.
    /// </summary>
    /// <remarks>
    /// <b>Собирается кодом, а не из UXML,</b> и это осознанно временно (заказ Макса 09.08.2026:
    /// «Система окон-предупреждений + ВРЕМЕННЫЙ дизайн»). Разметке нужна сериализованная ссылка в
    /// сцене, то есть редактор, — а шов нужен раньше, чем его вид. Классы берутся из темы, поэтому
    /// окно уже выглядит своим, а переезд на UXML не тронет ни одного вызывающего.
    /// <para><b>Вид меняет подпись и модификатор, но не устройство.</b> Отдельный экран на каждый вид
    /// разошёлся бы на первой правке — это уже случалось с диалогами.</para>
    /// </remarks>
    public static class NoticeDialogView
    {
        /// <param name="localize">Ключ → строка; вернуть пусто, если ключа нет.</param>
        /// <param name="close">Закрыть окно — единственный исход, других у сообщения нет.</param>
        public static VisualElement Build(in NoticeRequest request, Func<string, string> localize,
                                          Action close)
        {
            var root = new VisualElement();
            root.AddToClassList("gm-panel");
            // НЕ .gm-panel--dialog: его габариты — мера большого экрана (1280 x 860 минимум), и
            // сообщение в одну строку вырастало в пустой прямоугольник. Своя мера у окна в .gm-notice.
            root.AddToClassList("gm-notice");
            root.AddToClassList(ModifierFor(request.Kind));
            root.pickingMode = PickingMode.Position;

            root.Add(Badge(Text(localize, request.TitleKey, request.TitleFallback,
                                DefaultTitleFor(request.Kind))));

            var body = new Label(Text(localize, request.BodyKey, request.BodyFallback, string.Empty));
            body.AddToClassList("gm-text-body");
            body.AddToClassList("gm-notice__body");
            root.Add(body);

            // Последствие — отдельной строкой и только если оно есть: пустая строка оставляет зазор,
            // который читается как недогруженный экран (та же готча, что была в диалоге разрыва).
            if (!string.IsNullOrWhiteSpace(request.Consequence))
            {
                var consequence = new Label(request.Consequence);
                consequence.AddToClassList("gm-text-caption");
                consequence.AddToClassList("gm-notice__aside");
                root.Add(consequence);
            }

            // Что сказала система — как есть, без перевода.
            if (!string.IsNullOrWhiteSpace(request.Details))
            {
                var details = new Label(request.Details);
                details.AddToClassList("gm-text-code");
                details.AddToClassList("gm-notice__aside");
                root.Add(details);
            }

            root.Add(BuildAnswers(in request, localize, close));
            return root;
        }

        /// <summary>
        /// Ряд ответов. Пустой список — одна кнопка «Понятно»: <b>у окна всегда есть чем ответить</b>,
        /// потому что закрыть его иначе нельзя (решение Макса 09.08.2026: «Пока все требует кнопки»).
        /// </summary>
        /// <remarks>
        /// Действие варианта выполняется ДО снятия окна, а закрывается оно всегда: вариант, забывший
        /// закрыть за собой, оставил бы игрока с висящей модалкой — и это была бы ошибка, которую
        /// каждый заказчик окна допускал бы по-своему.
        /// </remarks>
        private static VisualElement BuildAnswers(in NoticeRequest request,
                                                  Func<string, string> localize, Action close)
        {
            var row = new VisualElement();
            row.AddToClassList("gm-notice__answers");

            IReadOnlyList<NoticeOption> options = request.Options;

            if (options == null || options.Count == 0)
            {
                row.Add(Answer(Text(localize, "ui.notice.ok", "Понятно", "Понятно"),
                               primary: true, act: () => close?.Invoke()));
                return row;
            }

            for (int i = 0; i < options.Count; i++)
            {
                NoticeOption option = options[i];
                row.Add(Answer(Text(localize, option.LocKey, option.Fallback, "…"), option.Primary,
                               () => { option.Act?.Invoke(); close?.Invoke(); }));
            }

            return row;
        }

        /// <summary>
        /// Ярлык вида, висящий НАД верхней кромкой окна: облик A, принят Максом 20.08.2026.
        /// </summary>
        /// <remarks>
        /// Рисуется контролом <see cref="Components.SlantedPanel"/> — тем же, что лента режимов
        /// забега. Скос, а не скругление: радиус в дизайн-системе нулевой, и язык формы у нас один
        /// на весь интерфейс. Своей фаски окно не заводит — иначе способов нарисовать скошенный
        /// торец стало бы два.
        /// <para>Капс подписи — через <see cref="Components.UiTextCase"/> (<c>--gm-text-case</c> в
        /// USS), а не заглавными буквами в ключе локали: регистр здесь свойство роли, и та же
        /// строка «Не получилось» в другом месте обязана остаться как написана. Голому
        /// <see cref="Label"/> привязка нужна явно — свойство читает не он, а контрол.</para>
        /// </remarks>
        private static VisualElement Badge(string text)
        {
            var badge = new Components.SlantedPanel { name = "notice-badge", pickingMode = PickingMode.Ignore };
            badge.AddToClassList("gm-notice__badge");

            var label = new Label(text);
            label.AddToClassList("gm-text-caption");
            label.AddToClassList("gm-notice__badge-label");
            Components.UiTextCase.Bind(label);
            badge.Add(label);

            return badge;
        }

        /// <summary>
        /// Один ответ окна. Собирается КОНТРОЛОМ <see cref="Components.PlateButton"/>, а не голым
        /// <see cref="Button"/> с классом.
        /// </summary>
        /// <remarks>
        /// Разница не косметическая: пластина рисует свою форму через <c>Painter2D</c> и читает
        /// custom-свойства темы, поэтому голая кнопка с теми же классами получала прямоугольник без
        /// фаски и без заливки — то есть ровно ту ловушку, что уже случилась с карточкой реликвии в
        /// гриде лоадаута: разметка одинаковая, а всё, что контрол делает НЕ разметкой, теряется молча.
        /// </remarks>
        private static Components.PlateButton Answer(string text, bool primary, Action act)
        {
            var button = new Components.PlateButton(() => act?.Invoke()) { text = text };
            button.AddToClassList("gm-button");
            if (primary) button.AddToClassList("gm-button--primary");
            return button;
        }

        /// <summary>Модификатор окна по виду: подача разная, устройство одно.</summary>
        private static string ModifierFor(NoticeKind kind) => kind switch
        {
            NoticeKind.Warning => "gm-notice--warning",
            NoticeKind.Error   => "gm-notice--error",
            _                  => "gm-notice--info",
        };

        /// <summary>
        /// Заголовок, когда заказчик его не назвал: у сообщения всегда есть хотя бы вид.
        /// </summary>
        private static string DefaultTitleFor(NoticeKind kind) => kind switch
        {
            NoticeKind.Warning => "Внимание",
            NoticeKind.Error   => "Не получилось",
            _                  => "К сведению",
        };

        private static string Text(Func<string, string> localize, string key, string fallback,
                                   string last)
        {
            if (!string.IsNullOrEmpty(key))
            {
                string translated = localize?.Invoke(key);
                if (!string.IsNullOrEmpty(translated)) return translated;
            }

            return !string.IsNullOrEmpty(fallback) ? fallback : last;
        }
    }

    /// <summary>
    /// Экран ожидания: игра занята, и это видно.
    /// </summary>
    /// <remarks>
    /// Кнопки нет намеренно — снимается он отменой того, кто ждёт (см. <see cref="BusyRequest.Until"/>).
    /// Собирается кодом по той же причине, что и <see cref="NoticeDialogView"/>: временный вид.
    /// </remarks>
    public static class BusyOverlayView
    {
        public static VisualElement Build(in BusyRequest request, Func<string, string> localize)
        {
            var root = new VisualElement();
            root.AddToClassList("gm-panel");
            // Габариты — свои (см. .gm-busy), диалоговые здесь означали бы окно 1280 x 860 под одно
            // слово «Подождите».
            root.AddToClassList("gm-busy");
            root.pickingMode = PickingMode.Position;   // ожидание перехватывает клики: под ним жать нечего

            string text = null;
            if (!string.IsNullOrEmpty(request.TitleKey)) text = localize?.Invoke(request.TitleKey);
            if (string.IsNullOrEmpty(text)) text = request.TitleFallback;
            if (string.IsNullOrEmpty(text)) text = "Подождите";

            var label = new Label(text);
            label.AddToClassList("gm-text-title");
            root.Add(label);

            return root;
        }
    }
}
