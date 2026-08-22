using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using Guildmaster.UI.Components;
using Guildmaster.UI.Tooltips;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Сборка экрана магазина (план [[act-map-run-loop]] §4 B2) из UXML — общий код для роутера и превью-стенда.
    /// Биндится к <see cref="IShopController"/>: витрина (4 слота, покупка), реролл, панель продажи запаса, золото.
    /// Полный запас (нет места) → тост красными буквами по центру, устойчивый к спаму (единый ярлык, рестарт фейда).
    /// Перерисовка — по <see cref="IShopController.Changed"/>; отписка на снятии экрана.
    /// </summary>
    public static class ShopScreenView
    {
        public static VisualElement Build(
            VisualTreeAsset uxml,
            IShopController shop,
            Func<RelicData, string> nameOf,
            Func<string, string> localize,
            Action onLeave)
        {
            string L(string key, string fallback)
            {
                string v = localize?.Invoke(key);
                return string.IsNullOrEmpty(v) ? fallback : v;
            }

            VisualElement screen = uxml.CloneTree();
            VisualElement root = screen.childCount > 0 ? screen[0] : screen;
            root.pickingMode = PickingMode.Position;

            var title    = root.Q<Label>("shop-title");
            var goldLbl  = root.Q<Label>("shop-gold");
            var reroll   = root.Q<Button>("btn-reroll");
            var shelfBox = root.Q<VisualElement>("shop-shelf");
            var stashBox = root.Q<ScrollView>("shop-stash");
            var leave    = root.Q<Button>("btn-leave");
            var toastBox = screen.Q<VisualElement>("shop-toast");

            if (title != null) title.text = L("ui.shop.title", "Лавка");
            if (leave != null) { leave.text = L("ui.shop.leave", "Уйти"); leave.clicked += () => onLeave?.Invoke(); }

            Label toast = null;
            void ShowNoSpace()
            {
                if (toastBox == null) return;
                if (toast == null)
                {
                    toast = new Label();
                    toast.AddToClassList("gm-text--negative");
                    toast.AddToClassList("gm-shop__toast-label");
                    toastBox.Add(toast);
                }
                toast.text = L("ui.shop.no_space", "Нет места — продай реликвию!");
                toast.style.opacity = 1f;
                toast.experimental.animation.Start(1f, 0f, 1200, (e, v) => e.style.opacity = v);
            }

            void RenderShelf()
            {
                shelfBox?.Clear();
                var shelf = shop.Shelf;
                for (int i = 0; i < shelf.Count; i++)
                {
                    ShopItem item = shelf[i];
                    var card = new VisualElement { focusable = true };
                    card.AddToClassList("gm-card");
                    card.AddToClassList("gm-card--shop");
                    if (item.Sold) card.AddToClassList("gm-card--sold");
                    // Витрина показывает имя и цену; чем реликвия ЯВЛЯЕТСЯ — тем же тултипом, что в награде
                    // и в инвентаре (Трек Т): решение о покупке принимается по киту, а не по цене.
                    card.WithTooltip(TooltipRequest.Relic(item.Relic?.Id));

                    var sprite = new VisualElement();
                    sprite.AddToClassList("gm-card__sprite");
                    if (item.Relic?.Icon != null) sprite.style.backgroundImage = new StyleBackground(item.Relic.Icon);
                    card.Add(sprite);

                    var name = new Label(item.Relic != null ? nameOf(item.Relic) : "—");
                    name.AddToClassList("gm-text-name");
                    name.AddToClassList("gm-card__name");
                    card.Add(name);

                    var price = new Label(item.Sold ? L("ui.shop.sold", "Куплено") : $"{item.Price}");
                    price.AddToClassList("gm-text-body");
                    price.AddToClassList("gm-text--value");
                    price.AddToClassList("gm-shop__price");
                    card.Add(price);

                    var buy = new PlateButton { text = L("ui.shop.buy", "Купить") };
                    buy.AddToClassList("gm-button");
                    // Карточка товара уже узкой средней ступени, и кнопка в ней вылезала за края,
                    // наезжая на соседний товар. Ступень --block — штатный отказ от собственной меры
                    // в пользу контейнера; задавать ширину правилом экрана нельзя (ControlSizeGate).
                    buy.AddToClassList("gm-button--block");
                    int index = i;
                    buy.clicked += () => { if (shop.Buy(index) == ShopBuyOutcome.NoSpace) ShowNoSpace(); };
                    buy.SetEnabled(!item.Sold && shop.Gold >= item.Price);
                    card.Add(buy);

                    shelfBox.Add(card);
                }
            }

            void RenderStash()
            {
                if (stashBox == null) return;
                stashBox.Clear();
                foreach (ShopStashItem st in shop.Stash)
                {
                    var row = new VisualElement { focusable = true };
                    row.AddToClassList("gm-shop__stash-row");

                    var n = new Label(nameOf(st.Relic));
                    n.AddToClassList("gm-text-body");
                    n.WithTooltip(TooltipRequest.Relic(st.Relic?.Id)); // продаём вслепую только по имени — плохо
                    row.Add(n);

                    var sell = new PlateButton { text = L("ui.shop.sell", "Продать") + $" ({st.SellValue})" };
                    sell.AddToClassList("gm-button");
                    RelicData relic = st.Relic;
                    sell.clicked += () => shop.Sell(relic);
                    row.Add(sell);

                    stashBox.Add(row);
                }
            }

            void RenderHeader()
            {
                if (goldLbl != null) goldLbl.text = L("ui.shop.gold", "Золото") + $": {shop.Gold}";
                if (reroll != null)
                {
                    reroll.text = L("ui.shop.reroll", "Обновить") + $" ({shop.RerollCost})";
                    reroll.SetEnabled(shop.Gold >= shop.RerollCost);
                }
            }

            void RenderAll() { RenderHeader(); RenderShelf(); RenderStash(); }

            if (reroll != null) reroll.clicked += () => shop.Reroll();

            Action onChanged = RenderAll;
            shop.Changed += onChanged;
            root.RegisterCallback<DetachFromPanelEvent>(_ => shop.Changed -= onChanged);

            RenderAll();
            return root;
        }
    }
}
