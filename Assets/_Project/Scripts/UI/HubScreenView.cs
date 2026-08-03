using System;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Двор гильдии — дом, из которого уходят в забег. Пока заглушка: имя дома, честная метка
    /// «в работе» и единственная дверь наружу.
    /// </summary>
    /// <remarks>
    /// <b>Заглушка объявляет себя вслух</b> и не притворяется готовым экраном: пустой двор с одной
    /// кнопкой без метки читался бы как поломка, а не как «сюда ещё не завезли». Содержимое — ростер,
    /// найм, лавка, занятия — придёт по ГДД [[guild-hub-courtyard]].
    /// </remarks>
    public static class HubScreenView
    {
        public static VisualElement Build(
            VisualTreeAsset uxml,
            string guildName,
            Func<string, string> localize,
            Action onStartRun)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var title = root.Q<Label>("hub-title");
            var stub  = root.Q<Label>("hub-stub");
            var start = root.Q<Button>("btn-start-run");

            // Титул — имя дома, а не слово «Двор»: игрок вернулся к СВОЕЙ гильдии, и первым делом
            // должен узнать её. Имени нет (дом только что заведён) — говорим общим словом.
            if (title != null)
                title.text = string.IsNullOrEmpty(guildName)
                    ? L("ui.hub.title", "Двор гильдии")
                    : guildName;

            if (stub != null) stub.text = L("ui.hub.stub", "IN PROGRESS");

            if (start != null)
            {
                start.text = L("ui.hub.start_run", "Начать забег");
                start.clicked += () => onStartRun?.Invoke();
            }

            return root;
        }
    }
}
