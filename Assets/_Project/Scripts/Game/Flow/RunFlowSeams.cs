using Cysharp.Threading.Tasks;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Гейт готовности игроков (вики «7» §10, план 11 §2): переходы, ждущие всех. Соло-тело возвращается
    /// мгновенно; NGO-тело (Фаза 6) ждёт всех через сеть. Шов есть — реализации нет.
    /// </summary>
    public interface ISharedDecision
    {
        UniTask WhenAllReady();
    }

    /// <summary>Соло: готовность — мгновенно (один локальный игрок).</summary>
    public sealed class SoloReadyGate : ISharedDecision
    {
        public UniTask WhenAllReady() => UniTask.CompletedTask;
    }

    /// <summary>
    /// Источник интентов игрока (план 11 §2): хост исполняет флоу, клиенты шлют интенты. Соло —
    /// локальный ввод, авторитет всегда у нас. Тело интентов наполняется по мере узлов (выбор на карте,
    /// выбор в ивенте — B1/B3); сетевая репликация — Фаза 6.
    /// </summary>
    public interface IPlayerIntentSource
    {
        /// <summary>Мы ли авторитет исполнения (host). Соло — всегда true.</summary>
        bool IsLocalAuthority { get; }
    }

    /// <summary>Соло: локальный игрок — всегда авторитет.</summary>
    public sealed class SoloPlayerIntentSource : IPlayerIntentSource
    {
        public bool IsLocalAuthority => true;
    }

    /// <summary>
    /// Гость: авторитета нет никогда. Решения по забегу принимает владелец состояния, гость их только
    /// просит — и это не «ограничение прав», а единственный способ не разойтись состояниями.
    /// </summary>
    public sealed class GuestPlayerIntentSource : IPlayerIntentSource
    {
        public bool IsLocalAuthority => false;
    }
}
