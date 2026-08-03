using System;
using Guildmaster.Core.Audio;
using Guildmaster.Core.Flow;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using MessagePipe;
using VContainer.Unity;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Звук ЗАБЕГА вне боя: экраны, карта, переходы, меню и музыка. Живёт в root-скоупе, поэтому
    /// переживает бой — в отличие от <c>AudioPresenter</c>, который умирает вместе с боевой сценой
    /// (именно поэтому до сих пор всё вне боя было немым).
    ///
    /// Подписки — на уже существующие сообщения MessagePipe и на <see cref="IBattleClock.PhaseChanged"/>:
    /// одна фаза закрывает разом старт расстановки, старт боя и передышку. Экраны и карта своих событий
    /// наружу не шлют — их звук зовётся точечно на месте (см. <c>WorldMapView</c>, <c>DeploymentController</c>).
    ///
    /// Музыка: одна дорожка за раз, приоритет «меню важнее фазы». Переключение идёт через
    /// <see cref="IAudioService.Stop"/> + <see cref="IAudioService.Play"/> — петли держат хранимый
    /// инстанс FMOD, поэтому повторный Play того же ключа безопасен (no-op).
    /// </summary>
    public sealed class RunAudioPresenter : IStartable, IDisposable
    {
        private const string MusicMenu   = "music.menu.loop";
        private const string MusicMap    = "music.map.loop";
        private const string MusicBattle = "music.battle.loop";
        private const string AmbientArena = "ambient.arena.loop";

        private readonly IAudioService _audio;
        private readonly IBattleClock _clock;

        private readonly ISubscriber<MainMenuVisibilityChangedEvent> _mainMenuVisSub;
        private readonly ISubscriber<ScreenFadeChangedEvent> _fadeSub;
        private readonly ISubscriber<WorldMapSpaceChangedEvent> _mapSpaceSub;
        private readonly ISubscriber<OpenRewardRequest> _rewardSub;
        private readonly ISubscriber<OpenShopRequest> _shopSub;
        private readonly ISubscriber<OpenChestRequest> _chestSub;
        private readonly ISubscriber<OpenCampRequest> _campSub;
        private readonly ISubscriber<OpenTextEventRequest> _eventSub;
        private readonly ISubscriber<OpenOutcomeRequest> _outcomeSub;
        private readonly ISubscriber<OpenTitleCardRequest> _titleCardSub;
        private readonly ISubscriber<RelicDragEvent> _relicDragSub;
        private readonly ISubscriber<Flow.RunPartyReadyEvent> _partyReadySub;

        private IDisposable _subscriptions;
        private string _music;              // что играет сейчас (null — тишина)
        private bool _menuVisible;
        private bool _mapVisible;
        private bool _runActive;            // забег идёт: между стартом отряда и экраном исхода
        private bool _fadeClosed;           // ребро шторки: звук только на смену состояния
        private BattlePhase _phase = BattlePhase.None;

        public RunAudioPresenter(
            IAudioService audio,
            IBattleClock clock,
            ISubscriber<MainMenuVisibilityChangedEvent> mainMenuVisSub,
            ISubscriber<ScreenFadeChangedEvent> fadeSub,
            ISubscriber<WorldMapSpaceChangedEvent> mapSpaceSub,
            ISubscriber<OpenRewardRequest> rewardSub,
            ISubscriber<OpenShopRequest> shopSub,
            ISubscriber<OpenChestRequest> chestSub,
            ISubscriber<OpenCampRequest> campSub,
            ISubscriber<OpenTextEventRequest> eventSub,
            ISubscriber<OpenOutcomeRequest> outcomeSub,
            ISubscriber<OpenTitleCardRequest> titleCardSub,
            ISubscriber<RelicDragEvent> relicDragSub,
            ISubscriber<Flow.RunPartyReadyEvent> partyReadySub)
        {
            _partyReadySub = partyReadySub;
            _audio = audio;
            _clock = clock;
            _mainMenuVisSub = mainMenuVisSub;
            _fadeSub = fadeSub;
            _mapSpaceSub = mapSpaceSub;
            _rewardSub = rewardSub;
            _shopSub = shopSub;
            _chestSub = chestSub;
            _campSub = campSub;
            _eventSub = eventSub;
            _outcomeSub = outcomeSub;
            _titleCardSub = titleCardSub;
            _relicDragSub = relicDragSub;
        }

        public void Start()
        {
            var bag = DisposableBag.CreateBuilder();
            _mainMenuVisSub?.Subscribe(OnMainMenuVisibility).AddTo(bag);
            _fadeSub?.Subscribe(OnScreenFade).AddTo(bag);
            _mapSpaceSub?.Subscribe(OnMapSpace).AddTo(bag);
            _rewardSub?.Subscribe(_ => { _audio?.Play("reward.open.stinger"); }).AddTo(bag);
            _shopSub?.Subscribe(_ => PlayUi("screen_open")).AddTo(bag);
            _chestSub?.Subscribe(_ => PlayUi("screen_open")).AddTo(bag);
            _campSub?.Subscribe(_ => PlayUi("screen_open")).AddTo(bag);
            _eventSub?.Subscribe(_ => PlayUi("screen_open")).AddTo(bag);
            _titleCardSub?.Subscribe(_ => _audio?.Play("menu.title_card.stinger")).AddTo(bag);
            _outcomeSub?.Subscribe(OnOutcome).AddTo(bag);
            _relicDragSub?.Subscribe(OnRelicDrag).AddTo(bag);
            _partyReadySub?.Subscribe(_ => OnPartyReady()).AddTo(bag);
            _subscriptions = bag.Build();

            if (_clock != null)
            {
                _clock.PhaseChanged += OnPhaseChanged;
                _phase = _clock.Phase;
            }
            SyncMusic();
        }

        public void Dispose()
        {
            if (_clock != null) _clock.PhaseChanged -= OnPhaseChanged;
            _subscriptions?.Dispose();
            _audio?.StopAll();
        }

        private void OnMainMenuVisibility(MainMenuVisibilityChangedEvent e)
        {
            if (_menuVisible == e.Visible) return;
            _menuVisible = e.Visible;
            _audio?.Play(e.Visible ? "menu.show.ui" : "menu.hide.ui");
            SyncMusic();
        }

        private void OnMapSpace(WorldMapSpaceChangedEvent e)
        {
            if (_mapVisible == e.Active) return;
            _mapVisible = e.Active;
            _audio?.Play(e.Active ? "map.open.ui" : "map.close.ui");
            SyncMusic();
        }

        // Шторка перехода: звучат только края — закрылась и открылась, а не каждый кадр прогресса.
        private void OnScreenFade(ScreenFadeChangedEvent e)
        {
            bool closed = e.Progress >= 0.99f;
            bool open = e.Progress <= 0.01f;
            if (closed && !_fadeClosed)
            {
                _fadeClosed = true;
                _audio?.Play("flow.fade_in.ui");
            }
            else if (open && _fadeClosed)
            {
                _fadeClosed = false;
                _audio?.Play("flow.fade_out.ui");
            }
        }

        // Отряд встал на арену — забег начался. С этого момента тишины быть не должно: дорожка есть
        // и на карте, и между узлами, и в бою.
        private void OnPartyReady()
        {
            _runActive = true;
            _audio?.Play("run.start.stinger");
            SyncMusic();
        }

        private void OnOutcome(OpenOutcomeRequest e)
        {
            _runActive = false;
            SetMusic(null); // исход забега слушают в тишине, поверх — стингер
            _audio?.Play(e.Victory ? "run.outcome_victory.stinger" : "run.outcome_defeat.stinger");
        }

        private void OnRelicDrag(RelicDragEvent e)
        {
            switch (e.Phase)
            {
                case RelicDragPhase.Start: _audio?.Play("ui.drag_grab.ui"); break;
                case RelicDragPhase.Drop:  _audio?.Play("ui.drag_drop.ui"); break;
            }
        }

        private void OnPhaseChanged()
        {
            BattlePhase next = _clock?.Phase ?? BattlePhase.None;
            if (next == _phase) return;
            BattlePhase previous = _phase;
            _phase = next;

            if (next == BattlePhase.Fighting && previous != BattlePhase.Fighting)
                _audio?.Play("battle.start.stinger");

            SyncMusic();
        }

        /// <summary>Одна дорожка за раз: меню важнее фазы, бой важнее карты.</summary>
        private void SyncMusic()
        {
            // Тишина — состояние «мы нигде»: забег ещё не начат или уже кончился. Признак — именно забег,
            // а НЕ отсутствие боя: между узлами карта закрыта и боя нет, и по фазе музыка тут глохла
            // ровно в тот момент, когда закрывалась карта (выбор узла, экран награды, шторка перехода).
            string wanted;
            if (_menuVisible) wanted = MusicMenu;
            else if (_phase == BattlePhase.Fighting) wanted = MusicBattle;
            else if (_mapVisible) wanted = MusicMap;
            else if (!_runActive && _phase == BattlePhase.None) wanted = null;
            else wanted = MusicMap;   // расстановка, передышка и экраны узла — спокойная дорожка карты

            SetMusic(wanted);

            // Амбиент арены живёт, пока мир на первом плане: в меню и на экранах узла его быть не должно.
            bool arenaAudible = !_menuVisible && _phase != BattlePhase.None;
            if (arenaAudible) _audio?.Play(AmbientArena);
            else _audio?.Stop(AmbientArena);
        }

        private void SetMusic(string key)
        {
            if (_music == key) return;
            if (_music != null) _audio?.Stop(_music);
            _music = key;
            if (key != null) _audio?.Play(key);
        }

        private void PlayUi(string name) => _audio?.Play("ui." + name + ".ui");
    }
}
