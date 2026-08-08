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

        /// <summary>
        /// Передышка между узлами: две кнопки-шортката, «Продолжить» (на карту) и «К построению».
        /// </summary>
        /// <remarks>
        /// <b>Решения тут нет — это навигация</b> (вердикт Макса 08.08.2026: «Каждый нажимает отдельно
        /// чисто для себя»). Кнопки ведут туда же, куда табы, и жмут их порознь: ждать напарника,
        /// чтобы посмотреть свой строй, незачем.
        /// </remarks>
        Interlude = 2,
    }

    /// <summary>
    /// Что сейчас на экране у группы — для тех, кто это ПОКАЗЫВАЕТ, независимо от роли.
    /// </summary>
    /// <remarks>
    /// <b>Заведён 08.08.2026 под HARD-правило «хозяин и гость — равные».</b> До него экраны узла
    /// публиковала петля акта, а она собирается только владельцу: из восьми экранов узла гость видел
    /// один. Показ переехал сюда, к общему для обеих ролей потребителю, а роли остались там, где им
    /// и место, — в том, кто ОБЪЯВЛЯЕТ шаг.
    /// </remarks>
    public interface INodeStageView
    {
        /// <summary>Что объявлено сейчас.</summary>
        NodeStageState Current { get; }

        /// <summary>Шаг сменился. Повтор того же шага события не поднимает.</summary>
        event Action<NodeStageState> Changed;
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

        /// <summary>
        /// Запас реликвий полон — витрина предложит обмен вместо простого «взять».
        /// </summary>
        /// <remarks>
        /// <b>Едет по проводу, потому что от него зависит ГОЛОС.</b> При полном запасе вариант выглядит
        /// как «взять взамен того-то», при неполном — просто «взять», а согласие требует побайтово
        /// одинаковой строки у всех. Пока признак считался на каждой стороне сам, у гостя он был зашит
        /// в <c>false</c>: при полном запасе у хозяина голоса не сходились НИКОГДА, и витрина не
        /// закрывалась ни у кого — забег вставал (08.08.2026).
        /// </remarks>
        public readonly bool InventoryFull;

        private static readonly string[] Nothing = Array.Empty<string>();

        public NodeStageState(NodeStageKind kind, IReadOnlyList<string> options = null,
                              bool inventoryFull = false)
        {
            Kind          = kind;
            Options       = options ?? Nothing;
            InventoryFull = inventoryFull;
        }

        /// <summary>Экрана нет — узел идёт своим ходом.</summary>
        public static NodeStageState Idle => new NodeStageState(NodeStageKind.None);

        public bool Equals(NodeStageState other)
        {
            if (Kind != other.Kind || InventoryFull != other.InventoryFull ||
                Options.Count != other.Options.Count) return false;

            for (int i = 0; i < Options.Count; i++)
                if (Options[i] != other.Options[i]) return false;

            return true;
        }

        public override bool Equals(object obj) => obj is NodeStageState other && Equals(other);

        public override int GetHashCode()
        {
            int hash = (int)Kind * 397 ^ (InventoryFull ? 8191 : 0);
            for (int i = 0; i < Options.Count; i++) hash = (hash * 31) ^ Options[i].GetHashCode();
            return hash;
        }

        public override string ToString() =>
            $"{Kind}({string.Join(", ", Options)}){(InventoryFull ? " [запас полон]" : string.Empty)}";
    }

    /// <summary>Шаг узла в байтах и обратно.</summary>
    public static class NodeStageCodec
    {
        public static ArraySegment<byte> Write(in NodeStageState state, NetByteWriter writer)
        {
            writer.Reset();
            writer.WriteByte((byte)state.Kind);
            writer.WriteBool(state.InventoryFull);
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
            if (payload.Count < 3) return false;

            var bytes = new NetByteReader(payload);

            byte kind;
            bool full;
            var options = new List<string>(3);
            try
            {
                kind = bytes.ReadByte();
                full = bytes.ReadBool();
                int count = bytes.ReadByte();
                for (int i = 0; i < count; i++) options.Add(bytes.ReadString());
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            if (!Enum.IsDefined(typeof(NodeStageKind), kind)) return false;

            state = new NodeStageState((NodeStageKind)kind, options, full);
            return true;
        }
    }
}
