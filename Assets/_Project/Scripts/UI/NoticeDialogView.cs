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
            root.AddToClassList("gm-panel--dialog");
            root.AddToClassList(ModifierFor(request.Kind));
            root.pickingMode = PickingMode.Position;

            var title = new Label(Text(localize, request.TitleKey, request.TitleFallback,
                                       DefaultTitleFor(request.Kind)));
            title.AddToClassList("gm-text-title");
            title.AddToClassList("gm-panel__title");
            root.Add(title);

            var body = new Label(Text(localize, request.BodyKey, request.BodyFallback, string.Empty));
            body.AddToClassList("gm-text-body");
            root.Add(body);

            // Последствие — отдельной строкой и только если оно есть: пустая строка оставляет зазор,
            // который читается как недогруженный экран (та же готча, что была в диалоге разрыва).
            if (!string.IsNullOrWhiteSpace(request.Consequence))
            {
                var consequence = new Label(request.Consequence);
                consequence.AddToClassList("gm-text-caption");
                root.Add(consequence);
            }

            // Что сказала система — как есть, без перевода.
            if (!string.IsNullOrWhiteSpace(request.Details))
            {
                var details = new Label(request.Details);
                details.AddToClassList("gm-text-code");
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
                var ok = new Button(() => close?.Invoke())
                {
                    text = Text(localize, "ui.notice.ok", "Понятно", "Понятно"),
                };
                ok.AddToClassList("gm-button");
                row.Add(ok);
                return row;
            }

            for (int i = 0; i < options.Count; i++)
            {
                NoticeOption option = options[i];
                var button = new Button(() => { option.Act?.Invoke(); close?.Invoke(); })
                {
                    text = Text(localize, option.LocKey, option.Fallback, "…"),
                };

                button.AddToClassList("gm-button");
                if (option.Primary) button.AddToClassList("gm-button--primary");
                row.Add(button);
            }

            return row;
        }

        /// <summary>Модификатор панели по виду: подача разная, устройство одно.</summary>
        private static string ModifierFor(NoticeKind kind) => kind switch
        {
            NoticeKind.Warning => "gm-panel--notice-warning",
            NoticeKind.Error   => "gm-panel--notice-error",
            _                  => "gm-panel--notice-info",
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
            root.AddToClassList("gm-panel--dialog");
            root.AddToClassList("gm-panel--busy");
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
