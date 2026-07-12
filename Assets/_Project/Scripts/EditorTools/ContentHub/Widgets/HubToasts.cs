using UnityEngine.UIElements;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>
    /// Одиночный toast поверх окна (порт Notify из Bloodlines ThemeEditorWindow, упрощённо). Один активный
    /// за раз; авто-исчезновение с fade. Стили — .gh-toast* в ContentHub.uss.
    /// </summary>
    public sealed class HubToasts
    {
        public enum Kind { Success, Info, Warning, Error }

        private readonly VisualElement _root;
        private Label _current;
        private IVisualElementScheduledItem _fade;
        private IVisualElementScheduledItem _remove;

        public HubToasts(VisualElement root) => _root = root;

        public void Show(string message, Kind kind = Kind.Info, int durationMs = 2400)
        {
            Clear();
            _current = new Label(message) { pickingMode = PickingMode.Ignore };
            _current.AddToClassList("gh-toast");
            _current.AddToClassList("gh-toast--" + kind.ToString().ToLowerInvariant());
            _root.Add(_current);

            int fadeAt = durationMs < 900 ? 900 : durationMs;
            _fade = _current.schedule.Execute(() => _current?.AddToClassList("gh-toast--out")).StartingIn(fadeAt);
            _remove = _current.schedule.Execute(Clear).StartingIn(fadeAt + 420);
        }

        public void Clear()
        {
            _fade?.Pause(); _fade = null;
            _remove?.Pause(); _remove = null;
            _current?.RemoveFromHierarchy();
            _current = null;
        }
    }
}
