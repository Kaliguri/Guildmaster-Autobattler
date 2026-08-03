using UnityEngine;

namespace Guildmaster.Presentation.Body
{
    /// <summary>
    /// Конвенция имён рига: кость зовётся как сустав, который она вращает (<c>LowerArm_R</c>), сторона
    /// живёт суффиксом (<c>_R</c> / <c>_L</c>), а рисунок висит на кости отдельным узлом с суффиксом
    /// <c>_Art</c> (<c>LowerArm_R_Art</c>). Всё, что читает структуру рига — и в редакторе, и в
    /// рантайме, — спрашивает здесь.
    /// </summary>
    /// <remarks>
    /// Имя кости обязано совпадать с именем трансформа: этого требует Unity для перепривязки клипов,
    /// и на этом же держится цепочка <c>Shoulder → UpperArm → LowerArm → Hand</c>. Предмет —
    /// звено цепи (<c>Weapon_R</c> внутри <c>Hand_R</c>), а не ветка рядом с ней: меч едет за ладонью
    /// структурно, а не потому, что две ветки удалось согласовать.
    /// <para>
    /// Копия строк во втором файле была бы вторым владельцем: узлы рига переименовываются только через
    /// <c>RigMigrate</c> (иначе молча рвутся пути клипов, маски и аватар), и вторая копия конвенции
    /// разошлась бы с первой без единой ошибки компиляции. Поэтому владелец живёт в рантайм-сборке,
    /// а инструменты рига читают конвенцию отсюда.
    /// </para>
    /// <para>
    /// Прежняя конвенция (<c>Rotation Point (Elbow)</c>, контейнер <c>Visual Part (…)</c>, сторона в
    /// скобках у конечности) отменена 04.08.2026 вместе с перестройкой рига: три узла на часть тела
    /// схлопнулись в два, и «где тут ось вращения» перестало быть вопросом — ось это сам узел.
    /// </para>
    /// </remarks>
    public static class RigNaming
    {
        /// <summary>Суффикс узла с рисунком: <c>LowerArm_R</c> -&gt; <c>LowerArm_R_Art</c>.</summary>
        public const string ArtSuffix = "_Art";

        /// <summary>
        /// Префикс кости-хвата: узла, в котором висит предмет. Имя нейтрально к содержимому нарочно —
        /// назови узел по мечу, и кит с двумя мечами или с копьём потребует переименования кости, то
        /// есть новой миграции клипов, масок и аватара.
        /// </summary>
        public const string GripPrefix = "Weapon_";

        const string LeftSuffix  = "_L";
        const string RightSuffix = "_R";

        /// <summary>Имя узла рисунка для кости: <c>Head</c> -&gt; <c>Head_Art</c>.</summary>
        public static string ArtName(string boneName) => boneName + ArtSuffix;

        /// <summary>Узел рисунка — лист с суффиксом <see cref="ArtSuffix"/>.</summary>
        public static bool IsArt(Transform node) =>
            node != null && node.name.EndsWith(ArtSuffix, System.StringComparison.Ordinal);

        /// <summary>
        /// Кость — любой узел рига, который не рисунок. Отдельного понятия «сустав» больше нет: кость и
        /// есть точка вращения, и это главное, что дала перестройка.
        /// </summary>
        public static bool IsBone(Transform node) => node != null && !IsArt(node);

        /// <summary>Кость-хват: та, внутри которой висит предмет.</summary>
        public static bool IsGrip(Transform node) =>
            node != null && node.name.StartsWith(GripPrefix, System.StringComparison.Ordinal);

        /// <summary>Кость плеча нужной стороны — начало дуги удара: рука вращается как жёсткий рычаг.</summary>
        public static string ShoulderBone(BodySide side) =>
            side == BodySide.Left ? "Shoulder" + LeftSuffix : "Shoulder" + RightSuffix;

        /// <summary>Кость, которую рисует этот рендерер: сам узел, если он не рисунок, иначе его родитель.</summary>
        public static Transform BoneOf(Transform rendererNode)
        {
            if (rendererNode == null) return null;
            return IsArt(rendererNode) && rendererNode.parent != null ? rendererNode.parent : rendererNode;
        }

        /// <summary>Имя кости, которую рисует этот рендерер (<c>LowerArm_R</c>, <c>Weapon_R</c>).</summary>
        public static string BoneNameOf(Transform rendererNode)
        {
            Transform bone = BoneOf(rendererNode);
            return bone != null ? bone.name : null;
        }

        /// <summary>
        /// Первый узел рисунка на этой кости. Кость вправе нести несколько рисунков (лицо и волосы,
        /// клинок, гарда и рукоять), поэтому «первый» — это порядок в иерархии, а не привилегия.
        /// </summary>
        public static Transform FindArt(Transform bone)
        {
            if (bone == null) return null;
            for (int i = 0; i < bone.childCount; i++)
            {
                Transform child = bone.GetChild(i);
                if (IsArt(child)) return child;
            }
            return null;
        }

        /// <summary>
        /// Сторона тела: читается из суффикса имени, своего или ближайшего предка до <paramref name="root"/>.
        /// Кисть и хват собственного суффикса не теряют, но предок нужен для рисунков и вложенных предметов.
        /// </summary>
        public static BodySide SideOf(Transform node, Transform root)
        {
            for (Transform t = node; t != null && t != root; t = t.parent)
            {
                string name = t.name;
                if (IsArt(t)) name = name.Substring(0, name.Length - ArtSuffix.Length);
                if (name.EndsWith(LeftSuffix, System.StringComparison.Ordinal)) return BodySide.Left;
                if (name.EndsWith(RightSuffix, System.StringComparison.Ordinal)) return BodySide.Right;
            }
            return BodySide.None;
        }

        /// <summary>Суффикс стороны для имени кости: <c>_L</c> / <c>_R</c> / пусто.</summary>
        public static string SideSuffix(BodySide side) => side switch
        {
            BodySide.Left  => LeftSuffix,
            BodySide.Right => RightSuffix,
            _              => "",
        };
    }
}
