using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Guildmaster.Presentation.Body;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Каждая часть тела обязана уметь ВСПЫХИВАТЬ: её материал должен знать <c>_FlashAmount</c>.
    ///
    /// <b>Зачем этот тест существует.</b> Вспышка, голограмма, контур и свечение пишутся кодом во ВСЕ
    /// части разом — выборочности там нет. Но часть на дефолтном спрайтовом материале физически не умеет
    /// их показать: property block пишется в свойство, которого у шейдера нет, и эффект исчезает молча.
    /// Именно так у сторибук-юнита не вспыхивали волосы, кисть, гарда и рукоять (нашёл Макс 06.08.2026,
    /// глядя на вспышку смерти).
    ///
    /// <b>Почему тест, а не кнопка.</b> Кнопка «поставить материал вспышки» в инспекторе тела есть и
    /// раньше была единственным хранителем этого инварианта — то есть держался он тем, что человек не
    /// забыл её нажать. Четыре части забыли. Инвариант, который живёт между префабом и кодом, обязан
    /// падать тестом, а не ждать, пока кто-то заметит невспыхивающие волосы.
    /// </summary>
    public sealed class BodyFlashMaterialTests
    {
        static IEnumerable<string> BodyPrefabPaths() =>
            AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path =>
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    return go != null && go.GetComponentInChildren<SkeletalBodyVisual>(true) != null;
                });

        [Test]
        public void EveryBodyPart_HasFlashCapableMaterial()
        {
            var offenders = new List<string>();
            int bodies = 0, parts = 0;

            foreach (string path in BodyPrefabPaths())
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (SkeletalBodyVisual body in go.GetComponentsInChildren<SkeletalBodyVisual>(true))
                {
                    bodies++;
                    foreach (SpriteRenderer part in PartsOf(body))
                    {
                        if (part == null) continue;
                        parts++;

                        Material mat = part.sharedMaterial;
                        if (mat != null && mat.HasProperty(BodyShaderIds.FlashAmount)) continue;

                        offenders.Add($"{System.IO.Path.GetFileName(path)} → {part.name} " +
                                      $"(материал {(mat != null ? mat.name : "не назначен")})");
                    }
                }
            }

            Assert.That(bodies, Is.GreaterThan(0), "не найдено ни одного тела — тест проверял пустоту");
            Assert.That(parts, Is.GreaterThan(0), "у тел нет частей — тест проверял пустоту");
            Assert.That(offenders, Is.Empty,
                "Эти части не умеют вспыхивать — их материал не знает _FlashAmount. Лечится кнопкой " +
                "«Поставить материал вспышки» в инспекторе тела:\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>
        /// Список частей берётся ТОТ ЖЕ, по которому ходит рантайм, а не обходом иерархии: адресация
        /// эффектов идёт по индексу в нём, и проверять что-то другое значило бы проверять не то.
        /// </summary>
        static IReadOnlyList<SpriteRenderer> PartsOf(SkeletalBodyVisual body)
        {
            FieldInfo field = typeof(SkeletalBodyVisual)
                .GetField("_parts", BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(body) as List<SpriteRenderer> ?? new List<SpriteRenderer>();
        }
    }
}
