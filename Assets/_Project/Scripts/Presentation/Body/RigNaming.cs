using UnityEngine;

namespace Guildmaster.Presentation.Body
{
    /// <summary>
    /// Конвенция имён рига: как называются суставы (<c>Rotation Point (Elbow)</c>), хват предмета
    /// (<c>Rotation Point (Grip)</c>), контейнер арта кости (<c>Visual Part (Head)</c>) и сторона тела
    /// (<c>Arm (Left)</c>). Всё, что читает структуру рига — и в редакторе, и в рантайме, — спрашивает здесь.
    /// </summary>
    /// <remarks>
    /// Конвенция родилась в editor-инструментах анимации (<c>RigProfileBuilder</c>, <c>RigVisualParts</c>)
    /// и жила там же. Рантайму она понадобилась, когда свечению каста стало нужно адресовать ЧАСТЬ —
    /// «то, что в хвате правой руки», а не «спрайт с именем Sword», — а editor-сборка в игре недоступна.
    /// <para>
    /// Копия строк во втором файле была бы вторым владельцем: узлы рига переименовываются только через
    /// <c>RigMigrate</c> (иначе молча рвутся пути клипов, маски и аватар), и вторая копия конвенции
    /// разошлась бы с первой без единой ошибки компиляции. Поэтому владелец переехал в рантайм-сборку,
    /// а инструменты рига читают конвенцию отсюда.
    /// </para>
    /// </remarks>
    public static class RigNaming
    {
        /// <summary>Префикс контейнера арта кости: <c>Visual Part (Head)</c>.</summary>
        public const string ContainerPrefix = "Visual Part";

        /// <summary>Префикс узла-сустава: <c>Rotation Point (Elbow)</c>.</summary>
        public const string JointPrefix = "Rotation Point (";

        /// <summary>Метка сустава-хвата: сустав, в котором висит предмет.</summary>
        public const string GripLabel = "Grip";

        const string LeftMark  = "(Left)";
        const string RightMark = "(Right)";

        /// <summary>Имя контейнера арта для кости: <c>Head</c> -&gt; <c>Visual Part (Head)</c>.</summary>
        public static string ContainerName(string boneName) => $"{ContainerPrefix} ({boneName})";

        public static bool IsContainer(Transform node) =>
            node != null && node.name.StartsWith(ContainerPrefix + " (", System.StringComparison.Ordinal);

        /// <summary>Узел-сустав (в том числе хват): имя начинается с <see cref="JointPrefix"/>.</summary>
        public static bool IsJoint(Transform node) =>
            node != null && node.name.StartsWith(JointPrefix, System.StringComparison.Ordinal);

        /// <summary>Сустав-хват — тот, чья метка равна <see cref="GripLabel"/>.</summary>
        public static bool IsGrip(Transform node) =>
            IsJoint(node) && ExtractLabel(node.name) == GripLabel;

        /// <summary><c>Rotation Point (Elbow)</c> -&gt; <c>Elbow</c>; имя без скобок возвращается как есть.</summary>
        public static string ExtractLabel(string nodeName)
        {
            if (string.IsNullOrEmpty(nodeName)) return nodeName;
            int open = nodeName.IndexOf('(');
            int close = nodeName.LastIndexOf(')');
            if (open < 0 || close <= open + 1) return nodeName;
            return nodeName.Substring(open + 1, close - open - 1).Trim();
        }

        /// <summary><c>Visual Part (Head)</c> -&gt; <c>Head</c>.</summary>
        public static string BoneNameFromContainer(string containerName) => ExtractLabel(containerName);

        /// <summary>Контейнер арта этой кости, если он уже выделен (риг мог быть не разделён).</summary>
        public static Transform FindContainer(Transform bone)
        {
            if (bone == null) return null;
            for (int i = 0; i < bone.childCount; i++)
            {
                Transform child = bone.GetChild(i);
                if (IsContainer(child)) return child;
            }
            return null;
        }

        /// <summary>
        /// True для контейнера и всего, что внутри него. Имена там принадлежат художнику — спрайтовый узел
        /// может зваться <c>Head</c>, <c>Hair</c>, <c>Armor Plate</c>, — поэтому код, узнающий кости ПО ИМЕНИ,
        /// обязан остановиться на контейнере, иначе выдумает сустав из куска арта.
        /// </summary>
        public static bool IsUnderContainer(Transform node)
        {
            for (Transform t = node; t != null; t = t.parent)
                if (IsContainer(t)) return true;
            return false;
        }

        /// <summary>
        /// Кость, которую рисует этот рендерер: узел НАД контейнером арта. Рендерер, ещё не переехавший в
        /// контейнер, сам себе кость.
        /// </summary>
        public static Transform BoneOf(Transform rendererNode)
        {
            if (rendererNode == null) return null;
            for (Transform t = rendererNode; t != null; t = t.parent)
                if (IsContainer(t))
                    return t.parent != null ? t.parent : t;
            return rendererNode;
        }

        /// <summary>Имя кости, которую рисует этот рендерер (<c>Arm_Down</c>, <c>Sword</c>).</summary>
        public static string BoneNameOf(Transform rendererNode)
        {
            Transform bone = BoneOf(rendererNode);
            return bone != null ? bone.name : null;
        }

        /// <summary>
        /// Сторона тела: берётся с конечности НАД узлом (<c>Arm (Left)</c>), поэтому сами суставы носят
        /// одно имя на обе стороны. Поиск идёт до <paramref name="root"/> включительно-исключая: выше корня
        /// тела уже чужая иерархия.
        /// </summary>
        public static BodySide SideOf(Transform node, Transform root)
        {
            for (Transform t = node; t != null && t != root; t = t.parent)
            {
                if (t.name.Contains(LeftMark)) return BodySide.Left;
                if (t.name.Contains(RightMark)) return BodySide.Right;
            }
            return BodySide.None;
        }

        /// <summary>Суффикс логического id сустава: <c>.L</c> / <c>.R</c> / пусто.</summary>
        public static string SideSuffix(BodySide side) => side switch
        {
            BodySide.Left  => ".L",
            BodySide.Right => ".R",
            _              => "",
        };
    }
}
