using System;
using Guildmaster.Guild.Commands;
using Guildmaster.Net;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Команда забега в байтах и обратно. Плоская структура кладётся полем за полем — ровно потому она
    /// плоской и заводилась.
    /// </summary>
    /// <remarks>
    /// <b>Едет только интент гостя.</b> Обратно, от хоста, команды не отправляются: у гостя состояние не
    /// вычисляется применением, а приходит снимком (см. <see cref="RunStateBroadcast"/>). Иначе у нас
    /// было бы два пути к одному состоянию, и расходились бы они на первом же изменении, прошедшем мимо
    /// шины, — а такие изменения сегодня есть и записаны долгом (транзакции магазина и наград).
    /// <para><b>Пара «игрок и его номер» едет как есть:</b> её назначает отправитель, и переприсвоить её
    /// на приёме значило бы потерять идемпотентность, ради которой она существует.</para>
    /// </remarks>
    public static class RunCommandCodec
    {
        /// <summary>Уложить команду в буфер. Возвращает готовый к отправке отрезок.</summary>
        public static ArraySegment<byte> Write(in RunCommand command, NetByteWriter writer)
        {
            writer.Reset();
            writer.WriteByte((byte)command.Kind);
            writer.WriteInt(command.PlayerId);
            writer.WriteInt(command.Sequence);
            writer.WriteLong(command.ClientTimeMs);
            writer.WriteInt(command.SlotIndex);
            writer.WriteInt(command.Amount);
            writer.WriteString(command.Text);
            writer.WriteFloat(command.X);
            writer.WriteFloat(command.Y);
            return writer.WrittenSegment;
        }

        /// <summary>
        /// Разобрать команду. <c>false</c> — вид команды неизвестен этой сборке: это расхождение версий,
        /// и применять «что-то похожее» нельзя, состояния разъедутся молча.
        /// </summary>
        public static bool TryRead(ArraySegment<byte> payload, out RunCommand command)
        {
            command = default;
            if (payload.Count < 1) return false;

            var bytes = new NetByteReader(payload);

            byte rawKind = bytes.ReadByte();
            // Проверяем именно byte: у перечисления байтовая основа, и int здесь уронил бы сравнение.
            if (!Enum.IsDefined(typeof(RunCommandKind), rawKind)) return false;

            var  kind     = (RunCommandKind)rawKind;
            int  playerId = bytes.ReadInt();
            int  sequence = bytes.ReadInt();
            long time     = bytes.ReadLong();
            int  slot     = bytes.ReadInt();
            int  amount   = bytes.ReadInt();
            string text   = bytes.ReadString();
            float x       = bytes.ReadFloat();
            float y       = bytes.ReadFloat();

            command = new RunCommand(kind, playerId, sequence, time, slot, amount, text, x, y);
            return true;
        }
    }
}
