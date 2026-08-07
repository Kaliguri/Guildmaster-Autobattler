using System;
using System.Collections.Generic;
using Guildmaster.Net;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>Что группа решает прямо сейчас внутри узла.</summary>
    /// <remarks>
    /// <b>Перечисление только ДОПИСЫВАЕТСЯ.</b> Номер едет по проводу, и переставленный номер увёл бы
    /// гостя не на тот экран — молча, потому что оба конца собираются порознь.
    /// </remarks>
    public enum NodeStageKind : byte
    {
        /// <summary>Ничего не показываем: узел идёт своим ходом.</summary>
        None = 0,

        /// <summary>Витрина награды: варианты — id реликвий.</summary>
        Reward = 1,
    }

    /// <summary>
    /// Шаг узла с содержимым: что на экране и из чего выбирают.
    /// </summary>
    /// <remarks>
    /// <b>Состояние, а не приказ «покажи».</b> Гость подключается в любой момент и переживает потери,
    /// поэтому ему нужен ответ на «что сейчас», а не история того, как к этому пришли. Применение
    /// идемпотентно: повтор того же шага ничего не открывает заново.
    /// <para><b>Содержимое — строковые id</b>, а не сериализованные определения: лента возит ссылки
    /// на ассеты тем же способом, и совпадение реестров уже проверено рукопожатием. Неизвестный id
    /// поэтому невозможен, а не «маловероятен».</para>
    /// </remarks>
    public readonly struct NodeStageState : IEquatable<NodeStageState>
    {
        public readonly NodeStageKind Kind;

        /// <summary>Из чего выбирают. Для награды — id выпавших реликвий, по порядку витрины.</summary>
        public readonly IReadOnlyList<string> Options;

        private static readonly string[] Nothing = Array.Empty<string>();

        public NodeStageState(NodeStageKind kind, IReadOnlyList<string> options = null)
        {
            Kind    = kind;
            Options = options ?? Nothing;
        }

        /// <summary>Экрана нет — узел идёт своим ходом.</summary>
        public static NodeStageState Idle => new NodeStageState(NodeStageKind.None);

        public bool Equals(NodeStageState other)
        {
            if (Kind != other.Kind || Options.Count != other.Options.Count) return false;

            for (int i = 0; i < Options.Count; i++)
                if (Options[i] != other.Options[i]) return false;

            return true;
        }

        public override bool Equals(object obj) => obj is NodeStageState other && Equals(other);

        public override int GetHashCode()
        {
            int hash = (int)Kind * 397;
            for (int i = 0; i < Options.Count; i++) hash = (hash * 31) ^ Options[i].GetHashCode();
            return hash;
        }

        public override string ToString() => $"{Kind}({string.Join(", ", Options)})";
    }

    /// <summary>Шаг узла в байтах и обратно.</summary>
    public static class NodeStageCodec
    {
        public static ArraySegment<byte> Write(in NodeStageState state, NetByteWriter writer)
        {
            writer.Reset();
            writer.WriteByte((byte)state.Kind);
            writer.WriteByte((byte)(state.Options.Count > 255 ? 255 : state.Options.Count));

            for (int i = 0; i < state.Options.Count && i < 255; i++) writer.WriteString(state.Options[i]);

            return writer.WrittenSegment;
        }

        /// <summary>
        /// Разобрать шаг. <c>false</c> — вид шага неизвестен этой сборке или пакет оборван: это
        /// расхождение версий, и показывать «примерно то же» нельзя.
        /// </summary>
        public static bool TryRead(ArraySegment<byte> payload, out NodeStageState state)
        {
            state = NodeStageState.Idle;
            if (payload.Count < 2) return false;

            var bytes = new NetByteReader(payload);

            byte kind;
            var options = new List<string>(3);
            try
            {
                kind = bytes.ReadByte();
                int count = bytes.ReadByte();
                for (int i = 0; i < count; i++) options.Add(bytes.ReadString());
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (!Enum.IsDefined(typeof(NodeStageKind), kind)) return false;

            state = new NodeStageState((NodeStageKind)kind, options);
            return true;
        }
    }
}
