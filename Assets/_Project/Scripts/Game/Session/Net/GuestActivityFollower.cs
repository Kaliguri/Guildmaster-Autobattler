using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Activity;
using Guildmaster.Game.Flow;
using Guildmaster.Net;
using Guildmaster.Net.Transport;
using MessagePipe; // ради расширения Subscribe(Action<T>): без него подписка требует IMessageHandler
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
        // Общее согласие. Гость его только отправляет — но отправить может лишь тогда, когда знает, ЧЕГО
        // ждут, а ключ ему выставить некому: у хоста это делает расстановка, которой у гостя нет.
        private readonly Guildmaster.Core.Net.ISharedDecision _ready;

        private Action _toggleReady;

        // Экран итога боя на площадке. Показывает его гость сам: по сети едет состояние, а не показ.
        private readonly MessagePipe.IPublisher<Guildmaster.Guild.OpenOutcomeRequest> _outcomePub;
        private readonly MessagePipe.ISubscriber<Guildmaster.Presentation.BattleEndedEvent> _endedSub;
        // Уйти с чужой площадки значит выйти из чужой игры: своего забега, который можно было бы
        // прервать, у гостя нет вовсе.
        private readonly Guildmaster.Core.Net.ICoopSessionControl _coop;
        // Своя сторона: по ней и только по ней читается, победа это или поражение.
        private readonly Guildmaster.Core.Players.ILocalPlayer _localPlayer;

        private IDisposable _endedSubscription;
        private bool        _lastVictory;

        // Снимок забега: из него гость сам считает и какие узлы достижимы, и ждут ли вообще выбора.
        // По сети не едет ни то, ни другое — оба выводятся из состояния (см. IActMapPresence).
        private readonly GuestRunState _runs;

        private ActivityState _applied = ActivityState.Nowhere;
        private byte[]        _envelope;

        public GuestActivityFollower(INetTransport transport, ActivityHost activities,
                                     IActMapPresence map,
                                     Guildmaster.Core.Net.ISharedDecision ready, GuestRunState runs,
                                     MessagePipe.IPublisher<Guildmaster.Guild.OpenOutcomeRequest> outcomePub,
                                     MessagePipe.ISubscriber<Guildmaster.Presentation.BattleEndedEvent> endedSub,
                                     Guildmaster.Core.Net.ICoopSessionControl coop,
                                     Guildmaster.Core.Players.ILocalPlayer localPlayer)
        {
            _transport  = transport  ?? throw new ArgumentNullException(nameof(transport));
            _activities = activities ?? throw new ArgumentNullException(nameof(activities));
            _map        = map;
            _ready      = ready;
            _runs       = runs;
            _outcomePub  = outcomePub;
            _endedSub    = endedSub;
            _coop        = coop;
            _localPlayer = localPlayer;
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

            // Горящие узлы пересчитываются на каждый снимок: и «ждём ли выбора», и «куда можно» —
            // это состояние забега, а оно приезжает именно снимком.
            if (_runs != null) _runs.SnapshotReceived += HandleSnapshot;

            // Исход боя гость узнаёт ИЗ ЛЕНТЫ — тем же событием, что и хозяин из своей симуляции.
            // Пересчитывать его по арене («остались ли враги») значило бы завести второго судью, и
            // разошлись бы они на добивании: у гостя показ идёт с лагом.
            _endedSubscription = _endedSub?.Subscribe(e =>
                _lastVictory = e.Outcome.IsWinFor(_localPlayer?.Team ?? 0));

            if (!_transport.IsRunning) return;

            _transport.Send(NetPeer.HostPeerId,
                NetEnvelope.Wrap(NetChannel.ActivityState, default, ref _envelope),
                NetDelivery.Reliable);
        }

        public void Dispose()
        {
            _transport.MessageReceived -= HandleMessage;
            if (_runs != null) _runs.SnapshotReceived -= HandleSnapshot;
            _endedSubscription?.Dispose();
            // Уходя, снимаем свой ключ: гейт живёт в сеансе и переживёт нас, а брошенный ключ показал бы
            // счёт согласия там, где подтверждать уже нечего.
            _ready?.Unbind(Guildmaster.Core.Net.DecisionKeys.BattleContinue);
        }

        private void HandleSnapshot(Guildmaster.Guild.RunState _) => RefreshNodeChoice();

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

            Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Follow,
                $"гость: приехало «где мы» — {state}");
            Apply(in state);
        }

        private void Apply(in ActivityState state)
        {
            if (state.Equals(_applied)) return; // повтор того же — штатно, состояние идемпотентно

            ApplyActivity(in state);
            ApplyBattle(in state);
            ApplyPhase(in state);
            // Итог боя — ПОСЛЕ фазы и ДО записи применённого: он сравнивает новое состояние с прежним,
            // чтобы показать экран один раз, а не на каждое объявление.
            ApplyOutcome(in state);
            ApplyMap(in state);

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
                _ready?.Bind(Guildmaster.Core.Net.DecisionKeys.BattleStart, (Action<string>)null);
                session.BindStart(_toggleReady ??= () => _ready?.ToggleLocal());
                Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Ready,
                    $"гость: расстановка — ключ «{Guildmaster.Core.Net.DecisionKeys.BattleStart}» взведён, кнопка привязана (гейт {(_ready == null ? "ОТСУТСТВУЕТ" : "есть")})");
                return;
            }

            _ready?.Unbind(Guildmaster.Core.Net.DecisionKeys.BattleStart);
            session.UnbindStart();
            Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Ready,
                $"гость: фаза {phase} — ключ снят, кнопка отвязана");
        }

        /// <summary>
        /// Итог боя на площадке: показать экран и взвести согласие на возврат к расстановке.
        /// </summary>
        /// <remarks>
        /// <b>Второй ключ гейта у гостя не взводил никто</b> — и кнопки «Продолжить» у него не
        /// появлялось вовсе (наход. Макса 07.08.2026). У хозяина ключ ставит расстановка
        /// (<c>DeploymentController.ShowGroundsOutcome</c>), а её у гостя нет: бой приезжает ему
        /// скоупом-приёмником. Ровно та же дыра, что закрывали для «Начать» 04.08.2026, — просто
        /// вторая её половина.
        /// <para><b>Момент — та же пара «место плюс фаза», по которой его берёт хозяин:</b> площадка
        /// и <c>Interlude</c>. Заводить для показа отдельное сообщение по сети значило бы завести
        /// второго владельца момента, который умеет разойтись с первым.</para>
        /// <para><b>«Продолжить» — согласие, а не команда.</b> Кнопка шлёт «я готов», экран закрывает
        /// признак срабатывания от гейта, а к расстановке всех возвращает хозяин: у гостя нет
        /// расстановки, которую можно было бы вернуть.</para>
        /// <para><b>«В меню» у гостя значит выйти из ЧУЖОЙ игры.</b> Прерывать нечего — своего забега
        /// нет; уходя, гость покидает сеанс, и верхняя петля сама возвращает его в своё меню.</para>
        /// </remarks>
        private void ApplyOutcome(in ActivityState state)
        {
            bool showing = state.Kind == ActivityKind.ProvingGrounds && state.Phase == BattlePhase.Interlude;
            bool wasShowing = _applied.Kind == ActivityKind.ProvingGrounds
                              && _applied.Phase == BattlePhase.Interlude;
            if (showing == wasShowing) return;

            if (!showing)
            {
                _ready?.Unbind(Guildmaster.Core.Net.DecisionKeys.BattleContinue);
                return;
            }

            // Действия у ключа нет намеренно: собранное согласие исполняет хозяин, а гостю приезжает
            // признак срабатывания — по нему экран и закрывается.
            _ready?.Bind(Guildmaster.Core.Net.DecisionKeys.BattleContinue, (Action<string>)null);
            Guildmaster.Core.Diagnostics.Diag.Log(Guildmaster.Core.Diagnostics.DiagChannel.Ready,
                $"гость: итог боя — ключ «{Guildmaster.Core.Net.DecisionKeys.BattleContinue}» взведён, показываю экран (победа: {_lastVictory})");

            _outcomePub?.Publish(new Guildmaster.Guild.OpenOutcomeRequest(
                _lastVictory,
                onToMenu:   () => _coop?.Leave(),
                onContinue: () => _ready?.ToggleLocal()));
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
        /// Зажечь или погасить достижимые узлы — по состоянию забега, а не по объявлению.
        /// </summary>
        /// <remarks>
        /// <b>Гость выбирает наравне</b> (решение Макса 04.08.2026): «все игроки полноправные и могут
        /// голосовать за карту в будущем. Пока, просто тыкать и выбирать». До этого у него карта
        /// открывалась, но узлы не горели и клик ничего не делал: момент ожидания жил в стеке петли
        /// акта, а её у гостя нет.
        /// <para><b>По сети сюда не едет ничего</b>, и это главное в устройстве. «Ждём выбора» — это
        /// «поле входа пусто и акт не завершён», достижимые — <c>MapTraversal.AvailableNext</c>; обе
        /// вещи считаются из снимка карты, который у гостя и так есть. Объявляй мы их отдельно, у
        /// подсветки появился бы второй владелец, умеющий разойтись с картой под ней.</para>
        /// <para><b>Клик отсюда никуда не отправляется:</b> его шлёт сама карта, командой в шину — той
        /// же, что и у хозяина.</para>
        /// </remarks>
        private void RefreshNodeChoice()
        {
            if (_map == null) return;

            Guildmaster.Guild.MapState map = _runs?.Current?.Map;
            bool waiting = map != null
                           && string.IsNullOrEmpty(map.EnteringNodeId)
                           && !Guildmaster.Guild.MapTraversal.IsActComplete(map);

            if (!waiting)
            {
                if (_map.IsChoosing) _map.EndChoose();
                return;
            }

            // Карту здесь не открываем (show: false): её открытость объявлена отдельным полем, и второй
            // хозяин видимости спорил бы с первым.
            _map.BeginChoose(Guildmaster.Guild.MapTraversal.AvailableNext(map), show: false);
        }
    }
}
