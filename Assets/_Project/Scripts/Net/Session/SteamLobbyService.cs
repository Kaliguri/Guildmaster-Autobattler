using System;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace Guildmaster.Net.Session
{
    /// <summary>
    /// Лобби Steam: создать, позвать друга оверлеем, узнать, что нас позвали.
    /// </summary>
    /// <remarks>
    /// <b>Лобби у нас — не комната со списком, а адрес приглашения.</b> Игрок создаёт игру одним кликом
    /// (ни названия, ни настроек — решение Макса 02.08.2026) и зовёт друга; списка комнат нет вовсе.
    /// Поэтому лобби здесь нужно ровно за двумя вещами: чтобы Steam знал, куда вести приглашённого, и
    /// чтобы оверлей друзей показывал «Присоединиться».
    /// <para><b>Steam может быть не запущен</b> — это внешний отказ, и он честно виден: лобби не
    /// создаётся, кнопка приглашения гаснет. Тихо подменять его чем-то своим нельзя, иначе игрок жмёт
    /// кнопку, которая ничего не делает.</para>
    /// <para><b>Вход по приглашению замкнётся вместе со Steam-транспортом.</b> Событие
    /// <see cref="JoinRequested"/> уже приходит, но вести приглашённого пока некуда: транспорт поверх
    /// SteamNetworkingSockets в проекте не заведён (`FacepunchTransportBootstrap` только инициализирует
    /// Steam, вопреки своему докстрингу). До этого момента подписчик получает id лобби и решает сам.</para>
    /// </remarks>
    public sealed class SteamLobbyService : ICoopLobby, IDisposable
    {
        /// <summary>Сколько мест в лобби. Кооп у нас до четырёх (дизайн).</summary>
        public const int MaxMembers = 4;

        private Lobby? _lobby;

        public SteamLobbyService()
        {
            SteamMatchmaking.OnLobbyCreated       += HandleLobbyCreated;
            SteamMatchmaking.OnLobbyEntered       += HandleLobbyEntered;
            SteamFriends.OnGameLobbyJoinRequested += HandleJoinRequested;
        }

        /// <summary>Steam на связи: клиент запущен и инициализирован.</summary>
        public bool IsSteamReady => SteamClient.IsValid;

        /// <summary>Есть ли поднятое лобби, в которое можно звать.</summary>
        public bool HasLobby => _lobby.HasValue;

        /// <summary>Нас позвали в чужое лобби — id лобби и хозяин. Ведёт в сессию.</summary>
        public event Action<ulong, ulong> JoinRequested;

        /// <summary>
        /// Лобби появилось или исчезло, то есть <see cref="HasLobby"/> сменилось.
        /// </summary>
        /// <remarks>
        /// <b>Создание лобби асинхронно, а спрашивают о нём синхронно.</b> <see cref="CreateLobby"/>
        /// возвращается сразу, лобби приходит от Steam кадры спустя — и всё это время «есть кого
        /// звать» отвечает <c>false</c>. Без этого события кнопка приглашения гасла навсегда: экран
        /// перечитывал состояние только на смену состояния сессии, а она случалась ДО лобби. Оживало
        /// оно лишь перезаходом на экран — то есть выглядело как «работает со второго раза».
        /// </remarks>
        public event Action LobbyChanged;

        /// <summary>
        /// Создать лобби под текущую сессию. Возвращается сразу: лобби придёт от Steam кадры спустя и
        /// объявит о себе через <see cref="LobbyChanged"/>.
        /// </summary>
        /// <remarks>
        /// Не создалось — предупреждение в лог и никакого события; это внешний отказ, и он должен быть
        /// виден, а не заглажен. Вызывающему возвращать нечего: к моменту возврата ответа Steam ещё нет.
        /// </remarks>
        /// <remarks>
        /// Метод <c>async void</c>, и это вынужденно: его зовут как обычное действие из UI, а сборка
        /// <c>Guildmaster.Net</c> на UniTask не ссылается, так что <c>UniTaskVoid</c> здесь недоступен.
        /// Цена такого метода — необработанное исключение уходит в никуда мимо всякого лога; поэтому
        /// ожидание под <c>try</c>. Отказ прилетает из сетевого вызова Steam, то есть ровно оттуда, где
        /// он ожидаем, и увидеть его надо в консоли, а не по молчанию кнопки приглашения.
        /// </remarks>
        public async void CreateLobby()
        {
            if (!IsSteamReady)
            {
                Debug.LogWarning("[SteamLobbyService] Steam не запущен — лобби не создать, приглашения недоступны");
                return;
            }

            try
            {
                Lobby? created = await SteamMatchmaking.CreateLobbyAsync(MaxMembers);
                if (!created.HasValue)
                {
                    Debug.LogWarning("[SteamLobbyService] Steam не создал лобби");
                    return;
                }

                created.Value.SetFriendsOnly(); // список комнат нам не нужен: вход только по приглашению
                created.Value.SetJoinable(true);
                SetLobby(created);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SteamLobbyService] Steam не отдал лобби: {e}");
            }
        }

        /// <summary>Открыть оверлей приглашений на текущее лобби.</summary>
        public void OpenInviteOverlay()
        {
            if (!IsSteamReady || !_lobby.HasValue) return;
            SteamFriends.OpenGameInviteOverlay(_lobby.Value.Id);
        }

        /// <summary>
        /// Открыть список друзей: оттуда игрок входит в чужую игру (ПКМ по другу → присоединиться).
        /// </summary>
        /// <remarks>
        /// Своего лобби для этого не нужно — мы не зовём, а ищем, куда пойти. Именно поэтому вызов
        /// живёт рядом с приглашением, но не требует <see cref="HasLobby"/>.
        /// </remarks>
        public void OpenFriendsOverlay()
        {
            if (!IsSteamReady) return;
            SteamFriends.OpenOverlay("friends");
        }

        /// <summary>Закрыть лобби (конец сессии).</summary>
        public void LeaveLobby()
        {
            if (!_lobby.HasValue) return;
            _lobby.Value.Leave();
            SetLobby(null);
        }

        public void Dispose()
        {
            LeaveLobby();
            SteamMatchmaking.OnLobbyCreated       -= HandleLobbyCreated;
            SteamMatchmaking.OnLobbyEntered       -= HandleLobbyEntered;
            SteamFriends.OnGameLobbyJoinRequested -= HandleJoinRequested;
        }

        private void HandleLobbyCreated(Result result, Lobby lobby)
        {
            if (result == Result.OK) return;
            Debug.LogWarning($"[SteamLobbyService] лобби не создалось: {result}");
        }

        private void HandleLobbyEntered(Lobby lobby) => SetLobby(lobby);

        /// <summary>
        /// Единственная точка, где меняется лобби: чтение (<see cref="HasLobby"/>) и правка ходят
        /// через неё, поэтому событие не может разъехаться с состоянием. Молчим, когда наличие не
        /// изменилось — создатель входит в собственное лобби, и об одном событии Steam сообщает дважды.
        /// </summary>
        private void SetLobby(Lobby? lobby)
        {
            bool had = _lobby.HasValue;
            _lobby = lobby;
            if (had != _lobby.HasValue) LobbyChanged?.Invoke();
        }

        private void HandleJoinRequested(Lobby lobby, SteamId host) =>
            JoinRequested?.Invoke(lobby.Id, host.Value);
    }
}
