using System;
using Guildmaster.Core.Flow;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Крутит бой ЗА главным меню: поднимает арену, когда меню на экране, меняет бой при новом заходе
    /// и убирает арену, когда меню ушло.
    /// </summary>
    /// <remarks>
    /// <b>Решение Макса 04.08.2026:</b> «в меню лучше - просто небольшие кнопки-текст без заднего фона и
    /// сам фон (например наша битва, что мы временно отказались от неё)». До этого за меню лежал тот же
    /// «стол», что под картой акта.
    ///
    /// <para><b>Записи боя нет и не будет.</b> Ядро детерминировано: пресет плюс сид дают тот же бой
    /// покадрово, поэтому «заранее просчитанный» получается бесплатно, а система реплеев ради фона не
    /// заводится. Набор боёв — данные (<see cref="MenuBattleConfig"/>), не код.</para>
    ///
    /// <para><b>Бой меняется на ВХОДЕ в меню, а не по таймеру.</b> Кластер меню — это и настройки, и
    /// профиль, и выбор режима; перескок на другой бой посреди этого читался бы как сбой, а не как
    /// разнообразие. Единственное исключение — предохранитель <see cref="MenuBattleConfig.MaxSeconds"/>:
    /// бой, который не кончается, единственный настоящий отказ этой затеи.</para>
    ///
    /// <para><b>Бой поднимается прямо от МИРА, а не через мероприятие.</b> Первая версия звала
    /// <c>ActivityHost.Open</c> и получала «сеанс не открыт → дочернему скоупу рождаться не от кого»:
    /// в главном меню сеанса ещё нет, он открывается вместе с игрой. Фон меню — не занятие игрока и
    /// владения состоянием не требует, поэтому живёт этажом ниже: мир → бой, без сессии посередине.</para>
    /// </remarks>
    public sealed class MenuBattleDirector : IStartable, ITickable, IDisposable
    {
        private readonly VContainer.Unity.LifetimeScope _world;
        private readonly Activity.BattleScopePrefab _battleScopePrefab;
        private readonly MenuBattleConfig _config;
        private readonly IRngService _rng;
        private readonly ISubscriber<MainMenuVisibilityChangedEvent> _menuSub;
        private readonly ISubscriber<Presentation.BattleEndedEvent> _endedSub;
        private readonly IPublisher<MenuBattleChangedEvent> _statePub;

        private IDisposable _menuSubscription;
        private IDisposable _endedSubscription;

        private bool _menuOpen;
        private bool _running;
        private float _elapsed;
        private float _sinceEnd = -1f;   // -1 — бой ещё идёт; иначе секунды с его конца
        private int _lastIndex = -1;
        private CombatLifetimeScope _battle;

        public MenuBattleDirector(VContainer.Unity.LifetimeScope world,
                                  Activity.BattleScopePrefab battleScopePrefab,
                                  MenuBattleConfig config,
                                  IRngService rng,
                                  ISubscriber<MainMenuVisibilityChangedEvent> menuSub,
                                  ISubscriber<Presentation.BattleEndedEvent> endedSub,
                                  IPublisher<MenuBattleChangedEvent> statePub)
        {
            _world             = world;
            _battleScopePrefab = battleScopePrefab;
            _config            = config;
            _rng        = rng;
            _menuSub    = menuSub;
            _endedSub   = endedSub;
            _statePub   = statePub;
        }

        public void Start()
        {
            _menuSubscription  = _menuSub?.Subscribe(e => OnMenuVisibility(e.Visible));
            _endedSubscription = _endedSub?.Subscribe(_ => _sinceEnd = 0f);
        }

        public void Dispose()
        {
            _menuSubscription?.Dispose();
            _endedSubscription?.Dispose();
            Stop();
        }

        public void Tick()
        {
            if (!_running) return;

            _elapsed += Time.unscaledDeltaTime;
            if (_sinceEnd >= 0f) _sinceEnd += Time.unscaledDeltaTime;

            // Бой кончился и добивание досмотрено — следующий. Либо бой не кончается вовсе, и тогда
            // его снимает предохранитель: висящая арена хуже смены боя на глазах.
            bool ended    = _sinceEnd >= 0f && _sinceEnd >= _config.AfterEndSeconds;
            bool overtime = _elapsed >= _config.MaxSeconds;
            if (ended || overtime) StartNext();
        }

        private void OnMenuVisibility(bool visible)
        {
            if (_menuOpen == visible) return;
            _menuOpen = visible;

            if (visible) StartNext();
            else Stop();
        }

        /// <summary>Поднять следующий бой: своё мероприятие, свой пресет, свой сид.</summary>
        private void StartNext()
        {
            if (_config == null || !_config.HasAnything)
            {
                // Молча ничего не делаем: пустой список — это законная настройка «фон без боя», а не
                // поломка. Ругаться здесь значило бы сыпать в консоль каждому, кто выключил фон.
                return;
            }

            MenuBattleConfig.Entry entry = PickNext();
            if (entry?.Preset == null) return;

            if (_battleScopePrefab?.Value == null)
            {
                Debug.LogWarning("[MenuBattleDirector] - в мире не задан префаб боевого скоупа → фона не будет");
                return;
            }

            // Скоуп рождается заново на каждый бой: его смерть — наш штатный способ убрать арену со
            // всем, что на ней жило. Чистить арену вручную мы уже пробовали и именно от этого ушли.
            CloseBattle();

            ulong seed = entry.Seed != 0UL ? entry.Seed : DeterministicHash.Of("menu:" + entry.Preset.Id);
            _battle = _world.CreateChildFromPrefab(_battleScopePrefab.Value,
                b => b.RegisterInstance(new BattleScopeParams(entry.Preset, seed)));
            _battle.name = "[Menu Battle] " + entry.Preset.Id;

            _elapsed  = 0f;
            _sinceEnd = -1f;

            if (!_running)
            {
                _running = true;
                _statePub?.Publish(new MenuBattleChangedEvent(true));
            }
        }

        /// <summary>
        /// Следующий бой вразнобой, но НЕ тот же самый два раза подряд: повтор сразу читается как
        /// «зациклилось», даже когда боёв в списке много.
        /// </summary>
        private MenuBattleConfig.Entry PickNext()
        {
            var battles = _config.Battles;
            if (battles.Count == 0) return null;
            if (battles.Count == 1) { _lastIndex = 0; return battles[0]; }

            int index = _lastIndex;
            for (int attempt = 0; attempt < 8 && index == _lastIndex; attempt++)
                index = _rng != null ? _rng.NextInt(0, battles.Count) : 0;

            _lastIndex = index;
            return battles[index];
        }

        private void CloseBattle()
        {
            if (_battle == null) return;
            _battle.Dispose();
            _battle = null;
        }

        private void Stop()
        {
            if (!_running) return;

            CloseBattle();
            _running  = false;
            _elapsed  = 0f;
            _sinceEnd = -1f;
            _statePub?.Publish(new MenuBattleChangedEvent(false));
        }
    }
}
