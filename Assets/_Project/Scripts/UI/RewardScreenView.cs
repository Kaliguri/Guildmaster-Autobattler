using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.UI.Components;
using Guildmaster.UI.Tooltips;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка экрана награды (A3) из UXML-шаблона — общий код для живого роутера
    /// (<see cref="MenuRouter"/>) и превью-стенда (<c>UiPreviewCatalog</c>). Разметка/стиль — только
    /// из <c>RewardScreen.uxml</c> + классы дизайн-системы; здесь наполнение витрины карточками-компонентами
    /// (<see cref="RelicCard"/> с анимированным спрайтом от <see cref="RelicCardVisualRig"/>, план 10 §5),
    /// подсветка выбора, тултип при наведении, звук при выборе и проводка кнопок. Состояние выбора —
    /// в замыканиях; результат уходит через <paramref name="onTake"/>/<paramref name="onSkip"/>.
    /// </summary>
    public static class RewardScreenView
    {
        /// <summary>
        /// Построить экран-оверлей награды. <paramref name="nameOf"/> — локализованное имя реликвии,
        /// <paramref name="localize"/> — строка по ключу (null/пусто → RU-фолбэк). <paramref name="onCardSelectSound"/>
        /// — хук звука/voice-стингера при выборе карточки (FMOD-ассет позже; план 10 §5). Кнопка «Взять»
        /// активна, когда выбрана реликвия и (при полном запасе) выбрано, что сбросить.
        /// </summary>
        public static VisualElement Build(
            VisualTreeAsset uxml,
            IReadOnlyList<RelicData> choices,
            bool inventoryFull,
            IReadOnlyList<string> currentInventory,
            Func<RelicData, string> nameOf,
            Func<string, string> localize,
            Action<RelicData, string> onTake,
            Action onSkip,
            Action<RelicData> onCardSelectSound = null,
            GuildmasterPalette palette = null)
        {
            string L(string key, string ru)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? ru : v;
            }

            VisualElement screen = uxml.CloneTree();
            // CloneTree даёт обёртку <UXML>; сам экран-оверлей — её первый ребёнок (gm-screen).
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var title      = root.Q<Label>("reward-title");
            var choicesBox = root.Q<VisualElement>("reward-choices");
            var dropSection = root.Q<VisualElement>("drop-section");
            var dropHint   = root.Q<Label>("drop-hint");
            var dropList   = root.Q<VisualElement>("drop-list");
            var skipBtn    = root.Q<Button>("btn-skip");
            var takeBtn    = root.Q<Button>("btn-take");

            if (title   != null) title.text   = L("ui.reward.title", "Награда — выбери реликвию");
            if (skipBtn != null) skipBtn.text = L("ui.reward.skip", "Пропустить");
            if (takeBtn != null) takeBtn.text = L("ui.reward.take", "Взять");

            // Риг анимированных спрайтов карточек. Живёт, пока открыт экран (dispose при detach).
            // Палитра нужна ему за цветом ступени приглушения — тем же, которым красит бой.
            var rig = new RelicCardVisualRig(palette: palette);
            root.RegisterCallback<DetachFromPanelEvent>(_ => rig.Dispose());

            RelicData chosen = null;
            string    drop   = null;

            var choiceCards = new List<(RelicData relic, RelicCard card, RenderTexture rt)>();
            var dropRows    = new List<(string id, VisualElement row)>();

            void Refresh()
            {
                foreach (var (relic, card, _) in choiceCards)
                    card.Selected = ReferenceEquals(relic, chosen);
                foreach (var (id, r) in dropRows)
                    r.EnableInClassList("gm-reward-drop__row--selected", id == drop);
                bool canTake = chosen != null && (!inventoryFull || drop != null);
                takeBtn?.SetEnabled(canTake);
            }

            // ── Витрина карточек ──
            for (int i = 0; choices != null && i < choices.Count; i++)
            {
                RelicData relic = choices[i];
                var card = new RelicCard { RelicName = nameOf != null ? nameOf(relic) : relic?.Id };
                card.AddToClassList("gm-card--reward");

                // Визуал: анимированный боевой спрайт через риг; нет ViewPrefab → портрет/иконка-фолбэк.
                RenderTexture rt = null;
                if (relic != null && relic.ViewPrefab != null)
                {
                    rt = rig.Acquire(relic);
                    card.SetVisual(rt);
                }
                else
                {
                    card.SetSprite(relic?.Visual != null ? relic.Visual.Portrait : relic?.Icon);
                }

                RelicData r = relic;
                RenderTexture cardRt = rt;
                // Подсказка — запросом по данным (Трек Т): содержимое соберёт общая фабрика, поэтому
                // реликвия в награде, в магазине и в инвентаре выглядит одинаково без копирования кода.
                card.WithTooltip(TooltipRequest.Relic(r?.Id));
                card.RegisterCallback<ClickEvent>(_ =>
                {
                    chosen = r;
                    if (cardRt != null) rig.PlayAttack(cardRt); // выбор → анимация атаки
                    onCardSelectSound?.Invoke(r);               // + звук (FMOD-хук)
                    Refresh();
                });

                choicesBox?.Add(card);
                choiceCards.Add((relic, card, rt));
            }

            // ── Секция сброса (только при полном запасе) ──
            if (inventoryFull && dropSection != null)
            {
                dropSection.style.display = DisplayStyle.Flex;
                if (dropHint != null)
                    dropHint.text = L("ui.reward.drop_hint", "Запас реликвий полон — выбери, что сбросить:");

                for (int i = 0; currentInventory != null && i < currentInventory.Count; i++)
                {
                    string id = currentInventory[i];
                    // Имя берём ключом по id, а не поиском в choices: там лежат ПРЕДЛОЖЕННЫЕ реликвии,
                    // и та, что уже у игрока, в этом списке отсутствует по определению — резолв всегда
                    // промахивался, и в списке «что сбросить» игрок читал сырые id.
                    string label = Coalesce(localize?.Invoke(id + "." + ContentKeys.NameSuffix), id);
                    var r = new Label(label) { focusable = true };
                    r.AddToClassList("gm-reward-drop__row");
                    r.RegisterCallback<ClickEvent>(_ => { drop = id; Refresh(); });
                    dropList?.Add(r);
                    dropRows.Add((id, r));
                }
            }

            if (skipBtn != null) skipBtn.clicked += () => onSkip?.Invoke();
            if (takeBtn != null) takeBtn.clicked += () =>
            {
                if (chosen == null) return;
                onTake?.Invoke(chosen, inventoryFull ? drop : null);
            };

            Refresh();
            return root;
        }

        private static string Coalesce(string a, string b) => string.IsNullOrEmpty(a) ? b : a;
    }
}
