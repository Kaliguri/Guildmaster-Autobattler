using System;
using System.Collections.Generic;
using System.Threading;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Guild
{
    /// <summary>Слот витрины магазина: Мементо + его цена + признак «уже куплено» (слот пуст).</summary>
    public sealed class ShopItem
    {
        public RelicData Relic;
        public int       Price;
        public bool      Sold;
    }

    /// <summary>Строка панели продажи: мементо из запаса игрока + сколько за неё дадут.</summary>
    public sealed class ShopStashItem
    {
        public RelicData Relic;
        public int       SellValue;
    }

    /// <summary>Итог попытки покупки (план [[act-map-run-loop]] §5.4).</summary>
    /// <summary>
    /// Одна рана отряда на прилавке лекаря: чья, какая и почём. Собирается контроллером из состояния
    /// забега — своего списка магазин не держит, иначе он отстал бы от вылеченного.
    /// </summary>
    public sealed class ShopInjury
    {
        /// <summary>Индекс «Сосуда» в гильдии — тот же, что в <c>RunState.Guild</c>.</summary>
        public int SlotIndex;

        public ConsequenceData Consequence;

        /// <summary>Цена снятия золотом (из ассета).</summary>
        public int Price;
    }

    public enum ShopBuyOutcome
    {
        Bought,          // куплено: золото списано, мементо в запасе, слот опустел
        NotEnoughGold,   // не хватает золота
        NoSpace,         // запас мементо полон (тост «нет места» → продать что-то)
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

        /// <summary>Запас игрока для продажи (мементо + цена продажи 25%).</summary>
        IReadOnlyList<ShopStashItem> Stash { get; }

        /// <summary>Купить слот витрины по индексу.</summary>
        ShopBuyOutcome Buy(int index);

        /// <summary>Перекатить всю витрину за <see cref="RerollCost"/>. false = не хватило золота.</summary>
        bool Reroll();

        /// <summary>Продать мементо из запаса (освободить место). false = такой в запасе нет.</summary>
        bool Sell(RelicData relic);

        /// <summary>
        /// Раны и закалки отряда, которые лекарь берётся снять, — по всем «Сосудам» гильдии.
        /// Пусто = отряд цел, полка лекаря не показывается.
        /// </summary>
        IReadOnlyList<ShopInjury> Injuries { get; }

        /// <summary>
        /// Вылечить одно последствие за золото. <c>false</c> — такого на «Сосуде» нет или не хватает
        /// золота; списание и снятие происходят одним шагом у владельца забега.
        /// </summary>
        bool Heal(int slotIndex, string consequenceId);

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

        /// <summary>Токен отмены забега (QA #37): отмена закрывает магазин через навигатор.</summary>
        public readonly CancellationToken Cancellation;

        public OpenShopRequest(IShopController shop, Action onLeave, CancellationToken cancellation = default)
        {
            Shop         = shop;
            OnLeave      = onLeave;
            Cancellation = cancellation;
        }
    }
}
