using Guildmaster.Data.Definitions;
using Sirenix.OdinInspector.Editor;
using UnityEditor;

namespace Guildmaster.Data.Editor
{
    /// <summary>
    /// Гарантирует Odin-инспектор для всех <see cref="ContentDefinition"/>-наследников: id-drawer
    /// (read-only + Edit Id, вики «13» §2.3) и пикеры полиморфных <c>[SerializeReference]</c>-полей
    /// (напр. компоненты <see cref="EffectData"/>). Без явного editor Unity рисовал бы базовый инспектор.
    /// </summary>
    [CustomEditor(typeof(ContentDefinition), editorForChildClasses: true)]
    public sealed class ContentDefinitionEditor : OdinEditor
    {
    }
}
