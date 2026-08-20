#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Общий выключатель служебной разметки юнита в Scene view — линейки роста, боевых точек и прочего,
    /// что рисует <c>UnitView.OnDrawGizmos</c>.
    ///
    /// Нужен потому, что разметка полезна ровно до момента, когда начинаешь двигать позы руками: тогда
    /// она лежит поверх спрайтов и мешает целиться мышью. Удалять её ради этого нельзя — она для того и
    /// заведена, — поэтому у неё выключатель, а не судьба.
    /// </summary>
    /// <remarks>
    /// Флаг живёт в <see cref="EditorPrefs"/>, а не в поле компонента: он относится к рабочему месту, а
    /// не к префабу. Поле означало бы правку ассета ради того, чтобы что-то не мешало смотреть, — и эта
    /// правка уехала бы в git и к соседям.
    /// <para>
    /// Оверлей суставов (<see cref="RigAnchorOverlay"/>) держит свой тумблер: он отвечает на другой
    /// вопрос («куда я тащу этот кусок») и гасится отдельно.
    /// </para>
    /// </remarks>
    public static class GizmoVisibility
    {
        public const string PrefKey = "Alebardium.Gizmos.Show";
        const string MenuPath = "Alebardium/Animation/Show Unit Gizmos In Scene";

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem(MenuPath, priority = 627)]
        static void Toggle()
        {
            Enabled = !Enabled;
            SceneView.RepaintAll();
        }

        [MenuItem(MenuPath, validate = true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }
    }
}
#endif
