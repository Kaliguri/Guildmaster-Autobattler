using System;

namespace Guildmaster.Net.Session
{
    /// <summary>
    /// Комната для игры без платформы: живёт в своём процессе, никого никуда не зовёт и честно об
    /// этом говорит.
    /// </summary>
    /// <remarks>
    /// Поднимается там же, где петлевой транспорт, — когда Steam не запущен или идёт автоматический
    /// прогон. Существует ради одного: чтобы сеанс можно было провести по всему пути — поднять,
    /// принять напарника, потерять его — не выходя из одного процесса.
    /// <para><b>Оверлеи молчат намеренно.</b> Позвать друга без платформы нельзя, и делать вид, что
    /// кнопка сработала, — худшее из возможных поведений: игрок ждал бы гостя, которого никто не
    /// звал.</para>
    /// </remarks>
    public sealed class LoopbackLobby : ICoopLobby
    {
        /// <summary>Платформы нет — и это не скрывается: кнопки приглашения гаснут сами.</summary>
        public bool IsSteamReady => false;

        public bool HasLobby { get; private set; }

        public event Action<ulong, ulong> JoinRequested;

        /// <inheritdoc />
        /// <remarks>В петле приглашений не бывает: звать сюда некого, все узлы свои.</remarks>
#pragma warning disable 67 // Событие шва: петля никого не зовёт и поднять его не может — см. remarks выше.
        public event Action<string, ulong, ulong> Invited;
#pragma warning restore 67
        public event Action LobbyChanged;

        public void CreateLobby()
        {
            HasLobby = true;
            LobbyChanged?.Invoke();
        }

        public void OpenInviteOverlay() { }
        public void OpenFriendsOverlay() { }

        public void LeaveLobby()
        {
            if (!HasLobby) return;
            HasLobby = false;
            LobbyChanged?.Invoke();
        }

        /// <summary>
        /// Позвать самих себя: то же событие, что приходит от платформы, когда друг принял
        /// приглашение. Единственный способ провести гостевую половину сеанса без второй машины.
        /// </summary>
        public void SimulateInvite(ulong lobbyId = 1UL, ulong hostAddress = 1UL) =>
            JoinRequested?.Invoke(lobbyId, hostAddress);
    }
}
