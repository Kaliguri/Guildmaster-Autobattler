using System.Collections.Generic;
using Guildmaster.Presentation.Body;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Вылет предмета ОБЪЯВЛЕН числом, а не выведен из картинки — и этот тест сторожит расхождение между
    /// объявленным и тем, что рисует арт.
    /// <para>
    /// До 06.08.2026 длина оружия равнялась размеру спрайта: <c>UnitPartGeometry</c> брал дальнюю вершину
    /// меша. Перерисовал клинок длиннее — молча поехали дуга за клинком, знак удара и офлайн-замеры.
    /// Теперь число живёт в данных, замер переехал в редакторную кнопку, а этот гейт ловит третий случай:
    /// арт перерисовали, а число обновить забыли.
    /// </para>
    /// <para>
    /// Порог намеренно мягкий. Совпадение бит в бит здесь не нужно и вредно: художник вправе подвинуть
    /// пиксель, не пересчитывая геометрию, — но разъехаться на четверть длины уже значит, что данные
    /// описывают не то оружие, которое видит игрок.
    /// </para>
    /// </summary>
    public sealed class DeclaredReachTests
    {
        private const string RigPrefab = "Assets/_Project/Prefabs/Bones/BoneUnit_SwordShield.prefab";

        /// <summary>Доля, на которую объявленному разрешено разойтись с замеренным.</summary>
        private const float Tolerance = 0.15f;

        /// <summary>Дальняя от точки крепления вершина меша — то же, что меряет редакторная кнопка.</summary>
        private static Vector3 LocalTipFromMesh(SpriteRenderer renderer)
        {
            Vector2[] vertices = renderer.sprite.vertices;
            Vector2 best = vertices[0];
            float bestSqr = -1f;
            for (int i = 0; i < vertices.Length; i++)
            {
                float sqr = vertices[i].sqrMagnitude;
                if (sqr <= bestSqr) continue;
                bestSqr = sqr;
                best = vertices[i];
            }
            return best;
        }

        [Test]
        public void EveryHeldItem_DeclaresItsReach_AndTheArtStillAgrees()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            Assert.That(prefab, Is.Not.Null, $"Нет префаба рига: {RigPrefab}");

            UnitHeldItem[] items = prefab.GetComponentsInChildren<UnitHeldItem>(true);
            Assert.That(items, Is.Not.Empty, "В риге нет ни одного предмета — проверять нечего.");

            var complaints = new List<string>();

            foreach (UnitHeldItem item in items)
            {
                if (!item.HasDeclaredReach)
                {
                    complaints.Add($"{item.name}: вылет не объявлен. Нажми «Замерить вылет по мешу» на " +
                                   "компоненте — иначе длина оружия снова станет размером картинки.");
                    continue;
                }

                SpriteRenderer reach = item.ReachPart;
                if (reach == null || reach.sprite == null)
                {
                    complaints.Add($"{item.name}: объявлена рабочая часть без спрайта — сверить не с чем.");
                    continue;
                }

                Vector3 measuredWorld = reach.transform.TransformPoint(LocalTipFromMesh(reach));
                float measured = item.transform.InverseTransformPoint(measuredWorld).magnitude;
                float declared = item.DeclaredLength;

                float drift = Mathf.Abs(declared - measured) / Mathf.Max(measured, 1e-4f);
                if (drift > Tolerance)
                    complaints.Add($"{item.name}: объявлено {declared:F4}, арт даёт {measured:F4} " +
                                   $"(расхождение {drift * 100f:F1}%). Либо перезамерь, либо это осознанно — " +
                                   "и тогда порог надо обсудить, а не двигать молча.");
            }

            Assert.That(complaints, Is.Empty,
                "Объявленный вылет разошёлся с артом:\n  " + string.Join("\n  ", complaints));
        }

        /// <summary>
        /// Объявленное значение обязано ПОБЕЖДАТЬ замер: иначе поле есть, а работает по-прежнему картинка,
        /// и вся развязка существует только на бумаге.
        /// </summary>
        [Test]
        public void DeclaredReach_WinsOverTheMesh()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            Assert.That(prefab, Is.Not.Null, $"Нет префаба рига: {RigPrefab}");

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                UnitHeldItem item = null;
                foreach (UnitHeldItem candidate in instance.GetComponentsInChildren<UnitHeldItem>(true))
                    if (candidate.HasDeclaredReach && candidate.ReachPart != null) { item = candidate; break; }

                Assert.That(item, Is.Not.Null, "Не нашлось предмета с объявленным вылетом.");

                var so = new SerializedObject(item);
                so.FindProperty("_declaredLength").floatValue = item.DeclaredLength * 3f;   // заведомо не арт
                so.ApplyModifiedPropertiesWithoutUndo();

                var part = new UnitPart(0, item.ReachPart, item.name, BodySide.None,
                    HandSlot.None, item.Kind, isHand: false, isReach: true);

                Assert.That(UnitPartGeometry.TryGetTip(part, out Vector3 tip), Is.True);
                Assert.That(item.TryGetDeclaredTip(out Vector3 declaredTip), Is.True);
                Assert.That(Vector3.Distance(tip, declaredTip), Is.LessThan(1e-3f),
                    "Кончик обязан прийти из ОБЪЯВЛЕННОГО числа: втрое удлинённое объявление должно " +
                    "сдвинуть остриё втрое, а не остаться на мешe.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
