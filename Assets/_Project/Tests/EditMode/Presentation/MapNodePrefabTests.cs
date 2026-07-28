using System;
using System.Collections.Generic;
using Guildmaster.Presentation.Map;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>Защищает общий префаб узла от пустых, перепутанных и разномасштабных вариантов иконок.</summary>
    public sealed class MapNodePrefabTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Map/MapNode.prefab";

        private static readonly IReadOnlyDictionary<string, string> ExpectedSprites =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Start"] = "compass.png",
                ["Battle"] = "broadsword.png",
                ["Elite"] = "sword-clash.png",
                ["Boss"] = "crowned-skull.png",
                ["Chest"] = "chest.png",
                ["Shop"] = "shop.png",
                ["TextEvent"] = "scroll-unfurled.png",
                ["Unknown"] = "uncertainty.png",
                ["Camp"] = "campfire.png",
            };

        /// <summary>Каждый тип имеет ровно один видимый силуэт из общего набора и одинаковый масштаб.</summary>
        [Test]
        public void EveryVariant_UsesExpectedVectorIconAtConsistentScale()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.NotNull(prefab, $"Не найден {PrefabPath}.");

            MapNodeView view = prefab.GetComponent<MapNodeView>();
            Assert.NotNull(view, "На корне MapNode.prefab нет MapNodeView.");

            var serialized = new SerializedObject(view);
            SerializedProperty variants = serialized.FindProperty("_variants");
            Assert.NotNull(variants);
            Assert.AreEqual(ExpectedSprites.Count, variants.arraySize, "Набор вариантов иконок неполон.");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < variants.arraySize; i++)
            {
                SerializedProperty variant = variants.GetArrayElementAtIndex(i);
                string kind = variant.FindPropertyRelative("Kind").stringValue;
                var icon = variant.FindPropertyRelative("Icon").objectReferenceValue as GameObject;

                Assert.That(seen.Add(kind), Is.True, $"Тип '{kind}' назначен дважды.");
                Assert.That(ExpectedSprites.ContainsKey(kind), Is.True, $"Неожиданный тип '{kind}'.");
                Assert.NotNull(icon, $"У типа '{kind}' не назначен корень иконки.");

                SpriteRenderer[] renderers = icon.GetComponentsInChildren<SpriteRenderer>(true);
                var visible = new List<SpriteRenderer>(renderers.Length);
                for (int r = 0; r < renderers.Length; r++)
                    if (IsVisibleWhenRootEnabled(renderers[r].transform, icon.transform) && renderers[r].sprite != null)
                        visible.Add(renderers[r]);

                Assert.AreEqual(1, visible.Count,
                    $"У типа '{kind}' должен быть один видимый нормализованный силуэт.");

                SpriteRenderer renderer = visible[0];
                string spritePath = AssetDatabase.GetAssetPath(renderer.sprite);
                Assert.That(spritePath, Does.EndWith("/" + ExpectedSprites[kind]),
                    $"Тип '{kind}' ссылается не на свой спрайт.");

                float scale = EffectiveScale(renderer.transform, icon.transform);
                Assert.That(scale, Is.EqualTo(0.6f).Within(0.001f),
                    $"Тип '{kind}' выбивается по масштабу.");

                Quaternion rotation = EffectiveRotation(renderer.transform, icon.transform);
                Assert.That(Quaternion.Angle(Quaternion.identity, rotation), Is.LessThan(0.1f),
                    $"Тип '{kind}' повёрнут относительно остальных.");
            }

            CollectionAssert.AreEquivalent(ExpectedSprites.Keys, seen);
        }

        private static bool IsVisibleWhenRootEnabled(Transform renderer, Transform root)
        {
            for (Transform current = renderer; current != root; current = current.parent)
                if (current == null || !current.gameObject.activeSelf) return false;
            return true;
        }

        private static float EffectiveScale(Transform renderer, Transform root)
        {
            float scale = 1f;
            for (Transform current = renderer; current != root.parent; current = current.parent)
                scale *= current.localScale.x;
            return scale;
        }

        private static Quaternion EffectiveRotation(Transform renderer, Transform root)
        {
            Quaternion rotation = Quaternion.identity;
            for (Transform current = renderer; current != root.parent; current = current.parent)
                rotation = current.localRotation * rotation;
            return rotation;
        }
    }
}
