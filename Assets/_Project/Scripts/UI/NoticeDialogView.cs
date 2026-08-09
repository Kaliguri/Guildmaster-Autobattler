using System;
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

            // Что сказала система — как есть. Пустой строки быть не должно: зазор под телом читается
            // как недогруженный экран (та же готча, что в диалоге разрыва).
            if (!string.IsNullOrWhiteSpace(request.Details))
            {
                var details = new Label(request.Details);
                details.AddToClassList("gm-text-code");
                root.Add(details);
            }

            var ok = new Button(() => close?.Invoke())
            {
                text = Text(localize, "ui.notice.ok", "Понятно", "Понятно"),
            };
            ok.AddToClassList("gm-button");
            root.Add(ok);

            return root;
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
