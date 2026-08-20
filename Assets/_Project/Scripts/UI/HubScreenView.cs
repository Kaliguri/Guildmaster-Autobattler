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
        /// <param name="canStartRun">
        /// Есть ли чем голосовать за выход в забег. <b>У обеих ролей — да</b> (вердикт Макса
        /// 08.08.2026): кнопка отправляет голос, а уходит группа, когда сошлись все. Гасим её только
        /// там, где голосовать физически некуда. Кнопка при этом остаётся на месте: пропавшая читается
        /// как «экран не догрузился», погашенная — как «сейчас нельзя».
        /// </param>
        public static VisualElement Build(
            VisualTreeAsset uxml,
            string guildName,
            Func<string, string> localize,
            Action onStartRun,
            bool canStartRun = true)
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
                start.SetEnabled(canStartRun);
                if (canStartRun) start.clicked += () => onStartRun?.Invoke();
            }

            return root;
        }

        /// <summary>
        /// Показать на кнопке, скольких ещё ждём. В одиночку счёт не рисуется: «(1/1)» не сообщает
        /// ничего, а место на кнопке занимает.
        /// </summary>
        /// <remarks>
        /// Отдельный метод, а не параметр сборки: счёт меняется, пока двор открыт, — напарник
        /// соглашается уже после того, как ты нажал. Молчащая кнопка выглядела бы как зависшая, а это
        /// худший вид зависания: причина не видна на экране.
        /// </remarks>
        public static void SetStartCount(VisualElement root, Func<string, string> localize,
                                         int ready, int required, bool locallyReady)
        {
            var button = root?.Q<Button>("btn-start-run");
            if (button == null) return;

            string label = localize?.Invoke("ui.hub.start_run");
            if (string.IsNullOrEmpty(label)) label = "Начать забег";

            button.text = required > 1 ? $"{label} ({ready}/{required})" : label;
            button.EnableInClassList("gm-btn--pending", required > 1 && locallyReady);
        }
    }
}
