using System;
using System.Collections.Generic;
using Guildmaster.UI.Components;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Глобальная панель забега (app-shell, редизайн 2026-07-20): всегда сверху во время забега, тело
    /// экранов сдвинуто под неё. Слева гильдия + возвышение + акт + веха одной строкой; центр — лента-островок
    /// из чипов-режимов (тот же <see cref="Chip"/>, что фильтры/теги, в режиме «скрытый»: иконка всегда,
    /// подпись — только у активного) и отделённые настройки; справа — капсулы золота и перезапусков.
    /// Время забега выключено (узел жив как шов). Разметка/стиль — только из <c>RunModeBar.uxml</c> + дизайн-система.
    /// </summary>
    public sealed class RunModeBarView
    {
        public VisualElement Root { get; }

        /// <summary>Теги режимов, объявленных, но ещё не построенных: экранов за ними нет, чипы погашены.</summary>
        private const string TacticsModeTag    = "tactics";
        private const string CompendiumModeTag = "compendium";

        /// <summary>Класс «недоступно, но живо» (см. <c>components.uss</c>, правило ui-feedback).</summary>
        private const string MutedClass = "gm-chip--muted";

        private readonly Label  _gold;
        private readonly Label  _act;
        private readonly Label  _floor;
        private readonly Label  _guildName;
        private readonly Label  _guildAsc;
        private readonly Label  _battleTimer;
        private readonly Label  _restarts;
        private readonly Button _start;
        private readonly Func<string, string> _loc;
        private readonly Dictionary<string, Chip> _modes = new();
        private readonly HashSet<string> _disabledModes = new();
        private readonly string _guildNameBeforeProving; // что стояло в лейбле гильдии до подмены на «Ристалище»

        public RunModeBarView(
            VisualTreeAsset uxml,
            Func<string, string> localize,
            Action onMap, Action onBattle, Action onInventory,
            Action onMenu, Action onStart)
        {
            _loc = localize;

            VisualElement tree = uxml.CloneTree();
            Root = tree.childCount > 0 ? tree[0] : tree;

            _gold        = Root.Q<Label>("topbar-gold");
            _act         = Root.Q<Label>("topbar-act");
            _floor       = Root.Q<Label>("topbar-floor");
            _guildName   = Root.Q<Label>("guild-name");
            _guildAsc    = Root.Q<Label>("guild-asc");
            _guildNameBeforeProving = _guildName != null ? _guildName.text : string.Empty;
            _battleTimer = Root.Q<Label>("battle-timer");
            _restarts    = Root.Q<Label>("topbar-hp");
            _start       = Root.Q<Button>("btn-start");

            // Ключ = тег режима (UiScreen.*): он же имя чипа mode-<key> в разметке, он же то,
            // что придёт в SetActiveMode. Одна строка на все три роли — иначе таб гаснет молча.
            WireMode(UiScreen.MapModeTag,       "ui.mode.map",       "Карта",     onMap);
            WireMode(UiScreen.BattleModeTag,    "ui.mode.battle",    "Бой",       onBattle);
            WireMode(UiScreen.InventoryModeTag, "ui.mode.inventory", "Инвентарь", onInventory);

            // Тактика и Компендиум — заявленные режимы (роадмап, слой AI-профилей), экранов за ними пока
            // нет. Держим их в ленте ПОГАШЕННЫМИ, а не убираем: место в ряду режимов у них своё, и игроку
            // честнее видеть «будет здесь», чем не подозревать об этом вовсе. Гашение — по HARD-правилу
            // ui-feedback: подпись и иконка тускнеют, но чип живой, наведение и нажатие он принимает.
            // Прежде они висели рабочими на вид с пустым обработчиком — вот это и было враньём.
            WireMode(TacticsModeTag,    "ui.mode.tactics",    "Тактика",    null);
            WireMode(CompendiumModeTag, "ui.mode.compendium", "Компендиум", null);

            var menu = Root.Q<Chip>("btn-menu");
            if (menu != null)
            {
                menu.Text = L("ui.mode.menu", "Меню");   // виден лишь если станет активным (обычно нет — это модалка)
                menu.RegisterCallback<ClickEvent>(_ => onMenu?.Invoke());
            }

            // Подпись — в дочернем Label (см. RunModeBar.uxml): у Button с детьми собственный text
            // рисуется поверх содержимого и наезжает на иконку боя.
            if (_start != null)
            {
                SetText(_start.Q<Label>("btn-start-label"), L("ui.run.start", "Начать"));
                _start.clicked += () => onStart?.Invoke();
            }
        }

        /// <summary>
        /// Показать, скольких ещё ждём. В одиночку счёт не рисуется вовсе: «(1/1)» ничего не сообщает, а
        /// place на кнопке занимает.
        /// </summary>
        /// <remarks>
        /// Подпись меняется, а не появляется рядом: кнопка одна, и второй элемент рядом с ней читался бы
        /// как отдельная сущность. Своё нажатие видно по состоянию кнопки — подтвердивший видит, что ждут
        /// уже не его.
        /// </remarks>
        public void SetReadyCount(int ready, int required, bool locallyReady)
        {
            if (_start == null) return;

            string caption = required > 1
                ? $"{L("ui.run.start", "Начать")} ({ready}/{required})"
                : L("ui.run.start", "Начать");

            SetText(_start.Q<Label>("btn-start-label"), caption);
            _start.EnableInClassList("gm-btn--pending", required > 1 && locallyReady);
        }

        /// <summary>
        /// Завести таб режима. <paramref name="action"/> = null означает «режим объявлен, экрана ещё нет»:
        /// чип встаёт в ленту погашенным (<c>gm-chip--muted</c>), но остаётся живым — по HARD-правилу
        /// ui-feedback недоступное гаснет видом, а не выключением. В радио-набор активных режимов такой
        /// чип не входит: подсвечивать нечего, пока он никуда не ведёт.
        /// </summary>
        private void WireMode(string key, string locKey, string ru, Action action)
        {
            var chip = Root.Q<Chip>("mode-" + key);
            if (chip == null) return;
            chip.Text = L(locKey, ru);   // подпись скрыта режимом --collapsible, всплывает у активного таба

            if (action == null)
            {
                chip.AddToClassList(MutedClass);
                return;
            }

            // Проверка «выключен ли режим» — ВНУТРИ обработчика, а не при разводке: набор доступных
            // режимов зависит от того, где игрок (на Ристалище карты нет), и меняется на лету.
            chip.RegisterCallback<ClickEvent>(_ => { if (!_disabledModes.Contains(key)) action.Invoke(); });
            _modes[key] = chip;
        }

        /// <summary>
        /// Включить/выключить режим на лету (Ристалище гасит «Карту» — идти по акту оттуда некуда).
        /// Гашение — по HARD-правилу ui-feedback: таб остаётся в ленте и живым на вид, но тускнеет и
        /// никуда не ведёт. Убрать его совсем нельзя: лента режимов одна на всю игру, и «пропавший»
        /// таб читался бы как сбой, а не как «здесь недоступно».
        /// </summary>
        public void SetModeEnabled(string key, bool enabled)
        {
            if (!_modes.TryGetValue(key, out Chip chip)) return;

            if (enabled) _disabledModes.Remove(key);
            else         _disabledModes.Add(key);

            chip.EnableInClassList(MutedClass, !enabled);
        }

        /// <summary>
        /// Переписать панель под Ристалище: слева имя площадки вместо гильдии, акта и вехи, справа —
        /// без золота и перезапусков. Всё это — величины ЗАБЕГА, а на площадке их не существует;
        /// показывать нули или последние значения значило бы врать игроку о том, где он находится.
        /// </summary>
        /// <param name="on">true — вид площадки; false — обычный вид забега.</param>
        public void SetProvingGroundsMode(bool on)
        {
            // Имя гильдии — ДАННЫЕ забега, а не строка интерфейса: локализовать его нечем, поэтому
            // возвращаем ровно то, что стояло в лейбле до подмены (сейчас — из разметки, позже — из RunState).
            SetText(_guildName, on ? L("ui.mode.proving_grounds", "Ристалище") : _guildNameBeforeProving);
            Show(_guildAsc, !on);
            Show(_act, !on);
            Show(_floor, !on);

            // Капсулы гасим ПОШТУЧНО, а сам правый контейнер оставляем на месте. Он не украшение: у
            // сторон панели flex-grow: 1 и flex-basis: 0, то есть лента режимов центрируется ИМИ.
            // Убери контейнер — и вся лента уезжает вбок (наход. Макса 2026-07-27).
            VisualElement right = Root.Q<VisualElement>(className: "gm-loadout__topbar-side--right");
            if (right != null)
                for (int i = 0; i < right.childCount; i++) Show(right[i], !on);

            SetModeEnabled(UiScreen.MapModeTag, !on);
        }

        private static void Show(VisualElement element, bool visible)
        {
            if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Подсветить активный режим (у него же появляется подпись). null — снять со всех.</summary>
        public void SetActiveMode(string key)
        {
            foreach (var kv in _modes)
                kv.Value.SetActive(kv.Key == key);
        }

        /// <summary>
        /// Держать таб настроек нажатым, пока открыто системное меню (наход. Макса, раунд 2, п.6).
        /// Настройки — не режим забега, поэтому в общий радио-набор <see cref="SetActiveMode"/> не входят:
        /// у них своё состояние, иначе открытое меню сбивало бы подсветку текущего режима.
        /// </summary>
        public void SetMenuActive(bool on)
        {
            var menu = Root.Q<Chip>("btn-menu");
            menu?.SetActive(on);
        }

        public void SetGold(int gold) => SetText(_gold, gold.ToString());
        public void SetAct(int actNumber) => SetText(_act, "· " + L("ui.run.act", "Акт") + " " + actNumber);

        /// <summary>
        /// Как далеко отряд ушёл по карте акта. В домене это <c>MapNode.Floor</c> (индекс колонки),
        /// в интерфейсе — «Веха»: у нас поход по карте, а не подъём по башне, и «этаж» тут врал бы игроку.
        /// </summary>
        public void SetFloor(int floorNumber, int floorCount) =>
            SetText(_floor, "· " + L("ui.run.floor", "Веха") + " " + floorNumber
                          + (floorCount > 0 ? "/" + floorCount : string.Empty));

        // Времени забега здесь нет (реш. 2026-07-20 — не показываем). Прежде лейбл был скрыт классом,
        // а бутстрап всё равно копил секунды каждый кадр и писал их в невидимый узел; вернуть счётчик
        // дешевле, чем держать его работающим вхолостую (аудит 2026-07-26, волна 2).

        /// <summary>ХП забега = перезапуски-на-акт (реш. №65): показываем компактным счётчиком «остаток/максимум».</summary>
        public void SetRestarts(int remaining, int max) => SetText(_restarts, remaining + "/" + max);

        /// <summary>Нет боя (карта/магазин): ни «Начать», ни таймера боя.</summary>
        public void HideBattleCenter()
        {
            if (_start != null)       _start.style.display       = DisplayStyle.None;
            if (_battleTimer != null) _battleTimer.style.display = DisplayStyle.None;
        }

        /// <summary>Бой идёт → таймер боя вместо «Начать»; расстановка → «Начать» вместо таймера.</summary>
        public void SetFighting(bool fighting, string battleTime)
        {
            if (_start != null)       _start.style.display       = fighting ? DisplayStyle.None : DisplayStyle.Flex;
            if (_battleTimer != null)
            {
                _battleTimer.style.display = fighting ? DisplayStyle.Flex : DisplayStyle.None;
                _battleTimer.text = battleTime;
            }
        }

        private string L(string key, string ru)
        {
            string v = _loc?.Invoke(key);
            return string.IsNullOrEmpty(v) ? ru : v;
        }

        private static void SetText(Label label, string text)
        {
            if (label != null) label.text = text;
        }
    }
}
