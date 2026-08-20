using UnityEngine;

namespace Guildmaster.Presentation.Body
{
    /// <summary>
    /// Замер НАПРАВЛЕНИЯ УДАРА по клипу атаки: куда двигался кончик оружия на кадре контакта.
    /// </summary>
    /// <remarks>
    /// Отдельный класс, а не метод вида, ровно по одной причине: этот замер обязан проверяться тестом
    /// на живых ассетах. Инвариант «клип атаки называет направление удара» живёт между клипом, ригом и
    /// кодом — комментарием его не удержать, а тест падает, когда маркер контакта уезжает или оружие
    /// перестаёт двигаться в кадре хита.
    /// <para>
    /// <b>Почему замер, а не поза в момент удара.</b> Событие урона приходит внутри <c>Update</c>
    /// презентера (<c>DefaultExecutionOrder(-100)</c>) — до <c>Update</c> видов и заведомо до того, как
    /// Animator применит позу. Кости там стоят в прошлом кадре: 15–20° ошибки на медленной атаке и
    /// 60–100° на быстрой, где взмах в 200° укладывается в два-три кадра показа.
    /// </para>
    /// </remarks>
    public static class StrikeDirectionMeasure
    {
        /// <summary>Доля длины клипа на шаг замера — примерно два кадра показа.</summary>
        private const float StepShare = 0.02f;

        private const float MinStep = 1f / 120f;
        private const float MaxStep = 1f / 30f;

        /// <summary>
        /// Замерить направление удара двумя сэмплами вокруг кадра контакта.
        /// </summary>
        /// <param name="clip">Клип атаки.</param>
        /// <param name="source">Ударная часть — предмет в руке либо кисть безоружного.</param>
        /// <param name="root">
        /// Корень тела — узел, ОТ КОТОРОГО клип адресует кости, и он же система координат результата.
        /// У скелетного вида это <c>BoneVisual</c>: пути в клипе начинаются с <c>Hips/…</c>, а Hips —
        /// его прямой ребёнок. Взять вместо него объект с Animator (на префабе он сидит в СОСЕДНЕЙ
        /// ветке) значит применить клип мимо костей — замер честно вернёт «кончик стоит на месте».
        /// </param>
        /// <param name="hitNormalized">Доля клипа до маркера контакта.</param>
        /// <param name="dirLocal">Единичное направление в координатах <paramref name="root"/>.</param>
        /// <returns><c>false</c> — замерить не удалось; вызывающий обязан сказать об этом вслух.</returns>
        public static bool TryMeasure(AnimationClip clip, in UnitPart source,
                                      Transform root, float hitNormalized, out Vector2 dirLocal)
        {
            dirLocal = default;
            if (clip == null || root == null || clip.length <= 0f) return false;

            float hit  = Mathf.Clamp01(hitNormalized) * clip.length;
            // Шаг имеет коридор: мельче — ловим шум кривой, крупнее — усредняем дугу и врём в ту же
            // сторону, что и отменённая хорда «замах → цель».
            float step = Mathf.Clamp(clip.length * StepShare, MinStep, MaxStep);
            float t0   = Mathf.Max(0f, hit - step);
            float t1   = Mathf.Min(clip.length, hit + step);
            if (t1 - t0 < 1e-4f) return false;

            // Перезаписи между выборками бояться нечего: Animator ставит позу в своей фазе, между
            // Update и LateUpdate, а обе выборки идут подряд.
            if (!SampleTip(clip, t0, root.gameObject, source, root, out Vector2 p0)) return false;
            if (!SampleTip(clip, t1, root.gameObject, source, root, out Vector2 p1)) return false;

            Vector2 delta = p1 - p0;
            if (delta.sqrMagnitude < 1e-10f) return false;

            dirLocal = delta.normalized;
            return true;
        }

        private static bool SampleTip(AnimationClip clip, float time, GameObject host, in UnitPart source,
                                      Transform root, out Vector2 local)
        {
            local = default;
            clip.SampleAnimation(host, time);
            if (!UnitPartGeometry.TryGetTip(source, out Vector3 tip)) return false;
            local = root.InverseTransformPoint(tip);
            return true;
        }
    }
}
