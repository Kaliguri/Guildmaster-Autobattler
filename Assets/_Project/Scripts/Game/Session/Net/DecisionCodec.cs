using System;
using System.Collections.Generic;
using Guildmaster.Core.Net;
using Guildmaster.Net;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Общее согласие в байтах и обратно: голос участника и объявленный счёт.
    /// </summary>
    /// <remarks>
    /// <b>Заведён 08.08.2026 вместе с кодеком состава и по той же причине.</b> Формат канала
    /// <see cref="NetChannel.Decision"/> жил в трёх местах — писатель у хозяина, читатель у гостя и
    /// третий разбор в тесте, — то есть инвариант «что записано, то и читается» держался ничем, кроме
    /// внимательности. Соседний канал с такой же раскладкой уже разъехался и унёс с собой весь состав
    /// сеанса; здесь просто не успело.
    /// <para><b>Первый байт объявляет, чьё это сообщение</b> (<see cref="DecisionWire"/>). Раньше
    /// стороны различались по ДЛИНЕ пакета, и такая развилка держалась ровно до первого изменения
    /// формата — голос стал строкой варианта, и длины перестали быть разными.</para>
    /// <para><b>Голоса едут поимённо, а не числом:</b> счёт из них выводится, а обратно — нет, и показу
    /// нужно именно «кто за что». Один владелец факта вместо двух согласованных чисел.</para>
    /// </remarks>
    public static class DecisionCodec
    {
        /// <summary>Сколько голосов влезает в объявление: счётчик едет одним байтом.</summary>
        private const int MaxChoices = 255;

        /// <summary>Голос ли это. Ложь на объявлении счёта — то есть на собственном эхе у хозяина.</summary>
        public static bool IsVote(ArraySegment<byte> payload) => HasTag(payload, DecisionWire.Vote);

        /// <summary>Объявление ли это. Ложь на голосе другого участника — он адресован не нам.</summary>
        public static bool IsTally(ArraySegment<byte> payload) => HasTag(payload, DecisionWire.Tally);

        /// <summary>Отдать свой голос: участник → хозяин.</summary>
        public static ArraySegment<byte> WriteVote(string option, NetByteWriter writer)
        {
            writer.Reset();
            writer.WriteByte(DecisionWire.Vote);
            writer.WriteString(option);
            return writer.WrittenSegment;
        }

        /// <summary>
        /// Разобрать голос. <c>false</c> — это не голос или пакет не разобрался целиком.
        /// </summary>
        public static bool TryReadVote(ArraySegment<byte> payload, out string option)
        {
            option = DecisionOptions.None;

            var bytes = new NetByteReader(payload);

            try
            {
                if (bytes.ReadByte() != DecisionWire.Vote) return false;
                option = bytes.ReadString();
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            return !bytes.HasMore;
        }

        /// <summary>Объявить счёт: хозяин → всем.</summary>
        public static ArraySegment<byte> WriteTally(string key, int required, bool fired,
                                                    IReadOnlyList<PlayerChoice> choices, NetByteWriter writer)
        {
            writer.Reset();
            writer.WriteByte(DecisionWire.Tally);
            writer.WriteByte((byte)Math.Clamp(required, 0, 255));
            writer.WriteBool(fired);

            // Ключ едет строкой, а не номером: он же и есть смысл действия, а таблица номеров разошлась
            // бы между сборками ровно так, как расходятся все таблицы, которые ведут руками.
            writer.WriteString(key);

            int count = choices == null ? 0 : Math.Min(choices.Count, MaxChoices);
            writer.WriteByte((byte)count);

            for (int i = 0; i < count; i++)
            {
                writer.WriteByte((byte)choices[i].PlayerId);
                writer.WriteString(choices[i].Option);
            }

            return writer.WrittenSegment;
        }

        /// <summary>
        /// Разобрать объявленный счёт в <paramref name="into"/>. <c>false</c> — это не объявление или
        /// пакет не разобрался целиком; тогда прежний счёт честнее половины нового.
        /// </summary>
        public static bool TryReadTally(ArraySegment<byte> payload, List<PlayerChoice> into,
                                        out string key, out int required, out bool fired)
        {
            key      = string.Empty;
            required = 0;
            fired    = false;

            if (into == null) return false;

            var bytes  = new NetByteReader(payload);
            var parsed = new List<PlayerChoice>(4);

            string parsedKey;
            int    parsedRequired;
            bool   parsedFired;

            try
            {
                if (bytes.ReadByte() != DecisionWire.Tally) return false;

                parsedRequired = bytes.ReadByte();
                parsedFired    = bytes.ReadBool();
                parsedKey      = bytes.ReadString();

                int count = bytes.ReadByte();
                for (int i = 0; i < count; i++)
                {
                    int    voter  = bytes.ReadByte();
                    string option = bytes.ReadString();
                    parsed.Add(new PlayerChoice(voter, option));
                }
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (bytes.HasMore) return false;

            into.Clear();
            into.AddRange(parsed);

            key      = parsedKey;
            required = parsedRequired;
            fired    = parsedFired;
            return true;
        }

        private static bool HasTag(ArraySegment<byte> payload, byte tag) =>
            payload.Count >= 1 && payload.Array != null && payload.Array[payload.Offset] == tag;
    }
}
