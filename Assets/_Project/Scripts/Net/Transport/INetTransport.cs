using System;

namespace Guildmaster.Net.Transport
{
    /// <summary>Надёжность доставки. Всё, что тоньше, транспорту знать незачем.</summary>
    public enum NetDelivery
    {
        /// <summary>Дойдёт и в порядке. Для команд забега и чанков ленты.</summary>
        Reliable = 0,

        /// <summary>Может не дойти. Для присутствия: потерянный курсор экстраполируется, и никто не заметит.</summary>
        Unreliable = 1,
    }

    /// <summary>
    /// Шов транспорта: отправить байты, получить байты, узнать о приходе и уходе пиров.
    /// <para><b>Зачем шов, если NGO и так абстракция.</b> Затем, что за этим интерфейсом живёт не только
    /// релизный Facepunch/Steam: **loopback** даёт весь кооп-код в EditMode без сети и сцен, а
    /// **chaos-обёртка** воспроизводит потерю, задержку и переупорядочивание ПО СИДУ. Без этого отладка
    /// коопа выглядит как «поднять два инстанса и ждать, пока баг случится сам» — по внешним
    /// свидетельствам это вторая по величине статья расходов мультиплеера после самой сессии.</para>
    /// <para><b>Заложен сразу, а не потом,</b> потому что Steam-транспорт, приросший к коду, лишает нас
    /// отладки на годы: Network Simulator из Multiplayer Tools работает только с UTP, и подменяемым
    /// транспорт обязан быть с первого дня.</para>
    /// </summary>
    public interface INetTransport
    {
        /// <summary>Поднят ли транспорт (соединение живо).</summary>
        bool IsRunning { get; }

        /// <summary>Наш id в сессии. У хоста — <see cref="HostPeerId"/>.</summary>
        int LocalPeerId { get; }

        /// <summary>Мы ли авторитет. Всё, что решает исход, считает хост.</summary>
        bool IsHost { get; }

        /// <summary>
        /// Предел размера одного НАДЁЖНОГО сообщения, байт. Существует в интерфейсе, потому что у нашего
        /// релизного транспорта его не проверяет никто: <c>FacepunchTransport</c> отдаёт данные прямо в
        /// <c>Connection.SendMessage</c>, своей фрагментации не имеет, а Steam сверх 512 КБ возвращает
        /// <c>InvalidParam</c>, который транспорт не читает — сообщение уезжает в тишину. Поэтому предел
        /// спрашивает и проверяет НАШ код (чанки ленты), а не «оно как-нибудь фрагментируется».
        /// </summary>
        int MaxReliableMessageBytes { get; }

        /// <summary>Пир подключился (у хоста — на каждого гостя, у гостя — на хоста).</summary>
        event Action<int> PeerConnected;

        /// <summary>Пир отключился. Уход хоста для гостя означает конец сессии (решение 01.08.2026).</summary>
        event Action<int> PeerDisconnected;

        /// <summary>
        /// Пришло сообщение: от кого и что. <b>Буфер живёт только на время вызова</b> — кому нужно
        /// сохранить, копирует. Так приёмник не заставляет транспорт выделять массив на каждое сообщение.
        /// </summary>
        event Action<int, ArraySegment<byte>> MessageReceived;

        /// <summary>Отправить одному пиру.</summary>
        void Send(int peerId, ArraySegment<byte> payload, NetDelivery delivery);

        /// <summary>Отправить всем, кроме себя.</summary>
        void SendToAll(ArraySegment<byte> payload, NetDelivery delivery);

        /// <summary>
        /// Прокачать входящие: транспорт поднимает события только здесь, и это не деталь реализации, а
        /// требование. Доставка, случающаяся «когда придёт», делает тест недетерминированным — а весь
        /// смысл этого шва в том, чтобы падение воспроизводилось по сиду.
        /// </summary>
        void Poll();

        /// <summary>Закрыть соединение и отписаться.</summary>
        void Shutdown();
    }

    /// <summary>Общие числа шва.</summary>
    public static class NetPeer
    {
        /// <summary>Id хоста. Авторитет всегда у него.</summary>
        public const int HostPeerId = 0;

        /// <summary>«Пира нет» — например, отправка до подключения.</summary>
        public const int NoPeer = -1;
    }
}
