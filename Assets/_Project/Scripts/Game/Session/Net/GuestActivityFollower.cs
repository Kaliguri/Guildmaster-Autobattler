using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Activity;
using Guildmaster.Game.Flow;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Гостевая половина «где мы»: открывает и закрывает мероприятие и арену вслед за хостом.
    /// </summary>
    /// <remarks>
    /// <b>Гость не ведёт акт.</b> Куда идти по карте, какая награда выпала и что стоит в лавке —
    /// решает владелец забега; гостю приезжает результат (снимок состояния) и место, где всё это
    /// происходит. Поэтому здесь нет ни выбора узла, ни запуска боя по своей воле: только следование.
    /// <para><b>Бой у гостя — это скоуп-приёмник.</b> Открылась арена у хоста — рождается своя, с
    /// приёмником чанков и составом; закрылась — умирает вместе со всем, что накопила. Отсюда же
    /// бесплатно закрылся долг «повторный бой у гостя не начинается»: сбрасывать номера чанков и
    /// реестр стало нечему, потому что новый бой — это новый скоуп.</para>
    /// <para><b>Порядок применения важен:</b> сперва мероприятие, потом арена. Арену открывает
    /// <c>BattleHost</c>, который живёт ВНУТРИ мероприятия, — до него его просто нет.</para>
    /// </remarks>
    public sealed class GuestActivityFollower : IStartable, IDisposable
    {
        private readonly INetTransport _transport;
        private readonly ActivityHost  _activities;
        // Карта живёт в мире, а не в мероприятии, но для гостя это часть одного ответа «где мы».
        private readonly IActMapPresence _map;
        // Двор — на тех же правах: место вне мероприятия, в которое гость обязан попасть вместе с хостом.
        private readonly Guildmaster.Core.Flow.IHubPresence _hub;
        // Общее согласие. Гость его только отправляет — но отправить может лишь тогда, когда знает, ЧЕГО
        // ждут, а ключ ему выставить некому: у хоста это делает расстановка, которой у гостя нет.
        private readonly Guildmaster.Core.Net.IReadyGate _ready;

        private Action _toggleReady;

        private ActivityState _applied = ActivityState.Nowhere;
        private byte[]        _envelope;

        public GuestActivityFollower(INetTransport transport, ActivityHost activities,
                                     IActMapPresence map, Guildmaster.Core.Flow.IHubPresence hub,
                                     Guildmaster.Core.Net.IReadyGate ready)
        {
            _transport  = transport  ?? throw new ArgumentNullException(nameof(transport));
            _activities = activities ?? throw new ArgumentNullException(nameof(activities));
            _map        = map;
            _hub        = hub;
            _ready      = ready;
        }

        /// <summary>Что применено последним — видно в dev-панели.</summary>
        public ActivityState Applied => _applied;

        /// <summary>
        /// Подписаться и спросить хоста, где идёт игра. Спрашиваем сами: объявление, посланное нам до
        /// рождения этого приёмника, ушло бы в пустоту — а сеанс открывается уже после рукопожатия.
        /// </summary>
        public void Start()
        {
            _transport.MessageReceived += HandleMessage;

            if (!_transport.IsRunning) return;

            _transport.Send(NetPeer.HostPeerId,
                NetEnvelope.Wrap(NetChannel.ActivityState, default, ref _envelope),
                NetDelivery.Reliable);
        }

        public void Dispose() => _transport.MessageReceived -= HandleMessage;

        private void HandleMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.ActivityState) return;

            // Где идёт игра, объявляет только хост: чужое объявление от другого гостя увело бы нас в
            // место, которого нет.
            if (from != NetPeer.HostPeerId) return;

            if (!ActivityStateCodec.TryRead(payload, out ActivityState state))
            {
                Debug.LogError("[GuestActivityFollower] - состояние мероприятия не разобрано: у вас " +
                               "разные версии сборки. Игра осталась там, где была.");
                return;
            }

            Apply(in state);
        }

        private void Apply(in ActivityState state)
        {
            if (state.Equals(_applied)) return; // повтор того же — штатно, состояние идемпотентно

            ApplyActivity(in state);
            ApplyBattle(in state);
            ApplyPhase(in state);
            ApplyMap(in state);
            ApplyHub(in state);

            _applied = state;
        }

        private void ApplyActivity(in ActivityState state)
        {
            if (state.Kind == _applied.Kind && _activities.IsOpen == (state.Kind != ActivityKind.None))
                return;

            if (state.Kind == ActivityKind.None) _activities.Close();
            else                                 _activities.Open(state.ToSetup());
        }

        private void ApplyBattle(in ActivityState state)
        {
            BattleHost battles = _activities.Battle;
            if (battles == null) return; // мероприятия нет — арене неоткуда взяться

            if (state.BattleOpen == battles.IsOpen) return;

            // Пустая арена и есть гостевой бой: состав приезжает паспортами, кадры — чанками ленты.
            // Пресета у гостя нет и быть не может — бой считает не он.
            if (state.BattleOpen) battles.OpenEmpty();
            else                  battles.Close();
        }

        private void ApplyPhase(in ActivityState state)
        {
            IBattleSession session = _activities.Battles;
            if (session == null) return;

            // Фазу у гостя вести нечем: её ведёт флоу забега, а забег ведёт хост. Без этой строки
            // боевой UI у гостя молчал бы весь бой — панель скрыта, потому что фаза None.
            session.SetPhase(state.Phase);

            ApplyReady(session, state.Phase);
        }

        /// <summary>
        /// Чего ждут от игрока в этой фазе — и чем он отвечает.
        /// </summary>
        /// <remarks>
        /// <b>Ключ гейта у гостя выставить больше некому.</b> У хоста это делает расстановка
        /// (<c>DeploymentController</c>), а у гостя её нет вовсе: бой ему приезжает скоупом-приёмником.
        /// Без ключа кнопка «Начать» не знала, чего ждут, счёт «(1/2)» не рисовался, а нажатие уходило
        /// в пустой делегат — гость не мог подтвердить готовность вообще (наход. Макса 04.08.2026).
        /// <para><b>Гость подтверждает, но не начинает.</b> Кнопка шлёт «я готов», а бой у него пойдёт
        /// оттого, что хост сменит фазу. Второй путь к тому же состоянию разошёлся бы с первым.</para>
        /// <para>Фаза здесь — единственный источник: она же приезжает от хоста, она же ведёт панель.
        /// Заводить своё «мы в расстановке» значило бы завести второго владельца одного факта.</para>
        /// </remarks>
        private void ApplyReady(IBattleSession session, BattlePhase phase)
        {
            if (phase == BattlePhase.Deployment)
            {
                _ready?.Bind(Guildmaster.Core.Net.ReadyKeys.BattleStart, null);
                session.BindStart(_toggleReady ??= () => _ready?.ToggleLocal());
                return;
            }

            _ready?.Unbind(Guildmaster.Core.Net.ReadyKeys.BattleStart);
            session.UnbindStart();
        }

        /// <summary>
        /// Открыть или закрыть карту вслед за хостом.
        /// </summary>
        /// <remarks>
        /// Карту открывает петля акта в момент выбора узла, а петли у гостя нет — до этой строки он
        /// заходил в чужую кампанию и оставался в пустом мире (наход. Макса 03.08.2026).
        /// <para>Данных для отрисовки в этот момент может ещё не быть: забег едет своим каналом, и
        /// порядок между каналами не гарантирован. Показ это переживает — просьба остаётся в силе, а
        /// нарисуется карта, как только доедет снимок (<see cref="WorldMapController.Refresh"/>).</para>
        /// </remarks>
        private void ApplyMap(in ActivityState state)
        {
            _map?.SetVisible(state.MapOpen);
        }

        /// <summary>
        /// Открыть или закрыть двор вслед за хостом.
        /// </summary>
        /// <remarks>
        /// Двор открывает петля игры между выбором дома и забегом, а петли у гостя нет: он оставался в
        /// том месте, где его застало подключение, — на боевой камере посреди пустого мира, пока хост
        /// стоял во дворе (наход. Макса 04.08.2026).
        /// <para>Имя дома гость не получает и не должен: двор здесь — МЕСТО, а чей он и что в нём
        /// лежит, приезжает состоянием забега своим каналом.</para>
        /// </remarks>
        private void ApplyHub(in ActivityState state)
        {
            _hub?.SetVisible(state.HubOpen);
        }
    }
}
