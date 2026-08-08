using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Гостевая половина «что на экране»: показывает витрину, объявленную хозяином, и отправляет голос.
    /// </summary>
    /// <remarks>
    /// <b>Витрину гость не катит.</b> Ему приезжают id того, что уже выпало, и он собирает из них ТУ ЖЕ
    /// витрину через реестр контента — совпадение реестров проверено рукопожатием. Свой раскат дал бы
    /// другие три реликвии, и группа выбирала бы из разного.
    /// <para><b>Экран закрывается признаком срабатывания</b> от общего решения, как и у хозяина, а не
    /// по объявлению <c>Idle</c>: два пути к одному закрытию разошлись бы, и у кого-то экран остался бы
    /// висеть после того, как награду уже взяли.</para>
    /// <para><b>Применить выбор гость не может</b> и не должен: реликвия ложится в чужой забег, которым
    /// владеет хозяин. Отсюда и роль этого класса — показать и передать голос.</para>
    /// </remarks>
    public sealed class GuestNodeStage : INodeStageView, IStartable, IDisposable
    {
        private readonly INetTransport   _transport;
        private readonly IContentDatabase _content;
        private readonly IPublisher<OpenRewardRequest> _openRewardPub;
        private readonly Core.Net.ISharedDecision _decision;
        // Инвентарь забега: витрина показывает, что придётся выбросить, если места нет.
        private readonly GuestRunState _runs;

        private NodeStageState _applied = NodeStageState.Idle;
        private byte[] _envelope;

        public GuestNodeStage(INetTransport transport, IContentDatabase content,
                              IPublisher<OpenRewardRequest> openRewardPub,
                              Core.Net.ISharedDecision decision, GuestRunState runs)
        {
            _transport     = transport ?? throw new ArgumentNullException(nameof(transport));
            _content       = content;
            _openRewardPub = openRewardPub;
            _decision      = decision;
            _runs          = runs;
        }

        /// <summary>Что применено последним — видно в dev-панели.</summary>
        public NodeStageState Applied => _applied;

        /// <summary>
        /// Подписаться и спросить, что сейчас на экране. Спрашиваем сами: объявление, посланное до
        /// рождения этого приёмника, ушло бы в пустоту — сеанс открывается уже после рукопожатия.
        /// </summary>
        public void Start()
        {
            _transport.MessageReceived += HandleMessage;

            if (!_transport.IsRunning) return;

            _transport.Send(NetPeer.HostPeerId,
                NetEnvelope.Wrap(NetChannel.NodeStage, default, ref _envelope),
                NetDelivery.Reliable);
        }

        public void Dispose() => _transport.MessageReceived -= HandleMessage;

        private void HandleMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.NodeStage || payload.Count == 0) return;

            // Что на экране, объявляет только хозяин: чужое объявление увело бы нас на экран, которого
            // у группы нет.
            if (from != NetPeer.HostPeerId) return;

            if (!NodeStageCodec.TryRead(payload, out NodeStageState state))
            {
                Debug.LogError("[GuestNodeStage] - шаг узла не разобран: у вас разные версии сборки. " +
                               "Экран остался прежним.");
                return;
            }

            Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Follow,
                $"гость: приехал шаг узла — {state}");

            Apply(in state);
        }

        /// <summary>Что объявлено сейчас.</summary>
        public NodeStageState Current => _applied;

        /// <inheritdoc />
        public event Action<NodeStageState> Changed;

        private void Apply(in NodeStageState state)
        {
            if (state.Equals(_applied)) return; // повтор того же — штатно, применение идемпотентно

            _applied = state;
            Changed?.Invoke(state);

            if (state.Kind != NodeStageKind.Reward) return;

            ShowReward(state.Options);
        }

        /// <summary>Собрать витрину из объявленных id и открыть её.</summary>
        private void ShowReward(IReadOnlyList<string> ids)
        {
            var choices = new List<RelicData>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                if (_content != null && _content.TryGet(ids[i], out RelicData relic)) choices.Add(relic);
                else Debug.LogError($"[GuestNodeStage] - реликвии '{ids[i]}' нет в реестре: " +
                                    "контент разъехался, хотя рукопожатие это проверяло.");
            }

            if (choices.Count == 0) return;

            IReadOnlyList<string> inventory = _runs?.Current?.RelicInventory ?? Array.Empty<string>();

            _openRewardPub?.Publish(new OpenRewardRequest(
                choices,
                // Место считает хозяин: у него правило вместимости, и второе мнение о нём разошлось бы
                // ровно тогда, когда правило поменяют. Гостю показываем витрину, а не приговор.
                inventoryFull: false,
                inventory,
                option => _decision?.Choose(option)));
        }
    }
}
