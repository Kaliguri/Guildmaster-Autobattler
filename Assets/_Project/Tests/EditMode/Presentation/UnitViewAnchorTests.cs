using System.Collections.Generic;
using Guildmaster.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Holds the contract between a unit view's DRAWING and its ANCHORS — the two halves that no single
    /// file can hold, because the artwork hangs off the rig while the anchors are four loose transforms
    /// beside it.
    ///
    /// This fails SILENTLY, which is the whole reason it is a test. Every consumer keeps working: sparks,
    /// damage numbers and point B of a hit form land exactly on Hit Point, dust lands on Feet Point, the
    /// overhead bars sit above Head Point. They just land on empty grass, because the body drifted away
    /// from the anchors it is supposed to describe. On 05.08.2026 UnitView_Human128 stood 0.8 above its
    /// own anchors and it read as four unrelated bugs: missing hit effects, dust under the floor, bars
    /// needing a hand-tuned lift, and slash forms ballooning to two body heights.
    /// </summary>
    public class UnitViewAnchorTests
    {
        const string ViewFolder = "Assets/_Project/Prefabs/Units";

        /// <summary>
        /// How far the drawing may stick out past an anchor, in world units — roughly a tenth of a unit's
        /// height. Hair, helmet crests and boot soles legitimately overshoot; a body-length drift does not.
        /// </summary>
        const float Tolerance = 0.15f;

        static IEnumerable<string> ViewPrefabs()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ViewFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponent<UnitView>() != null)
                    yield return path;
            }
        }

        /// <summary>
        /// Vertical extent of everything the view draws. The shadow is excluded on purpose: it is a mark
        /// on the ground, not part of the body, and it is allowed to sit below the feet.
        /// </summary>
        static bool TryMeasureBody(GameObject instance, out Bounds bounds)
        {
            bounds = default;
            Transform visual = instance.transform.Find("Visual Sprites");
            if (visual == null) return false;

            bool any = false;
            foreach (SpriteRenderer renderer in visual.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
            {
                if (renderer.sprite == null || !renderer.gameObject.activeInHierarchy) continue;
                if (renderer.name == "Feet Shadow") continue;

                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            return any;
        }

        [Test]
        public void BodyStandsInItsOwnAnchors()
        {
            var offenders = new List<string>();

            foreach (string path in ViewPrefabs())
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(path));
                try
                {
                    if (!TryMeasureBody(instance, out Bounds body)) continue;
                    var view = instance.GetComponent<UnitView>();

                    float feetGap = body.min.y - view.FeetPoint.y;   // > 0 — тело парит над якорем ног
                    float headGap = body.max.y - view.HeadPoint.y;   // > 0 — макушка выше якоря головы

                    if (Mathf.Abs(feetGap) > Tolerance)
                        offenders.Add($"{path}: ступни на {body.min.y:F3}, а Feet Point на " +
                                      $"{view.FeetPoint.y:F3} (расхождение {feetGap:F3})");

                    if (Mathf.Abs(headGap) > Tolerance)
                        offenders.Add($"{path}: макушка на {body.max.y:F3}, а Head Point на " +
                                      $"{view.HeadPoint.y:F3} (расхождение {headGap:F3})");

                    // Hit Point принимает искры, цифры и точку B формы удара — он обязан быть В корпусе,
                    // а не под ним и не над ним. Без этой строки тело, съехавшее РОВНО на свой рост,
                    // прошло бы обе проверки выше.
                    if (view.HitPoint.y < body.min.y || view.HitPoint.y > body.max.y)
                        offenders.Add($"{path}: Hit Point на {view.HitPoint.y:F3} вне тела " +
                                      $"[{body.min.y:F3}, {body.max.y:F3}]");
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }

            Assert.That(offenders, Is.Empty,
                "Тело вида обязано стоять в своих якорях: ступни у Feet Point, макушка у Head Point, " +
                "корпус вокруг Hit Point. Разъехавшись, оно ничего не ломает вслух — эффекты продолжают " +
                "исправно рисоваться там, где им сказали, просто мимо юнита.\n  " +
                string.Join("\n  ", offenders));
        }
    }
}
