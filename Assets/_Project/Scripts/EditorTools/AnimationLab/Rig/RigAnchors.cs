#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using Guildmaster.Presentation.Body;

namespace Guildmaster.AnimationLab.Editor
{
    /// <summary>
    /// Где сустав НА САМОМ ДЕЛЕ, если спросить арт: собирает пары «узел вращения ↔ пивот спрайта,
    /// который на нём висит», и меряет, насколько они разъехались.
    ///
    /// Существует потому, что точка вращения у нас описана дважды — узлом рига и пивотом спрайта, — и
    /// расходятся они МОЛЧА. Ни одной ошибки в консоли: просто рисунок начинает описывать дугу вокруг
    /// чужого центра, и это читается как «поехали Rotation Point», хотя поехал спрайт. Замер 03.08 на
    /// собранном вручную юните: плечо 11 px, предплечье 30, голова 62, меч 70.
    /// </summary>
    /// <remarks>
    /// Правило принадлежности одно: <b>пивот спрайта стоит в том суставе, под которым спрайт висит.</b>
    /// Обход идёт от узла сустава вниз и останавливается на следующем суставе — поэтому меч принадлежит
    /// хвату, а не локтю, хотя лежит глубоко под ним.
    /// <para>
    /// Владелец правила один на все инструменты: гизмо только ПОКАЗЫВАЕТ расхождение, а чинит его тот,
    /// кто переставляет узлы. Две копии этого обхода разошлись бы, и картинка начала бы оправдывать риг,
    /// который инструмент уже собрал иначе.
    /// </para>
    /// </remarks>
    public static class RigAnchors
    {
        /// <summary>Спрайт и сустав, за который он держится.</summary>
        public sealed class Anchor
        {
            public string JointId;
            public Transform Joint;
            public SpriteRenderer Visual;

            /// <summary>Мировая точка пивота спрайта — то есть где сустав, по мнению рисунка.</summary>
            public Vector3 PivotWorld;

            /// <summary>Расхождение «узел ↔ пивот» в мировых единицах.</summary>
            public float Offset;

            /// <summary>То же расхождение в пикселях исходного арта — в них его удобнее обсуждать.</summary>
            public float OffsetPixels;

            /// <summary>Масштаб, с которым кусок реально стоит в мире (единый на юнита — норма).</summary>
            public float Scale;

            /// <summary>
            /// Кусок объявил, что его пивот — точка крепления (пивот сдвинут с центра спрайта).
            /// Плейсхолдеры-кубики пивотятся по центру и живут в ДРУГОЙ модели: их положение задаётся
            /// смещением узла, и мерить у них «пивот против сустава» бессмысленно — судить их за это
            /// значит закрасить картинку красным там, где всё работает как задумано.
            /// </summary>
            public bool DeclaresPivot;

            public string SpriteName => Visual != null && Visual.sprite != null ? Visual.sprite.name : "(null)";

            public override string ToString() =>
                $"{JointId} <- {SpriteName}: {OffsetPixels:F0} px ({Offset:F4}), scale {Scale:F3}";
        }

        /// <summary>
        /// Все пары «сустав ↔ спрайт» юнита. <paramref name="rigRoot"/> — корень инстанса (или префаба),
        /// <paramref name="profile"/> задаёт, какие узлы вообще считаются суставами.
        /// </summary>
        public static List<Anchor> Collect(Transform rigRoot, RigProfile profile)
        {
            if (rigRoot == null) throw new System.ArgumentNullException(nameof(rigRoot));
            if (profile == null) throw new System.ArgumentNullException(nameof(profile));

            // Узлы всех суставов сразу: обход должен останавливаться на ЧУЖОМ суставе, иначе меч,
            // лежащий под локтем, был бы записан локтю и обвинён в расхождении в пол-предплечья.
            var jointNodes = new Dictionary<Transform, string>();
            foreach (var joint in profile.Joints)
            {
                var node = rigRoot.Find(joint.Path);
                if (node != null) jointNodes[node] = joint.Id;
            }

            var anchors = new List<Anchor>();
            foreach (var pair in jointNodes)
            {
                var visuals = new List<SpriteRenderer>();
                CollectVisuals(pair.Key, jointNodes, visuals);
                foreach (var sr in visuals)
                {
                    var pivotWorld = sr.transform.position;   // пивот спрайта = origin его трансформа
                    float offset = Vector3.Distance(pivotWorld, pair.Key.position);
                    anchors.Add(new Anchor
                    {
                        JointId = pair.Value,
                        Joint = pair.Key,
                        Visual = sr,
                        PivotWorld = pivotWorld,
                        Offset = offset,
                        OffsetPixels = offset * (sr.sprite != null ? sr.sprite.pixelsPerUnit : 100f),
                        Scale = sr.transform.lossyScale.x,
                        DeclaresPivot = HasDeclaredPivot(sr.sprite),
                    });
                }
            }

            anchors.Sort((a, b) => b.Offset.CompareTo(a.Offset));
            return anchors;
        }

        /// <summary>Расхождение, начиная с которого кусок считается сидящим не на своём суставе.</summary>
        public const float DefaultTolerancePixels = 4f;

        /// <summary>Куски, которые крутятся вокруг чужой точки: их поворот врёт тем сильнее, чем дальше пивот.</summary>
        public static List<Anchor> Offenders(List<Anchor> anchors, float tolerancePixels = DefaultTolerancePixels)
        {
            var bad = new List<Anchor>();
            foreach (var a in anchors)
                if (a.DeclaresPivot && a.OffsetPixels > tolerancePixels) bad.Add(a);
            return bad;
        }

        /// <summary>
        /// Пивот сдвинут с центра — значит он поставлен осознанно, в точку крепления. Центральный пивот
        /// (0.5, 0.5) — умолчание импортёра, и означает он «про эту точку никто ничего не сказал».
        /// </summary>
        public static bool HasDeclaredPivot(Sprite sprite)
        {
            if (sprite == null || sprite.rect.width <= 0f || sprite.rect.height <= 0f) return false;
            var normalized = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
            return Mathf.Abs(normalized.x - 0.5f) > 0.02f || Mathf.Abs(normalized.y - 0.5f) > 0.02f;
        }

        /// <summary>
        /// Масштабы кусков, встречающиеся в юните. Больше одного значения — куски живут в разных
        /// «размерах мира»: стыки можно свести в позе покоя, но пропорции нарисованного уже не те,
        /// что рисовал художник.
        /// </summary>
        public static Dictionary<float, int> ScaleHistogram(List<Anchor> anchors, float step = 0.001f)
        {
            var histogram = new Dictionary<float, int>();
            foreach (var a in anchors)
            {
                float key = Mathf.Round(a.Scale / step) * step;
                histogram.TryGetValue(key, out int count);
                histogram[key] = count + 1;
            }
            return histogram;
        }

        static void CollectVisuals(Transform node, Dictionary<Transform, string> stops, List<SpriteRenderer> into)
        {
            for (int i = 0; i < node.childCount; i++)
            {
                var child = node.GetChild(i);
                if (stops.ContainsKey(child) || RigNaming.IsJoint(child)) continue;

                var sr = child.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null) into.Add(sr);
                CollectVisuals(child, stops, into);
            }
        }
    }
}
#endif
