using System;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Гостевая половина «что на экране»: принимает объявленный хозяином шаг узла и держит его у себя.
    /// </summary>
    /// <remarks>
    /// <b>Показывать — не его дело.</b> Экраны узла открывает общий для обеих ролей потребитель
    /// (<see cref="SessionStageScreens"/>), а этот класс — только гостевой конец провода: принять, разобрать
    /// и поднять событие. Пока показ жил здесь, у витрины награды было ДВА пути — этот и хозяйская
    /// петля, — и во втором признак «запас полон» был зашит в <c>false</c> (HARD «равные игроки»).
    /// <para><b>Витрину гость не катит.</b> Ему приезжают id того, что уже выпало: свой раскат дал бы
    /// другие три мементо, и группа выбирала бы из разного.</para>
    /// </remarks>
    public sealed class GuestSessionStage : ISessionStageView, IStartable, IDisposable
    {
        private readonly INetTransport _transport;

        private SessionStageState _applied = SessionStageState.Idle;
        private byte[] _envelope;

        public GuestSessionStage(INetTransport transport) =>
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));

        /// <summary>Что применено последним — видно в dev-панели.</summary>
        public SessionStageState Applied => _applied;

        /// <summary>
        /// Подписаться и спросить, что сейчас на экране. Спрашиваем сами: объявление, посланное до
        /// рождения этого приёмника, ушло бы в пустоту — сеанс открывается уже после рукопожатия.
        /// </summary>
        public void Start()
        {
            _transport.MessageReceived += HandleMessage;

            if (!_transport.IsRunning) return;

            _transport.Send(NetPeer.HostPeerId,
                NetEnvelope.Wrap(NetChannel.SessionStage, default, ref _envelope),
                NetDelivery.Reliable);
        }

        public void Dispose() => _transport.MessageReceived -= HandleMessage;

        private void HandleMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.SessionStage || payload.Count == 0) return;

            // Что на экране, объявляет только хозяин: чужое объявление увело бы нас на экран, которого
            // у группы нет.
            if (from != NetPeer.HostPeerId) return;

            if (!SessionStageCodec.TryRead(payload, out SessionStageState state))
            {
                Debug.LogError("[GuestSessionStage] - шаг узла не разобран: у вас разные версии сборки. " +
                               "Экран остался прежним.");
                return;
            }

            Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Follow,
                $"гость: приехал шаг узла — {state}");

            Apply(in state);
        }

        /// <summary>Что объявлено сейчас.</summary>
        public SessionStageState Current => _applied;

        /// <inheritdoc />
        public event Action<SessionStageState> Changed;

        private void Apply(in SessionStageState state)
        {
            if (state.Equals(_applied)) return; // повтор того же — штатно, применение идемпотентно

            _applied = state;
            Changed?.Invoke(state);
        }
    }
}
