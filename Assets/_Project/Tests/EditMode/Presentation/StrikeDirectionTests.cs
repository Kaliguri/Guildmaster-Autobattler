using Guildmaster.Data.Definitions;
using Guildmaster.Presentation.Body;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Инвариант «клип атаки называет НАПРАВЛЕНИЕ УДАРА» — он живёт между тремя вещами сразу: клипом с
    /// маркером контакта, ригом с оружием и кодом замера. Ни одна из сторон не узнает о поломке сама.
    ///
    /// Тест написан после того, как замер дважды соврал молча — и оба раза одинаково, «кончик оружия
    /// стоит на месте», видимое только в игре:
    /// <list type="number">
    /// <item>Animator выключался на время замера, а <c>SampleAnimation</c> без него не делает ничего.</item>
    /// <item>Сэмплировался объект с Animator — а он на префабе вида сидит в СОСЕДНЕЙ ветке
    /// (<c>Visual Sprites/Body</c>), тогда как кости живут под <c>Visual Sprites/BoneVisual</c>, и пути
    /// клипа (<c>Hips/…</c>) разрешаются только от него.</item>
    /// </list>
    /// Отсюда и диагностика в сообщении об ошибке: без путей иерархии причина неотличима от «клип не
    /// анимирует оружие».
    /// </summary>
    public class StrikeDirectionTests
    {
        /// <summary>Путь узла относительно предка — им клип и адресует кости.</summary>
        static string PathOf(Transform root, Transform node)
        {
            if (node == null) return "<null>";
            string path = node.name;
            for (Transform t = node.parent; t != null && t != root; t = t.parent) path = t.name + "/" + path;
            return path;
        }

        const string ViewPrefab = "Assets/_Project/Prefabs/Units/UnitView_BoneStorybook.prefab";
        const string AttackClip = "Assets/_Project/Prefabs/Bones/Attack.anim";

        /// <summary>
        /// Замер обязан состояться и назвать направление, которое идёт ВНИЗ: рубящий удар приходит
        /// сверху. Горизонтальный ответ — это ровно та ошибка, ради которой построение по хорде
        /// «замах → цель» и было отменено 06.08.2026.
        /// </summary>
        [Test]
        public void AttackClipYieldsADownwardStrikeDirection()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ViewPrefab);
            Assert.That(prefab, Is.Not.Null, $"Нет префаба вида: {ViewPrefab}");

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AttackClip);
            Assert.That(clip, Is.Not.Null, $"Нет клипа атаки: {AttackClip}");

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                var animator = instance.GetComponentInChildren<Animator>(true);
                Assert.That(animator, Is.Not.Null, "У вида нет Animator — сэмплировать нечего.");

                var body = instance.GetComponentInChildren<SkeletalBodyVisual>(true);
                Assert.That(body, Is.Not.Null, "У вида нет скелетного тела.");
                Assert.That(body.Parts, Is.Not.Null, "Тело не отдаёт части.");

                Assert.That(body.Parts.TryGetStrikeSource(HandSlot.None, out UnitPart source), Is.True,
                    "Тело не отдаёт, ЧЕМ бьют: ни предмета в хвате, ни кисти.");

                float hitNormalized = ClipMarkers.HitNormalized(clip);
                Assert.That(hitNormalized, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f),
                    "В клипе атаки нет маркера контакта — замер не от чего отсчитывать.");

                // Диагностика в сообщении: когда замер не удаётся, важно видеть ЧТО именно не сдвинулось.
                float hit = hitNormalized * clip.length;
                clip.SampleAnimation(body.Root.gameObject, Mathf.Max(0f, hit - 0.03f));
                UnitPartGeometry.TryGetTip(source, out Vector3 tipA);
                Vector3 boneA = source.Renderer != null ? source.Renderer.transform.position : Vector3.zero;
                clip.SampleAnimation(body.Root.gameObject, Mathf.Min(clip.length, hit + 0.03f));
                UnitPartGeometry.TryGetTip(source, out Vector3 tipB);
                Vector3 boneB = source.Renderer != null ? source.Renderer.transform.position : Vector3.zero;

                string probe = $"clip={clip.name} len={clip.length:F3} hitN={hitNormalized:F3} " +
                               $"bone={source.Bone} tip={tipA}->{tipB} node={boneA}->{boneB} " +
                               $"ctrl={(animator.runtimeAnimatorController != null)} " +
                               $"animatorPath=[{PathOf(instance.transform, animator.transform)}] " +
                               $"rootPath=[{PathOf(instance.transform, body.Root)}] " +
                               $"weaponPath=[{PathOf(instance.transform, source.Renderer.transform)}]";

                bool measured = StrikeDirectionMeasure.TryMeasure(
                    clip, source, body.Root, hitNormalized, out Vector2 dir);

                Assert.That(measured, Is.True,
                    "Замер не состоялся: на кадре контакта кончик оружия не двигается. " + probe);

                Assert.That(dir.magnitude, Is.EqualTo(1f).Within(1e-3f), "Направление не единичное.");

                // Вниз — значит y отрицательный. Порог мягкий: важно не точное число, а то, что удар
                // не лёг горизонтально, как это делала отменённая хорда (она давала ~20° к горизонту).
                Assert.That(dir.y, Is.LessThan(-0.35f),
                    $"Направление удара {dir} не идёт вниз — рубящий удар приходит сверху, и знак " +
                    "обязан лежать вдоль этого движения, а не поперёк.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
