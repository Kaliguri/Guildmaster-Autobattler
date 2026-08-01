using System;
using System.Collections.Generic;
using Guildmaster.Net.Transport;

namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Приёмная сторона раздачи: складывает приходящие чанки в свою ленту и просит повторить те, что
    /// не доехали.
    /// </summary>
    /// <remarks>
    /// <b>Порядок прихода не важен.</b> Чанк самодостаточен — дельта считается только внутри него, —
    /// поэтому переупорядочивание не портит ничего, и склейка сводится к «прочитать и уложить».
    /// Дыра при этом всё равно видна: нумерация непрерывна, и пропущенный номер не закроется сам.
    /// <para><b>Время подаёт вызывающий</b> (как у отправителя присутствия). Свой таймер сделал бы
    /// повторные запросы недетерминированными, а весь смысл chaos-слоя в том, чтобы падение
    /// воспроизводилось по сиду, а не «иногда на двух копиях».</para>
    /// <para><b>Отказ громкий.</b> Битый чанк, чужая версия формата и неизвестный id контента не
    /// проглатываются: показ у гостя после них разойдётся с хостом, и молчание здесь стоило бы поиска
    /// по симптому «у второго игрока другая картинка».</para>
    /// </remarks>
    public sealed class TapeIntake
    {
        /// <summary>Сколько ждать перед повторной просьбой о том же чанке, секунд.</summary>
        public const float DefaultRetrySeconds = 0.5f;

        private readonly INetTransport   _transport;
        private readonly TapeChunkReader _reader;

        private readonly HashSet<int>            _seen  = new HashSet<int>();
        private readonly Dictionary<int, float>  _asked = new Dictionary<int, float>();
        private readonly List<int>               _missingScratch = new List<int>();

        private byte[] _envelope;
        private int    _highest = -1;

        public TapeIntake(INetTransport transport, TapeChunkReader reader)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _reader    = reader    ?? throw new ArgumentNullException(nameof(reader));

            _transport.MessageReceived += HandleMessage;
        }

        /// <summary>Сколько чанков уложено в ленту.</summary>
        public int AppliedChunkCount { get; private set; }

        /// <summary>Сколько запросов повтора отправлено — метрика качества канала.</summary>
        public int ResendRequestCount { get; private set; }

        /// <summary>Чанк отвергнут: причина и текст. Слушает сессия — это кандидат на разрыв.</summary>
        public event Action<TapeChunkStatus, string> ChunkRejected;

        /// <summary>Сколько номеров между первым и последним пришедшим ещё не доехало.</summary>
        public int MissingCount
        {
            get
            {
                int missing = 0;
                for (int n = 0; n <= _highest; n++)
                    if (!_seen.Contains(n)) missing++;
                return missing;
            }
        }

        /// <summary>
        /// Попросить хоста повторить чанки, которых не хватает. Зовётся раз в кадр; повтор одного и того
        /// же номера уходит не чаще <paramref name="retrySeconds"/>, иначе на просевшем канале запросы
        /// сами станут нагрузкой.
        /// </summary>
        public void RequestMissing(float now, float retrySeconds = DefaultRetrySeconds)
        {
            _missingScratch.Clear();
            for (int n = 0; n <= _highest; n++)
                if (!_seen.Contains(n)) _missingScratch.Add(n);

            for (int i = 0; i < _missingScratch.Count; i++)
            {
                int number = _missingScratch[i];
                if (_asked.TryGetValue(number, out float last) && now - last < retrySeconds) continue;

                _asked[number] = now;
                _transport.Send(NetPeer.HostPeerId,
                    NetEnvelope.Wrap(NetChannel.TapeResend, new ArraySegment<byte>(BitConverter.GetBytes(number)), ref _envelope),
                    NetDelivery.Reliable);
                ResendRequestCount++;
            }
        }

        /// <summary>Новый бой: чанки нумеруются заново, поэтому забываем всё, что видели.</summary>
        public void Reset()
        {
            _seen.Clear();
            _asked.Clear();
            _highest = -1;
            AppliedChunkCount  = 0;
            ResendRequestCount = 0;
            _reader.Reset();
        }

        /// <summary>Отписаться от транспорта.</summary>
        public void Dispose() => _transport.MessageReceived -= HandleMessage;

        private void HandleMessage(int from, ArraySegment<byte> message)
        {
            if (!NetEnvelope.TryUnwrap(message, out NetChannel channel, out ArraySegment<byte> payload)) return;
            if (channel != NetChannel.TapeChunk) return;

            TapeChunkStatus status = _reader.Read(payload);
            switch (status)
            {
                case TapeChunkStatus.Ok:
                    int number = _reader.LastChunkNumber;
                    _seen.Add(number);
                    _asked.Remove(number);
                    if (number > _highest) _highest = number;
                    AppliedChunkCount++;
                    return;

                // Дубль — штатное следствие повтора и реконнекта: чанк уже лежит в ленте, и это не
                // новость ни для кого.
                case TapeChunkStatus.Duplicate:
                    return;

                default:
                    ChunkRejected?.Invoke(status, _reader.LastError);
                    return;
            }
        }
    }
}
