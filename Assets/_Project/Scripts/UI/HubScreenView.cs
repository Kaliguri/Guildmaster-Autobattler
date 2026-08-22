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
        /// <param name="stage">Где стоит забег: номер акта, ступень маршрута и ключ имени акта.</param>
        /// <param name="onLeave">Уйти со двора. <c>null</c> — уходить некуда, и двери не будет.</param>
        public static VisualElement Build(
            VisualTreeAsset uxml,
            string guildName,
            Func<string, string> localize,
            Action onStartRun,
            bool canStartRun,
            (int Act, int Level, string TitleKey) stage = default,
            Action onLeave = null)
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
            var where = root.Q<Label>("hub-stage");
            var leave = root.Q<Components.BackButton>("btn-leave");
            var start = root.Q<Button>("btn-start-run");

            // Титул — имя дома, а не слово «Двор»: игрок вернулся к СВОЕЙ гильдии, и первым делом
            // должен узнать её. Имени нет (дом только что заведён) — говорим общим словом.
            if (title != null)
                title.text = string.IsNullOrEmpty(guildName)
                    ? L("ui.hub.title", "Двор гильдии")
                    : guildName;

            if (stub != null) stub.text = L("ui.hub.stub", "Двор ещё обустраивают");

            // ГДЕ СТОИТ ЗАБЕГ. Строка собирается здесь, а не приезжает готовой: имя акта приходит
            // ключом, и переводит его каждый у себя (иначе гость с другим языком читал бы чужую локаль).
            if (where != null) where.text = StageLine(stage, localize, L);

            if (leave != null)
            {
                leave.Localize(localize);
                if (onLeave != null) leave.clicked += () => onLeave.Invoke();
                else                 leave.style.display = DisplayStyle.None;
            }

            if (start != null)
            {
                start.text = L("ui.hub.start_run", "Начать забег");
                start.SetEnabled(canStartRun);
                if (canStartRun) start.clicked += () => onStartRun?.Invoke();
            }

            return root;
        }

        /// <summary>
        /// «Акт II — Пепельный тракт · Уровень 8». Имени нет — остаётся «Акт II · Уровень 8»; нет и
        /// акта (двор открыт вне забега) — строка пустая, и место под неё не занимается.
        /// </summary>
        /// <remarks>
        /// «Уровень», а не «этаж» и не «ступень» — слово Макса 22.08.2026: «можем мб назвать то как
        /// далеко прошли - просто "уровнями"? Просто и понятно. Как этажи в STS». Совпадение с
        /// «Уровнем Сосуда» он признал безобидным: «Ясно что и чей уровень и так».
        /// </remarks>
        private static string StageLine((int Act, int Level, string TitleKey) stage,
                                        Func<string, string> localize,
                                        Func<string, string, string> L)
        {
            if (stage.Act <= 0) return string.Empty;

            string act = string.Format(L("ui.hub.act", "Акт {0}"), Roman(stage.Act));

            string name = string.IsNullOrEmpty(stage.TitleKey) ? null : localize?.Invoke(stage.TitleKey);
            if (!string.IsNullOrEmpty(name)) act += " — " + name;

            if (stage.Level <= 0) return act;
            return act + " · " + string.Format(L("ui.hub.level", "Уровень {0}"), stage.Level);
        }

        /// <summary>Номер акта римской цифрой. Актов у нас единицы, поэтому и таблица короткая.</summary>
        private static string Roman(int number)
        {
            string[] digits = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            return number > 0 && number < digits.Length ? digits[number] : number.ToString();
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
