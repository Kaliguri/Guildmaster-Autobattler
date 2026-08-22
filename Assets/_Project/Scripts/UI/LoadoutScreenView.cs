using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.UI.Components;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Экран расстановки реликвий: сетка карточек и панель деталей. Про <c>LoadoutViewModel</c> не
    /// знает ничего — получает список реликвий и отдаёт наружу события наведения и выбора.
    /// </summary>
    /// <remarks>
    /// <b>Почему вид уехал из роутера.</b> Сборка жила приватным методом в <c>MenuRouter</c> и потому
    /// была недоступна стенду превью — а экран, который стенд не может собрать, нельзя и снять
    /// кадром. Правило Макса 23.08.2026: стенд и игра собирают экран одним кодом, иначе кадра нет.
    ///
    /// <para><b>Что осталось владельцу.</b> Тексты деталей и признаки «выбрана / текущая» приходят
    /// снаружи: их знает модель, а экран лишь показывает. Так стенд собирает ту же сетку с теми же
    /// карточками, просто со своими данными.</para>
    /// </remarks>
    public sealed class LoadoutScreenView
    {
        /// <summary>Корень экрана — то, что кладётся в слой.</summary>
        public VisualElement Root { get; private set; }

        /// <summary>Кнопки экрана: принять, сохранить, закрыть.</summary>
        public Button Accept { get; private set; }
        public Button Save { get; private set; }
        public BackButton Close { get; private set; }

        /// <summary>Наведение на карточку — повод показать её детали.</summary>
        public event Action<RelicData> Hovered;

        /// <summary>Клик по карточке — выбор реликвии.</summary>
        public event Action<RelicData> Picked;

        private readonly List<(RelicData Relic, VisualElement Card)> _cards = new();
        private Label _detailName;
        private Label _detailDesc;
        private Label _detailTags;
        private Label _detailStats;

        /// <summary>
        /// Собирает экран и сетку карточек по списку реликвий.
        /// </summary>
        /// <param name="uxml">Разметка экрана.</param>
        /// <param name="relics">Что показать в сетке, в порядке показа.</param>
        /// <param name="nameOf">Подпись карточки: имя реликвии в языке игрока.</param>
        /// <param name="localize">Ключ → строка, для кнопки возврата.</param>
        public static LoadoutScreenView Build(VisualTreeAsset uxml, IReadOnlyList<RelicData> relics,
                                              Func<RelicData, string> nameOf, Func<string, string> localize)
        {
            VisualElement tree = uxml.CloneTree();
            tree.style.position = Position.Absolute;
            tree.style.left = 0;
            tree.style.top = 0;
            tree.style.right = 0;
            tree.style.bottom = 0;

            var view = new LoadoutScreenView
            {
                Root         = tree,
                Accept       = tree.Q<Button>("btn-accept"),
                Save         = tree.Q<Button>("btn-save"),
                Close        = tree.Q<BackButton>("btn-close"),
                _detailName  = tree.Q<Label>("detail-name"),
                _detailDesc  = tree.Q<Label>("detail-desc"),
                _detailTags  = tree.Q<Label>("detail-tags"),
                _detailStats = tree.Q<Label>("detail-stats"),
            };

            var grid = tree.Q<ScrollView>("relic-grid");
            grid.contentContainer.AddToClassList("gm-grid");

            for (int i = 0; i < (relics?.Count ?? 0); i++)
            {
                RelicData relic = relics[i];

                // КОНТРОЛ, а не ручная сборка. До 07.08.2026 здесь построчно повторялся конструктор
                // RelicCard — те же классы, тот же спрайт, та же подпись, — и расхождение уже стоило
                // грида: карточки контрола стали focusable, а собранные тут остались недоступны с
                // клавиатуры. Один владелец сборки: правка контрола доезжает сюда сама.
                var card = new RelicCard { RelicName = nameOf?.Invoke(relic) };
                card.SetSprite(relic.Icon);

                card.RegisterCallback<PointerEnterEvent>(_ => view.Hovered?.Invoke(relic));
                card.RegisterCallback<ClickEvent>(_ => view.Picked?.Invoke(relic));

                grid.Add(card);
                view._cards.Add((relic, card));
            }

            // Заголовок и табы: разметка несёт RU-запас, ключ проводится здесь. До 23.08.2026 они
            // стояли латиницей прямо в UXML («Loadout», «Relic/Items/Upgrades/AI») — мимо ключей и
            // мимо языка игрока.
            Localize(tree.Q<Label>("loadout-title"), "ui.loadout.title", localize);
            Localize(tree.Q<Button>("tab-relic"),    "ui.loadout.tab.relic", localize);
            Localize(tree.Q<Button>("tab-items"),    "ui.loadout.tab.items", localize);
            Localize(tree.Q<Button>("tab-upgrades"), "ui.loadout.tab.upgrades", localize);
            Localize(tree.Q<Button>("tab-ai"),       "ui.loadout.tab.ai", localize);

            // Табы-заглушки (кроме Реликвии) — недоступны (структура на будущее).
            Disable(tree.Q<Button>("tab-items"));
            Disable(tree.Q<Button>("tab-upgrades"));
            Disable(tree.Q<Button>("tab-ai"));

            // Дверь наружу — тот же контрол, что и на прочих экранах: слово, место и вид у возврата
            // одни на всю игру (правило Макса 22.08.2026).
            view.Close?.Localize(localize);
            return view;
        }

        /// <summary>Заполняет панель деталей готовыми строками.</summary>
        public void ShowDetail(string name, string desc, string tags, string stats)
        {
            if (_detailName  != null) _detailName.text  = name;
            if (_detailDesc  != null) _detailDesc.text  = desc;
            if (_detailTags  != null) _detailTags.text  = tags;
            if (_detailStats != null) _detailStats.text = stats;
        }

        /// <summary>
        /// Перекрашивает карточки: какая выбрана и какая надета сейчас.
        /// </summary>
        /// <remarks>
        /// Два признака, а не один: «выбрана» — то, что игрок трогает прямо сейчас, «текущая» — то,
        /// что уже стоит в расстановке. Одно состояние на оба заставило бы экран врать в момент,
        /// когда игрок примеряет замену.
        /// </remarks>
        public void SyncCards(Func<RelicData, bool> isSelected, Func<RelicData, bool> isCurrent)
        {
            foreach ((RelicData relic, VisualElement card) in _cards)
            {
                card.EnableInClassList("gm-card--selected", isSelected?.Invoke(relic) ?? false);
                card.EnableInClassList("gm-card--current", isCurrent?.Invoke(relic) ?? false);
            }
        }

        /// <summary>Первая реликвия сетки — с неё экран открывается, когда выбора ещё нет.</summary>
        public RelicData FirstRelic => _cards.Count > 0 ? _cards[0].Relic : null;

        private static void Disable(Button b) { if (b != null) b.SetEnabled(false); }

        /// <summary>
        /// Ставит перевод, оставляя написанное в разметке как RU-запас.
        /// </summary>
        /// <remarks>
        /// Пустой ответ локализатора означает «перевода нет» — тогда на экране остаётся то, что
        /// лежит в UXML. Затирать запас пустой строкой значило бы получить безымянный элемент там,
        /// где перевод просто не завели.
        /// </remarks>
        private static void Localize(VisualElement element, string key, Func<string, string> localize)
        {
            string value = localize?.Invoke(key);
            if (string.IsNullOrEmpty(value)) return;

            if (element is Button button) button.text = value;
            else if (element is Label label) label.text = value;
        }
    }
}
