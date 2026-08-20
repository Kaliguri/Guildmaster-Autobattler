using System.Collections.Generic;
using Guildmaster.Core.Players;
using Guildmaster.Net.Presence;
using UnityEngine;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Общая для обеих ролей половина присутствия: держит принятые чужие курсоры и выдаёт их отрисовке
    /// сглаженными.
    /// </summary>
    /// <remarks>
    /// <b>Вынесено из ролей, потому что приём у хоста и у гостя одинаков.</b> Различается только раздача:
    /// хост режет пакет по сторонам и рассылает, гость отправляет своё одному хосту. Держать две копии
    /// приёма значило бы чинить сглаживание дважды.
    /// <para><b>Свой курсор сюда не кладётся.</b> Он и так под рукой, а пройдя через буфер, отставал бы
    /// от мыши на время буфера — это ровно тот лаг, из-за которого чужие курсоры выглядят живыми, а свой
    /// «залипшим».</para>
    /// </remarks>
    public sealed class PresenceCursors : IPresenceView
    {
        private readonly PresenceInterpolator _interpolator = new PresenceInterpolator();
        private readonly List<RemoteCursor>   _visible      = new List<RemoteCursor>(4);
        private readonly List<int>            _players      = new List<int>(4);

        public int Count => _visible.Count;

        public RemoteCursor this[int index] => _visible[index];

        /// <summary>Принять чужое состояние. <paramref name="now"/> — время приёма, секунды.</summary>
        public void Push(in PresenceState state, float now) => _interpolator.Push(in state, now);

        /// <summary>Забыть ушедшего: его курсор иначе завис бы на арене навсегда.</summary>
        public void Forget(int playerId) => _interpolator.Remove(playerId);

        public void Clear()
        {
            _interpolator.Clear();
            _visible.Clear();
        }

        /// <summary>
        /// Пересчитать положения на момент <paramref name="now"/>. Зовётся раз в кадр: интерполяция
        /// строится ко времени показа, а не ко времени приёма пакета.
        /// </summary>
        /// <remarks>
        /// <b>Ушедший и сменивший сторону забываются ЗДЕСЬ, по составу сеанса.</b> По давности последнего
        /// пакета это сделать нельзя, и ошибка соблазнительная: курсор в покое не шлёт пакетов вовсе
        /// (в этом весь смысл dirty-check), поэтому «давно молчит» означает «человек не двигает мышью»,
        /// а не «его нет». Состав же приходит надёжным каналом и меняется ровно тогда, когда надо забыть.
        /// </remarks>
        public void Sample(float now, ISessionRoster roster, int localPlayerId)
        {
            _visible.Clear();

            _players.Clear();
            foreach (int playerId in _interpolator.Players) _players.Add(playerId);

            for (int i = 0; i < _players.Count; i++)
            {
                int playerId = _players[i];
                if (playerId == localPlayerId) continue; // свой курсор рисует система, а не сеть

                if (roster != null && !roster.SharesTeamWithLocal(playerId))
                {
                    _interpolator.Remove(playerId);
                    continue;
                }

                if (!_interpolator.TrySample(playerId, now, out PresenceState state, out Vector2 position))
                    continue;

                _visible.Add(new RemoteCursor(playerId, position, state.IsHolding, state.HoveredId));
            }
        }
    }
}
