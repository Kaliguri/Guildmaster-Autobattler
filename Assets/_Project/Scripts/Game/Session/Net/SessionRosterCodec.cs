using System;
using System.Collections.Generic;
using Guildmaster.Core.Players;
using Guildmaster.Net;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Как участник представляется хозяину: ник, желаемый цвет, скин курсора и место.
    /// </summary>
    /// <remarks>
    /// Цвет — <b>пожелание</b>, а не назначение: занять его мог кто-то раньше, и решает это хозяин.
    /// Отдельная структура нужна потому, что представление и строка таблицы — разные факты: в таблице
    /// уже есть номер участника и назначенная сторона, а представляющийся о них ещё не знает.
    /// </remarks>
    public readonly struct SessionIntro
    {
        public readonly string      Name;
        public readonly int         WantedColorIndex;
        public readonly string      CursorSkinId;
        public readonly PlayerWhere Where;

        public SessionIntro(string name, int wantedColorIndex, string cursorSkinId, PlayerWhere where)
        {
            Name             = name ?? string.Empty;
            WantedColorIndex = wantedColorIndex;
            CursorSkinId     = cursorSkinId ?? string.Empty;
            Where            = where;
        }
    }

    /// <summary>
    /// Состав сеанса в байтах и обратно: таблица от хозяина и представление от участника.
    /// </summary>
    /// <remarks>
    /// <b>Заведён 08.08.2026 после поломки, которую нечем было поймать.</b> До него писатель жил в
    /// <see cref="HostSessionRoster"/>, а читатель — в <see cref="GuestSessionRoster"/>, за сотню строк
    /// друг от друга. Правка, добавившая участнику место, дописала байт в обе половины ХОЗЯЙСКОЙ
    /// стороны и не тронула гостевую: хозяин стал писать шесть полей, гость читать пять, а гость слать
    /// три против четырёх читаемых. Обе стороны глотали разбор молча, и состав перестал доезжать
    /// куда-либо вовсе — при живом рукопожатии, зелёной компиляции и зелёных тестах.
    /// <para><b>Поэтому обе половины формата лежат тут, рядом.</b> Инвариант «что записано, то и
    /// читается» перестаёт быть договором двух файлов и становится свойством одного — и держится
    /// прогоном туда-обратно, а не внимательностью того, кто правит.</para>
    /// <para><b>Разбор возвращает <c>false</c> и НЕ трогает состав наполовину.</b> Половина таблицы
    /// хуже прежней целой: по ней мы решили бы, что кто-то вышел, и перестали бы показывать его
    /// курсор.</para>
    /// </remarks>
    public static class SessionRosterCodec
    {
        /// <summary>Сколько участников влезает в таблицу: счётчик едет одним байтом.</summary>
        private const int MaxPlayers = 255;

        /// <summary>
        /// Объявить таблицу: хозяин → все. Порядок полей — единственный владелец формата этого канала.
        /// </summary>
        public static ArraySegment<byte> WriteTable(IReadOnlyList<SessionPlayer> players, NetByteWriter writer)
        {
            writer.Reset();

            int count = players == null ? 0 : Math.Min(players.Count, MaxPlayers);
            writer.WriteByte((byte)count);

            for (int i = 0; i < count; i++)
            {
                SessionPlayer player = players[i];
                writer.WriteByte((byte)player.Id);
                writer.WriteByte((byte)player.Team);
                writer.WriteByte((byte)player.ColorIndex);
                writer.WriteString(player.Name);
                writer.WriteString(player.CursorSkinId);
                writer.WriteByte((byte)player.Where);
            }

            return writer.WrittenSegment;
        }

        /// <summary>
        /// Разобрать таблицу в <paramref name="into"/>. <c>false</c> — пакет не разобрался целиком:
        /// список остаётся нетронутым, а прежняя таблица честнее половины новой.
        /// </summary>
        public static bool TryReadTable(ArraySegment<byte> payload, List<SessionPlayer> into)
        {
            if (into == null) return false;

            var bytes   = new NetByteReader(payload);
            var parsed  = new List<SessionPlayer>(4);

            try
            {
                int count = bytes.ReadByte();

                for (int i = 0; i < count; i++)
                {
                    int    id    = bytes.ReadByte();
                    int    team  = bytes.ReadByte();
                    int    color = bytes.ReadByte();
                    string name  = bytes.ReadString();
                    string skin  = bytes.ReadString();
                    byte   where = bytes.ReadByte();

                    if (!Enum.IsDefined(typeof(PlayerWhere), where)) return false;

                    parsed.Add(new SessionPlayer(id, name, team, color, skin, (PlayerWhere)where));
                }
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            // Лишний хвост значит, что писали не этим кодеком: молча взять начало — это то самое
            // «встать примерно там же», от которого канал и сломался.
            if (bytes.HasMore) return false;

            into.Clear();
            into.AddRange(parsed);
            return true;
        }

        /// <summary>Представиться хозяину: участник → хозяин.</summary>
        public static ArraySegment<byte> WriteIntro(in SessionIntro intro, NetByteWriter writer)
        {
            writer.Reset();
            writer.WriteString(intro.Name);
            writer.WriteByte((byte)Math.Clamp(intro.WantedColorIndex, 0, 255));
            writer.WriteString(intro.CursorSkinId);
            writer.WriteByte((byte)intro.Where);
            return writer.WrittenSegment;
        }

        /// <summary>
        /// Разобрать представление. <c>false</c> — пакет не разобрался целиком; состав не трогаем.
        /// </summary>
        public static bool TryReadIntro(ArraySegment<byte> payload, out SessionIntro intro)
        {
            intro = default;

            var bytes = new NetByteReader(payload);

            string      name;
            int         color;
            string      skin;
            PlayerWhere where;

            try
            {
                name  = bytes.ReadString();
                color = bytes.ReadByte();
                skin  = bytes.ReadString();

                byte raw = bytes.ReadByte();
                if (!Enum.IsDefined(typeof(PlayerWhere), raw)) return false;
                where = (PlayerWhere)raw;
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (bytes.HasMore) return false;

            intro = new SessionIntro(name, color, skin, where);
            return true;
        }
    }
}
