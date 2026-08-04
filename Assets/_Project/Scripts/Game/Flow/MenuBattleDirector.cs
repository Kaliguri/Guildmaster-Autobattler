using System;
using System.IO;
using Guildmaster.Core.Flow;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;
using Guildmaster.Net.Tape;
using MessagePipe;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Крутит бой ЗА главным меню: поднимает воспроизведение записанной дуэли, когда меню на экране,
    /// меняет дуэль при новом заходе и убирает её, когда меню ушло.
    /// </summary>
    /// <remarks>
    /// <b>Решение Макса 04.08.2026:</b> «в меню лучше - просто небольшие кнопки-текст без заднего фона и
    /// сам фон (например наша битва, что мы временно отказались от неё)». До этого за меню лежал тот же
    /// «стол», что под картой акта.
    ///
    /// <para><b>За меню идёт ПОВТОР, а не живой бой.</b> Дуэли записаны заранее (headless-прогон пишет
    /// файлы в <c>StreamingAssets/Replays</c>), а здесь проигрываются — воспроизведение не поднимает ни
    /// симуляцию, ни сессию, ни расстановку, поэтому и не упирается в те их зависимости, которых у меню
    /// нет. «Просчитанные заранее бои» из исходной задумки Макса — это и есть записи. Прежнее «записи
    /// боя нет и не будет» отменено (журнал 2026-08-04-replay-is-the-tape-persisted).</para>
    ///
    /// <para><b>Дуэль меняется на ВХОДЕ в меню, а не по таймеру.</b> Кластер меню — это и настройки, и
    /// профиль, и выбор режима; перескок на другую дуэль посреди этого читался бы как сбой, а не как
    /// разнообразие. Единственное исключение — предохранитель <see cref="MenuBattleConfig.MaxSeconds"/>:
    /// запись, которая не кончается в отведённое, снимается им.</para>
    ///
    /// <para><b>Воспроизведение поднимается прямо от МИРА</b> реплей-вариантом боевого скоупа
    /// (<see cref="MenuReplayScope"/>): в главном меню сеанса ещё нет, а фон меню — не занятие игрока и
    /// владения состоянием не требует. Мир → воспроизведение, без сессии посередине.</para>
    /// </remarks>
    public sealed class MenuBattleDirector : IStartable, ITickable, IDisposable
    {
        private readonly VContainer.Unity.LifetimeScope _world;
        private readonly MenuReplayScope _replayScope;
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
        private float _sinceEnd = -1f;   // -1 — запись ещё идёт; иначе секунды с её конца
        private int _lastIndex = -1;
        private CombatLifetimeScope _battle;

        public MenuBattleDirector(VContainer.Unity.LifetimeScope world,
                                  MenuReplayScope replayScope,
                                  MenuBattleConfig config,
                                  IRngService rng,
                                  ISubscriber<MainMenuVisibilityChangedEvent> menuSub,
                                  ISubscriber<Presentation.BattleEndedEvent> endedSub,
                                  IPublisher<MenuBattleChangedEvent> statePub)
        {
            _world       = world;
            _replayScope = replayScope;
            _config      = config;
            _rng      = rng;
            _menuSub  = menuSub;
            _endedSub = endedSub;
            _statePub = statePub;
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

            // Запись кончилась и добивание досмотрено — следующая. Либо она не кончилась вовсе, и тогда
            // её снимает предохранитель: висящая арена хуже смены на глазах.
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

        /// <summary>Поднять следующую запись: свой файл, свой реплей-скоуп.</summary>
        private void StartNext()
        {
            if (_config == null || !_config.Enabled)
            {
                // Пустая настройка «фон без боя» — законна, а не поломка: молча ничего не делаем.
                return;
            }

            if (_replayScope?.Value == null)
            {
                Debug.LogWarning("[MenuBattleDirector] - в мире не задан реплей-префаб → фона не будет");
                return;
            }

            int index = PickNextIndex();
            if (index < 0) return;

            byte[] bytes = LoadReplay(index);
            if (bytes == null) return;

            // Скоуп рождается заново на каждую запись: его смерть — наш штатный способ убрать арену со
            // всем, что на ней жило.
            CloseBattle();

            // Сид передаём для простаивающего сима реплей-скоупа (RNG хочет BattleScopeParams); на само
            // воспроизведение он не влияет — лента играется как записана. Байты файла — заказом.
            _battle = _world.CreateChildFromPrefab(_replayScope.Value,
                b =>
                {
                    b.RegisterInstance(new BattleScopeParams(preset: null, seed: 0UL));
                    b.RegisterInstance(new ReplayPlaybackRequest(bytes));
                });
            _battle.name = "[Menu Replay] duel " + index;

            _elapsed  = 0f;
            _sinceEnd = -1f;

            if (!_running)
            {
                _running = true;
                _statePub?.Publish(new MenuBattleChangedEvent(true));
            }
        }

        /// <summary>
        /// Прочитать файл записи по индексу дуэли. Нет файла — не поломка: запись могли не сгенерировать,
        /// и фон просто не заведётся на этой дуэли.
        /// </summary>
        private byte[] LoadReplay(int index)
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Replays",
                                       "menu_duel_" + index + ReplayFile.Extension);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[MenuBattleDirector] - нет файла записи '{path}' → эта дуэль за меню не покажется");
                return null;
            }

            try
            {
                return File.ReadAllBytes(path);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[MenuBattleDirector] - не прочитать запись '{path}': {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Следующая дуэль вразнобой, но НЕ та же самая два раза подряд: повтор сразу читается как
        /// «зациклилось». Индекс совпадает с именем файла (menu_duel_{index}).
        /// </summary>
        private int PickNextIndex()
        {
            int count = _config.Battles.Count;
            if (count == 0) return -1;
            if (count == 1) { _lastIndex = 0; return 0; }

            int index = _lastIndex;
            for (int attempt = 0; attempt < 8 && index == _lastIndex; attempt++)
                index = _rng != null ? _rng.NextInt(0, count) : 0;

            _lastIndex = index;
            return index;
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
