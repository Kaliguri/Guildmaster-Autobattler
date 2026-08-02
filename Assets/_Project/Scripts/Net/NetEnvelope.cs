using System;

namespace Guildmaster.Net
{
    /// <summary>
    /// Кто говорит в сообщении. Транспорт у нас один на всё, поэтому первый байт любого пакета
    /// объявляет канал.
    /// </summary>
    /// <remarks>
    /// Значения назначены явно и <b>не меняются</b>: канал едет по сети, и перенумерация развела бы
    /// сборки разных версий молча — гость читал бы чанк ленты как присутствие. Новый канал получает
    /// следующий свободный номер, освободившийся не переиспользуется.
    /// </remarks>
    public enum NetChannel : byte
    {
        /// <summary>Чанк боевой ленты: хост → гости.</summary>
        TapeChunk = 1,

        /// <summary>Запрос повтора чанка по номеру: гость → хост.</summary>
        TapeResend = 2,

        /// <summary>Присутствие (курсоры). Ненадёжный канал.</summary>
        Presence = 3,

        /// <summary>Команда забега.</summary>
        RunCommand = 4,

        /// <summary>Управление боем: общая пауза и возобновление.</summary>
        BattleControl = 5,

        /// <summary>Рукопожатие: версия, отпечаток контента, назначенный номер пира.</summary>
        Handshake = 6,

        /// <summary>Состав боя: кто вышел на арену. Хост → гости, по одному паспорту на спавн.</summary>
        BattleRoster = 7,
    }

    /// <summary>
    /// Конверт сообщения: один байт канала спереди, дальше полезная нагрузка как есть.
    /// </summary>
    /// <remarks>
    /// <b>Почему канал, а не отдельные транспорты.</b> У Steam-соединения канал один, и разводить
    /// потоки всё равно пришлось бы нам. Единственный владелец правила «какой байт что значит» —
    /// этот класс: разбор, размазанный по подписчикам, расходится на первом же новом канале.
    /// <para><b>Буфер отдаётся вызывающему.</b> Обёртка не заводит своего кольца буферов: у
    /// отправителей (кодек чанка, кодек присутствия) уже есть переиспользуемые массивы, и второй слой
    /// копий стоил бы аллокации на каждый пакет ради нулевого выигрыша.</para>
    /// </remarks>
    public static class NetEnvelope
    {
        /// <summary>Длина заголовка конверта.</summary>
        public const int HeaderBytes = 1;

        /// <summary>
        /// Уложить нагрузку в конверт. <paramref name="buffer"/> переиспользуется между вызовами и
        /// растёт по необходимости — держать его полагается отправителю, по одному на канал.
        /// </summary>
        public static ArraySegment<byte> Wrap(NetChannel channel, ArraySegment<byte> payload, ref byte[] buffer)
        {
            int total = HeaderBytes + payload.Count;
            if (buffer == null || buffer.Length < total) buffer = new byte[Math.Max(total, 256)];

            buffer[0] = (byte)channel;
            if (payload.Count > 0)
                Array.Copy(payload.Array, payload.Offset, buffer, HeaderBytes, payload.Count);

            return new ArraySegment<byte>(buffer, 0, total);
        }

        /// <summary>
        /// Разобрать конверт. Возвращает false на пустом сообщении и на неизвестном канале: чужой
        /// канал — это расхождение версий, и молча съеденный пакет искался бы по отсутствию картинки.
        /// </summary>
        public static bool TryUnwrap(ArraySegment<byte> message, out NetChannel channel, out ArraySegment<byte> payload)
        {
            channel = default;
            payload = default;

            if (message.Count < HeaderBytes) return false;

            byte raw = message.Array[message.Offset];
            if (!IsKnown(raw)) return false;

            channel = (NetChannel)raw;
            payload = new ArraySegment<byte>(
                message.Array, message.Offset + HeaderBytes, message.Count - HeaderBytes);
            return true;
        }

        // Таблица известных каналов ВЫВОДИТСЯ из перечисления, а не переписывается рядом с ним.
        // Рукописный список тут уже стоил захода: канал завели, в список не дописали, и сообщение
        // молча не доходило — ровно та поломка, от которой этот метод и должен защищать. Владелец
        // факта «какие каналы бывают» ровно один — сам NetChannel.
        private static readonly bool[] Known = BuildKnown();

        private static bool[] BuildKnown()
        {
            var known = new bool[256];
            foreach (NetChannel channel in (NetChannel[])Enum.GetValues(typeof(NetChannel)))
                known[(byte)channel] = true;
            return known;
        }

        private static bool IsKnown(byte raw) => Known[raw];
    }
}
