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
    public sealed class SteamLobbyService : IDisposable
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

        /// <summary>Мы вошли в лобби (своё или чужое).</summary>
        public event Action<ulong> LobbyEntered;

        /// <summary>
        /// Создать лобби под текущую сессию. Возвращает false, если Steam не запущен — это внешний
        /// отказ, и он должен быть виден игроку, а не заглажен.
        /// </summary>
        public async void CreateLobby()
        {
            if (!IsSteamReady)
            {
                Debug.LogWarning("[SteamLobbyService] Steam не запущен — лобби не создать, приглашения недоступны");
                return;
            }

            Lobby? created = await SteamMatchmaking.CreateLobbyAsync(MaxMembers);
            if (!created.HasValue)
            {
                Debug.LogWarning("[SteamLobbyService] Steam не создал лобби");
                return;
            }

            _lobby = created;
            created.Value.SetFriendsOnly(); // список комнат нам не нужен: вход только по приглашению
            created.Value.SetJoinable(true);
        }

        /// <summary>Открыть оверлей приглашений на текущее лобби.</summary>
        public void OpenInviteOverlay()
        {
            if (!IsSteamReady || !_lobby.HasValue) return;
            SteamFriends.OpenGameInviteOverlay(_lobby.Value.Id);
        }

        /// <summary>Закрыть лобби (конец сессии).</summary>
        public void LeaveLobby()
        {
            if (!_lobby.HasValue) return;
            _lobby.Value.Leave();
            _lobby = null;
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

        private void HandleLobbyEntered(Lobby lobby)
        {
            _lobby = lobby;
            LobbyEntered?.Invoke(lobby.Id);
        }

        private void HandleJoinRequested(Lobby lobby, SteamId host) =>
            JoinRequested?.Invoke(lobby.Id, host.Value);
    }
}
