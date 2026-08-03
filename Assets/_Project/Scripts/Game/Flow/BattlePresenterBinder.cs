using System;
using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Presentation;
using VContainer.Unity;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Раздаёт боевые внутренности сценным презентерам на время жизни боя и забирает их обратно.
    /// </summary>
    /// <remarks>
    /// <b>Почему не инъекцией.</b> <see cref="CombatPresenter"/>, <see cref="CombatDebugDraw"/> и
    /// <see cref="CombatAreaFlash"/> — объекты персист-сцены: они переживают бои по построению, а
    /// инъекция в них случается ровно один раз. Родить их вместе с боем нельзя, значит боевые куски
    /// приходят и уходят отдельно от объекта — тем же приёмом, которым бой подключает свою ленту к
    /// показу (<see cref="BattleStageBinder"/>).
    /// <para>Вне боя это не «презентер без зависимостей», а штатное состояние: показ рисует тела мира
    /// из роутера кадра, dev-оверлеи гаснут, потому что показывать им нечего.</para>
    /// </remarks>
    public sealed class BattlePresenterBinder : IStartable, IDisposable
    {
        private readonly CombatPresenter    _presenter;
        private readonly CombatDebugDraw    _debugDraw;
        private readonly CombatAreaFlash    _areaFlash;
        private readonly CombatSimulation   _simulation;
        private readonly SpatialHash        _spatialHash;
        private readonly BattleTapePlayback _playback;
        private readonly BattleTapeDispatcher _dispatcher;
        private readonly DevOverlayMode     _overlayMode;

        public BattlePresenterBinder(CombatPresenter presenter, CombatDebugDraw debugDraw,
                                     CombatAreaFlash areaFlash, CombatSimulation simulation,
                                     SpatialHash spatialHash, BattleTapePlayback playback,
                                     BattleTapeDispatcher dispatcher, DevOverlayMode overlayMode)
        {
            _presenter   = presenter;
            _debugDraw   = debugDraw;
            _areaFlash   = areaFlash;
            _simulation  = simulation;
            _spatialHash = spatialHash;
            _playback    = playback;
            _dispatcher  = dispatcher;
            _overlayMode = overlayMode;
        }

        public void Start()
        {
            _presenter.BindBattle(_simulation, _playback, _dispatcher, _overlayMode);
            _debugDraw.BindBattle(_simulation, _spatialHash, _playback, _overlayMode);
            _areaFlash.BindBattle(_dispatcher);
        }

        public void Dispose()
        {
            // Отвязываем ИМЕННО свой бой: Destroy объекта скоупа отложен до конца кадра, а рестарт
            // закрывает и открывает бой одним вызовом — новый успевает привязаться раньше, чем старый
            // отвязаться. Безымянная отвязка погасила бы показ уже начавшегося боя.
            // Порядок обратный привязке — не по необходимости, а чтобы читалось как закрытие скобок.
            _areaFlash.UnbindBattle(_dispatcher);
            _debugDraw.UnbindBattle(_simulation);
            _presenter.UnbindBattle(_simulation);
        }
    }
}
