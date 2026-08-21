using System;
using System.Collections.Generic;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Логика магазина (план [[act-map-run-loop]] §4 B2), реализация <see cref="IShopController"/>. Витрину катит
    /// <see cref="RewardService"/> (тот же пул, что награда), цены — <see cref="RelicPricer"/>, все денежные операции
    /// идут через <see cref="RunStateService"/> (единая точка золота/вместимости). DI-синглтон; <see cref="Open"/>
    /// сбрасывает состояние на каждый вход в магазин.
    /// </summary>
    public sealed class ShopController : IShopController
    {
        private const int ShelfSize = 4;

        private readonly RewardService    _rewards;
        private readonly RelicPricer      _pricer;
        private readonly RunStateService  _runStates;
        // Односторонние записи в забег (продажа: минус реликвия, плюс золото) идут через шину команд —
        // иначе в коопе второй игрок увидит опустевшую полку без причины в логе.
        private readonly Guildmaster.Guild.Commands.IRunCommands _commands;
        private readonly IContentDatabase _content;
        private readonly IRngService      _rng;
        private readonly GameConfig       _config;
        private readonly Core.Audio.IAudioService _audio; // покупка/продажа/реролл/отказ — голос лавки

        private readonly List<ShopItem> _shelf = new();
        private int _shopSeed;

        public ShopController(RewardService rewards, RelicPricer pricer, RunStateService runStates,
                              Guildmaster.Guild.Commands.IRunCommands commands,
                              IContentDatabase content, IRngService rng, GameConfig config,
                              Core.Audio.IAudioService audio = null)
        {
            _audio     = audio;
            _rewards   = rewards;
            _pricer    = pricer;
            _runStates = runStates;
            _commands  = commands;
            _content   = content;
            _rng       = rng;
            _config    = config;
        }

        public event Action Changed;

        public int Gold       => _runStates.Gold;
        public int RerollCost => _config.ShopRerollCost;
        public IReadOnlyList<ShopItem> Shelf => _shelf;

        public IReadOnlyList<ShopStashItem> Stash
        {
            get
            {
                var stash = new List<ShopStashItem>();
                RunState run = _runStates.Current;
                if (run?.RelicInventory == null) return stash;

                foreach (string id in run.RelicInventory)
                {
                    if (_content.TryGet<RelicData>(id, out RelicData relic) && relic != null)
                        stash.Add(new ShopStashItem { Relic = relic, SellValue = SellValueOf(relic) });
                }
                return stash;
            }
        }

        /// <summary>Открыть магазин: свежий сид, перекат витрины. Зовётся на входе в узел.</summary>
        public void Open()
        {
            _shopSeed = _rng.NextInt(0, int.MaxValue);
            RollShelf();
        }

        public ShopBuyOutcome Buy(int index)
        {
            if (index < 0 || index >= _shelf.Count) return ShopBuyOutcome.NotEnoughGold;
            ShopItem item = _shelf[index];
            if (item.Sold || item.Relic == null) return ShopBuyOutcome.NotEnoughGold;

            // Отказы звучат отдельно: молчаливая кнопка «Купить» читается как сломанная.
            if (_runStates.RelicInventoryFull) { _audio?.Play("shop.denied.ui"); return ShopBuyOutcome.NoSpace; }
            if (_runStates.Gold < item.Price)  { _audio?.Play("shop.denied.ui"); return ShopBuyOutcome.NotEnoughGold; }

            _runStates.TrySpendGold(item.Price);
            _runStates.TryAddRelic(item.Relic.Id);
            item.Sold = true;
            _runStates.Autosave();
            Changed?.Invoke();
            _audio?.Play("shop.buy.ui");
            return ShopBuyOutcome.Bought;
        }

        public bool Reroll()
        {
            if (!_runStates.TrySpendGold(RerollCost)) { _audio?.Play("shop.denied.ui"); return false; }
            _shopSeed = _rng.NextInt(0, int.MaxValue);
            RollShelf();
            _runStates.Autosave();
            Changed?.Invoke();
            _audio?.Play("shop.reroll.ui");
            return true;
        }

        public bool Sell(RelicData relic)
        {
            if (relic == null || _runStates.Current == null) return false;
            if (Array.IndexOf(_runStates.Current.RelicInventory, relic.Id) < 0) return false;

            _commands.RemoveRelic(relic.Id);
            _commands.AddGold(SellValueOf(relic));
            _runStates.Autosave();
            Changed?.Invoke();
            _audio?.Play("shop.sell.ui");
            return true;
        }

        /// <summary>
        /// Полка лекаря: все последствия отряда с ценой снятия. Пересобирается на каждый запрос, а не
        /// кэшируется, — иначе вылеченная рана осталась бы висеть на витрине до перерисовки.
        /// </summary>
        public IReadOnlyList<ShopInjury> Injuries
        {
            get
            {
                var list = new List<ShopInjury>();
                RunState run = _runStates.Current;
                if (run?.Guild == null) return list;

                for (int slot = 0; slot < run.Guild.Length; slot++)
                {
                    Injury[] injuries = run.Guild[slot]?.Injuries;
                    if (injuries == null) continue;

                    for (int i = 0; i < injuries.Length; i++)
                    {
                        string id = injuries[i]?.Id;
                        if (string.IsNullOrEmpty(id)) continue;
                        if (!_content.TryGet(id, out ConsequenceData def) || !def.IsInjury) continue;

                        list.Add(new ShopInjury
                        {
                            SlotIndex   = slot,
                            Consequence = def,
                            Price       = def.HealCostGold,
                        });
                    }
                }
                return list;
            }
        }

        public bool Heal(int slotIndex, string consequenceId)
        {
            if (string.IsNullOrEmpty(consequenceId) || _runStates.Current == null) return false;

            // Цену спрашиваем ДО отправки команды: применитель откажет молча (команда ничего не
            // возвращает), а лекарю надо ответить игроку отказом здесь и сейчас.
            int cost = _runStates.HealCost(consequenceId);
            if (cost > Gold)
            {
                _audio?.Play("shop.deny.ui");
                return false;
            }

            _commands.HealInjury(slotIndex, consequenceId, payGold: true);
            _runStates.Autosave();
            Changed?.Invoke();
            _audio?.Play("shop.buy.ui");
            return true;
        }

        private void RollShelf()
        {
            _shelf.Clear();
            IReadOnlyList<RelicData> choices = _rewards.RollChoices(RewardTier.Battle, ShelfSize);
            foreach (RelicData relic in choices)
                _shelf.Add(new ShopItem { Relic = relic, Price = _pricer.Price(relic, _shopSeed), Sold = false });
        }

        private int SellValueOf(RelicData relic) => _pricer.SellValue(_pricer.Price(relic, _shopSeed));
    }
}
