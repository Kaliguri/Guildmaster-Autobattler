using System;
using System.Collections.Generic;
using Guildmaster.Net.Tape;
using UnityEngine;

namespace Guildmaster.Net.Presence
{
    /// <summary>
    /// Упаковка присутствия: все курсоры — ОДНИМ склеенным пакетом, ненадёжным каналом.
    /// <para><b>Почему склейка.</b> Так делает Figma: presence шлётся по тому же каналу, что и правки, но
    /// склеенная и никогда не попадающая в журнал. Отдельный пакет на курсор при четверых игроках и 128 Гц
    /// давал бы четыреста посылок в секунду ради двадцати байт каждая — сериализация и очереди стоят
    /// дороже самих данных.</para>
    /// <para><b>Почему ненадёжным.</b> Потерянный курсор не нужно доставлять: к моменту, когда повтор
    /// доедет, он уже устарел, а приёмник тем временем экстраполировал позицию по скорости — и никто не
    /// заметил. Надёжная доставка присутствия означала бы очередь устаревших положений.</para>
    /// </summary>
    /// <remarks>
    /// Пакет обязан влезать в MTU: у ненадёжного канала фрагментированное сообщение теряется целиком, и
    /// «потерять всех разом» хуже, чем не отправить одного. Отсюда потолок
    /// <see cref="MaxPlayersPerPacket"/> — при большем числе игроков присутствие режется на несколько
    /// пакетов, а не растёт в один.
    /// </remarks>
    public static class PresenceCodec
    {
        /// <summary>Версия формата — как у чанков ленты, по той же причине.</summary>
        public const byte Version = 1;

        /// <summary>
        /// Сколько курсоров кладём в один пакет. Четверо — предел кооп-сессии, и при 15 байтах на
        /// курсор пакет остаётся много меньше MTU.
        /// </summary>
        public const int MaxPlayersPerPacket = 8;

        /// <summary>Байт на один курсор в пакете — считаем явно, чтобы рост формата был заметен.</summary>
        public const int BytesPerPlayer = 1 + 2 + 4 + 4 + 2 + 2;

        public static void Write(TapeByteWriter bytes, IReadOnlyList<PresenceState> states)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            int count = states != null ? Mathf.Min(states.Count, MaxPlayersPerPacket) : 0;

            bytes.WriteByte(Version);
            bytes.WriteByte((byte)count);

            for (int i = 0; i < count; i++)
            {
                PresenceState s = states[i];
                bytes.WriteByte((byte)s.PlayerId);
                bytes.WriteUShort(s.Sequence);
                bytes.WriteShort(TapeQuantization.PackPosition(s.Cursor.x));
                bytes.WriteShort(TapeQuantization.PackPosition(s.Cursor.y));
                bytes.WriteShort(TapeQuantization.PackPosition(s.Velocity.x));
                bytes.WriteShort(TapeQuantization.PackPosition(s.Velocity.y));
                bytes.WriteShort((short)s.HoveredId);
                bytes.WriteShort((short)s.HeldId);
            }
        }

        /// <summary>
        /// Разобрать пакет в <paramref name="into"/>. <c>false</c> — чужая версия или битые байты;
        /// присутствие при этом просто не обновится, и это правильный исход: терять курсор безопасно, а
        /// падать из-за него — нет.
        /// </summary>
        public static bool TryRead(ArraySegment<byte> packet, List<PresenceState> into)
        {
            if (into == null) return false;
            into.Clear();

            try
            {
                var bytes = new TapeByteReader(packet);

                if (bytes.ReadByte() != Version) return false;
                int count = bytes.ReadByte();

                for (int i = 0; i < count; i++)
                {
                    int    playerId = bytes.ReadByte();
                    ushort sequence = bytes.ReadUShort();

                    var cursor = new Vector2(
                        TapeQuantization.UnpackPosition(bytes.ReadShort()),
                        TapeQuantization.UnpackPosition(bytes.ReadShort()));
                    var velocity = new Vector2(
                        TapeQuantization.UnpackPosition(bytes.ReadShort()),
                        TapeQuantization.UnpackPosition(bytes.ReadShort()));

                    int hovered = bytes.ReadShort();
                    int held    = bytes.ReadShort();

                    into.Add(new PresenceState(playerId, sequence, cursor, velocity, hovered, held));
                }

                return true;
            }
            catch (InvalidOperationException)
            {
                into.Clear();
                return false;
            }
        }
    }
}
