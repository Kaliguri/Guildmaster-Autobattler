using System;
using Guildmaster.Guild;
using Guildmaster.UI.Components;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка экрана привала из UXML-шаблона — общий код для живого роутера (<see cref="MenuRouter"/>) и
    /// превью-стенда. Разметка/стиль — только из <c>CampScreen.uxml</c> + классы дизайн-системы; здесь
    /// наполнение действиями и перерисовка по <see cref="CampSession.Changed"/>.
    /// <para>Экран НЕ считает бюджет сам: он читает <see cref="CampSession"/> и зовёт
    /// <see cref="CampSession.TryPerform"/>. Своего счётчика у вью нет — потому и разъезжаться нечему.</para>
    /// </summary>
    public static class CampScreenView
    {
        /// <summary>
        /// Построить экран привала. <paramref name="localize"/> — строка по ключу (null/пусто → RU-фолбэк).
        /// <paramref name="onLeave"/> зовётся, когда отряд уходит («Пройти мимо»).
        /// </summary>
        public static VisualElement Build(
            VisualTreeAsset uxml,
            CampSession session,
            Func<string, string> localize,
            Action onLeave,
            Action<bool> onActionSound = null)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var title   = root.Q<Label>("camp-title");
            var body    = root.Q<Label>("camp-body");
            var budget  = root.Q<Label>("camp-budget");
            var actions = root.Q<VisualElement>("camp-actions");

            if (title != null) title.text = L("ui.camp.title", "Привал");
            if (body  != null) body.text  = L("ui.camp.body",
                "Отряд разбивает лагерь. Времени немного — успеете сделать не всё.");

            // Кнопка на каждое действие; порядок enum = порядок на экране, «Пройти мимо» замыкает список.
            var buttons = new (CampAction Action, Button Button)[CampActions.Length];
            for (int i = 0; i < CampActions.Length; i++)
            {
                CampAction action = CampActions[i];
                var btn = new PlateButton { text = L(LabelKey(action), Fallback(action)) };
                btn.AddToClassList("gm-button");
                btn.clicked += () => OnAction(action);
                actions?.Add(btn);
                buttons[i] = (action, btn);
            }

            void OnAction(CampAction action)
            {
                if (!session.TryPerform(action))
                {
                    // Не по карману: кнопка сознательно остаётся живой (см. Refresh), поэтому отказ должен
                    // хотя бы звучать — иначе нажатие выглядит как проглоченное.
                    onActionSound?.Invoke(false);
                    return;
                }
                onActionSound?.Invoke(true);
                if (action == CampAction.MoveOn) onLeave?.Invoke();
            }

            void Refresh()
            {
                if (budget != null)
                    budget.text = string.Format(L("ui.camp.budget", "Действий осталось: {0} из {1}"),
                                                session.Remaining, session.Budget);

                for (int i = 0; i < buttons.Length; i++)
                {
                    // Бесплатный уход доступен всегда; остальное гаснет ТЕКСТОМ при нехватке бюджета.
                    // Намеренно не SetEnabled(false): кнопка остаётся живой на hover/нажатие — правило
                    // дизайн-системы, недоступность не должна ощущаться как «экран сломался».
                    bool affordable = buttons[i].Action == CampAction.MoveOn || session.CanAfford;
                    buttons[i].Button.EnableInClassList("gm-button--unaffordable", !affordable);
                }
            }

            session.Changed += Refresh;
            root.RegisterCallback<DetachFromPanelEvent>(_ => session.Changed -= Refresh);
            Refresh();

            return root;
        }

        private static readonly CampAction[] CampActions =
        {
            CampAction.Empower,
            CampAction.CopyRelic,
            CampAction.Cleanse,
            CampAction.HireVessel,
            CampAction.MoveOn,
        };

        private static string LabelKey(CampAction action) => action switch
        {
            CampAction.Empower    => "ui.camp.action.empower",
            CampAction.CopyRelic  => "ui.camp.action.copy_relic",
            CampAction.Cleanse    => "ui.camp.action.cleanse",
            CampAction.HireVessel => "ui.camp.action.hire_vessel",
            _                     => "ui.camp.action.move_on",
        };

        private static string Fallback(CampAction action) => action switch
        {
            CampAction.Empower    => "Усилиться",
            CampAction.CopyRelic  => "Получить копию реликвии",
            CampAction.Cleanse    => "Снять негативное последствие",
            // Скобка про замену — ПОЯСНЕНИЕ, и в подпись кнопки оно не помещалось: текст выходил за
            // пластину и рисовался поверх соседей (кадр 23.08.2026). Место пояснению — тултип.
            CampAction.HireVessel => "Нанять Сосуда",
            _                     => "Пройти мимо",
        };
    }
}
