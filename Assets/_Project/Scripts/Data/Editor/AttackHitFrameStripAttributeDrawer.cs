using Guildmaster.Data.Definitions;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Data.Editor
{
    /// <summary>
    /// Odin-drawer для <c>UnitVisual._attackHitFrames</c> (вики «14»): рисует полоску превью кадров
    /// атаки (сиблинг-<c>_attack</c>); клик по кадру задаёт кадр контакта (пишет <c>[index]</c>).
    /// Ниже — обычный редактор массива для ручного/мульти-режима.
    /// </summary>
    public sealed class AttackHitFrameStripAttributeDrawer
        : OdinAttributeDrawer<AttackHitFrameStripAttribute, int[]>
    {
        private const float Cell = 56f;

        protected override void DrawPropertyLayout(GUIContent label)
        {
            UnitVisual visual = Property.Parent != null && Property.Parent.ValueEntry != null
                ? Property.Parent.ValueEntry.WeakSmartValue as UnitVisual
                : null;

            Sprite[] frames = visual != null ? visual.Frames(UnitAnimationState.Attack) : null;

            if (frames == null || frames.Length == 0)
            {
                SirenixEditorGUI.WarningMessageBox(
                    "Нет кадров в _attack — заполни клип атаки, чтобы выбирать кадр контакта кликом.");
                CallNextDrawer(label);
                return;
            }

            int[] current = ValueEntry.SmartValue ?? System.Array.Empty<int>();
            int hit = current.Length > 0 ? current[0] : -1;

            if (label != null) EditorGUILayout.LabelField(label);

            Rect row = GUILayoutUtility.GetRect(0f, Cell, GUILayout.ExpandWidth(true));
            float x = row.x;
            for (int i = 0; i < frames.Length; i++)
            {
                var cell = new Rect(x, row.y, Cell, Cell);
                x += Cell + 2f;
                if (x > row.xMax) { /* перенос не делаем — горизонтальный скролл инспектора */ }

                if (i == hit)
                    EditorGUI.DrawRect(cell, new Color(0.2f, 0.6f, 1f, 0.35f));

                Sprite s = frames[i];
                Texture tex = s != null ? AssetPreview.GetAssetPreview(s) : null;
                if (tex == null && s != null) tex = s.texture;
                if (tex != null)
                    GUI.DrawTexture(new Rect(cell.x + 2f, cell.y + 2f, cell.width - 4f, cell.height - 16f),
                        tex, ScaleMode.ScaleToFit);

                GUI.Label(new Rect(cell.x, cell.yMax - 14f, cell.width, 14f), i.ToString(),
                    EditorStyles.centeredGreyMiniLabel);

                if (GUI.Button(cell, GUIContent.none, GUIStyle.none))
                {
                    ValueEntry.SmartValue = new[] { i };
                    GUI.changed = true;
                }

                SirenixEditorGUI.DrawBorders(cell, 1);
            }

            EditorGUILayout.LabelField(
                $"Кадр контакта: {(hit >= 0 ? hit.ToString() : "—")} из {frames.Length}",
                EditorStyles.miniLabel);

            CallNextDrawer(null);
        }
    }
}
