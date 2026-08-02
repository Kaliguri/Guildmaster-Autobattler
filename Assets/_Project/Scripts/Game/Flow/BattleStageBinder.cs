using System;
using Guildmaster.Combat.Tape;
using VContainer.Unity;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Подключает ленту боя к показу на время жизни боевого скоупа и отключает её, когда бой уходит.
    /// </summary>
    /// <remarks>
    /// <b>Привязку делает бой, а не мир.</b> Мир про бой не знает: он держит тела и отдаёт их показу,
    /// пока никто не перебил. Обратное направление (мир спрашивает «а идёт ли бой») вернуло бы нам
    /// ровно то, от чего уходим, — знание о границе боя, размазанное по тем, кто её не создаёт.
    /// <para>Пока боевой скоуп поднимается на буте, привязка живёт всю сессию и поведение игры не
    /// меняется. Смысл появится в шаге 1б, когда скоуп начнёт рождаться вместе с боем — этот класс
    /// переедет в него без единой правки.</para>
    /// </remarks>
    public sealed class BattleStageBinder : IStartable, IDisposable
    {
        private readonly StageFrameRouter    _router;
        private readonly BattleTapePlayback  _playback;

        public BattleStageBinder(StageFrameRouter router, BattleTapePlayback playback)
        {
            _router   = router;
            _playback = playback;
        }

        public void Start() => _router.Bind(_playback);

        public void Dispose() => _router.Unbind(_playback);
    }
}
