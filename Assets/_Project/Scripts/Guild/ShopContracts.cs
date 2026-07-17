using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Guild
{
    /// <summary>Слот витрины магазина: реликвия + её цена + признак «уже куплено» (слот пуст).</summary>
    public sealed class ShopItem
    {
        public RelicData Relic;
        public int       Price;
        public bool      Sold;
    }

    /// <summary>Строка панели продажи: реликвия из запаса игрока + сколько за неё дадут.</summary>
    public sealed class ShopStashItem
    {
        public RelicData Relic;
        public int       SellValue;
    }

    /// <summary>Итог попытки покупки (план [[act-map-run-loop]] §5.4).</summary>
    public enum ShopBuyOutcome
    {
        Bought,          // куплено: золото списано, реликвия в запасе, слот опустел
        NotEnoughGold,   // не хватает золота
        NoSpace,         // запас реликвий полон (тост «нет места» → продать что-то)
    }

    /// <summary>
    /// Контракт магазина (план [[act-map-run-loop]] §4 B2). Логика — в <c>Game.Flow.ShopController</c>, но интерфейс
    /// живёт в Guild, чтобы UI (<c>ShopScreenView</c>) биндился к нему без обратной ссылки UI→Game. Все операции
    /// идут через золото <c>RunStateService</c> (единая точка). <see cref="Changed"/> — сигнал перерисовки для UI.
    /// </summary>
    public interface IShopController
    {
        int Gold { get; }
        int RerollCost { get; }

        /// <summary>4 слота витрины (проданные помечены <see cref="ShopItem.Sold"/>).</summary>
        IReadOnlyList<ShopItem> Shelf { get; }

        /// <summary>Запас игрока для продажи (реликвия + цена продажи 25%).</summary>
        IReadOnlyList<ShopStashItem> Stash { get; }

        /// <summary>Купить слот витрины по индексу.</summary>
        ShopBuyOutcome Buy(int index);

        /// <summary>Перекатить всю витрину за <see cref="RerollCost"/>. false = не хватило золота.</summary>
        bool Reroll();

        /// <summary>Продать реликвию из запаса (освободить место). false = такой в запасе нет.</summary>
        bool Sell(RelicData relic);

        /// <summary>Изменилось состояние (золото/витрина/запас) — UI перерисовывается.</summary>
        event Action Changed;
    }

    /// <summary>
    /// Запрос открыть экран магазина (план B2). Публикует петля акта (<c>ShopFlow</c>), слушает UI. Несёт контроллер
    /// (UI биндится к нему) и колбэк выхода — ровно один вызов <see cref="OnLeave"/> (кнопка «Уйти»/ESC).
    /// </summary>
    public readonly struct OpenShopRequest
    {
        public readonly IShopController Shop;
        public readonly Action OnLeave;

        public OpenShopRequest(IShopController shop, Action onLeave)
        {
            Shop    = shop;
            OnLeave = onLeave;
        }
    }
}
