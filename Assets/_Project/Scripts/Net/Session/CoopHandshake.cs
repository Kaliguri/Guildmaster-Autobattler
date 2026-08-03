using System;
using System.Text;
using Guildmaster.Data;
using Guildmaster.Net.Transport;

namespace Guildmaster.Net.Session
{
    /// <summary>
    /// Рукопожатие сессии: гость представляется версией и отпечатком контента, хост либо принимает его и
    /// выдаёт номер пира, либо отказывает с внятной причиной.
    /// </summary>
    /// <remarks>
    /// <b>Живёт на нашем уровне, а не в транспорте</b> (решение Макса 02.08.2026, когда сетевой стек
    /// сузился до Steam). Раньше эту работу делал <c>ConnectionApproval</c> из NGO; вместе с NGO ушла бы
    /// и проверка, а без неё расхождение контента всплывает не отказом, а сломанным показом боя: чанк
    /// ленты несёт строковые id, и неизвестный id роняет картинку у гостя.
    /// <para><b>Номер пира выдаёт хост.</b> До этого гость своего номера не знает: у Steam есть SteamId,
    /// но он не наш адрес в сессии, а сама сессия нумерует участников подряд. Отсюда порядок: соединение
    /// есть → рукопожатие прошло → только теперь мы «в сессии».</para>
    /// <para><b>Отказ приходит текстом.</b> Молчаливый разрыв игрок читает как «не работает интернет» и
    /// идёт не за патчем, а за отзывом.</para>
    /// </remarks>
    public sealed class CoopHandshake
    {
        private const byte KindHello   = 0; // гость → хост
        private const byte KindWelcome = 1; // хост → гость: принят, вот твой номер
        private const byte KindReject  = 2; // хост → гость: причина отказа

        private readonly INetTransport      _transport;
        private readonly ContentFingerprint _mine;

        private byte[] _envelope;
        private byte[] _payload = new byte[256];

        public CoopHandshake(INetTransport transport, ContentFingerprint mine)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _mine      = mine;

            _transport.MessageReceived += HandleMessage;
        }

        /// <summary>Нас приняли: вот наш номер в сессии.</summary>
        public event Action<int> Approved;

        /// <summary>Нам отказали: вот причина, её показывают игроку.</summary>
        public event Action<string> Rejected;

        /// <summary>Гость принят хостом — сессия узнаёт о новом участнике.</summary>
        public event Action<int> GuestApproved;

        /// <summary>Гостю отказано: кого и почему — для лога хоста.</summary>
        public event Action<int, string> GuestRejected;

        /// <summary>Представиться хосту. Зовётся гостем, как только соединение поднялось.</summary>
        public void SayHello()
        {
            string body = $"{_mine.ContentHash}|{_mine.ContentCount}|{_mine.SchemaVersion}|{_mine.GameVersion}";
            Send(NetPeer.HostPeerId, KindHello, body);
        }

        public void Dispose() => _transport.MessageReceived -= HandleMessage;

        private void HandleMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.Handshake || payload.Count < 1) return;

            byte kind = payload.Array[payload.Offset];
            string body = payload.Count > 1
                ? Encoding.UTF8.GetString(payload.Array, payload.Offset + 1, payload.Count - 1)
                : string.Empty;

            switch (kind)
            {
                case KindHello:   OnHello(from, body);   return;
                case KindWelcome: OnWelcome(body);       return;
                case KindReject:  Rejected?.Invoke(body); return;
            }
        }

        private void OnHello(int from, string body)
        {
            // Гостю на чужое «привет» отвечать нечем: участников сводит хост, и только он знает номера.
            if (!_transport.IsHost) return;

            if (!TryParse(body, out ContentFingerprint theirs))
            {
                Reject(from, "Непонятное рукопожатие: другая версия игры");
                return;
            }

            if (!_mine.Matches(theirs))
            {
                Reject(from, _mine.DescribeMismatch(theirs));
                return;
            }

            // Номер пира транспорт уже назначил при подключении — рукопожатие его только подтверждает и
            // сообщает гостю. Второй нумератор здесь развёл бы два имени у одного участника.
            Send(from, KindWelcome, from.ToString());
            GuestApproved?.Invoke(from);
        }

        private void OnWelcome(string body)
        {
            if (int.TryParse(body, out int peerId)) Approved?.Invoke(peerId);
            else                                    Rejected?.Invoke("Хост прислал непонятный ответ");
        }

        private void Reject(int peer, string reason)
        {
            Send(peer, KindReject, reason);
            GuestRejected?.Invoke(peer, reason);
        }

        private void Send(int peer, byte kind, string body)
        {
            byte[] text = Encoding.UTF8.GetBytes(body ?? string.Empty);
            int total = 1 + text.Length;
            if (_payload.Length < total) _payload = new byte[total];

            _payload[0] = kind;
            Array.Copy(text, 0, _payload, 1, text.Length);

            _transport.Send(peer,
                NetEnvelope.Wrap(NetChannel.Handshake, new ArraySegment<byte>(_payload, 0, total), ref _envelope),
                NetDelivery.Reliable);
        }

        private static bool TryParse(string body, out ContentFingerprint print)
        {
            print = default;
            if (string.IsNullOrEmpty(body)) return false;

            string[] parts = body.Split('|');
            if (parts.Length != 4) return false;

            if (!ulong.TryParse(parts[0], out ulong hash)) return false;
            if (!int.TryParse(parts[1], out int count))    return false;
            if (!int.TryParse(parts[2], out int schema))   return false;

            print = new ContentFingerprint(hash, count, schema, parts[3]);
            return true;
        }
    }
}
