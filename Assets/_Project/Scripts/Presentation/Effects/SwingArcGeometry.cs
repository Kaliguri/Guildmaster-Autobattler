using Guildmaster.Presentation.Body;
using UnityEngine;

namespace Guildmaster.Presentation.Effects
{
    /// <summary>
    /// Геометрия взмаха: вокруг чего дуга вращается и где сейчас остриё. Единственный владелец ответа —
    /// его зовут и бой (<c>UnitView</c>), и редакторный стенд.
    /// </summary>
    /// <remarks>
    /// Вынесено 06.08.2026 из <c>UnitView</c>, когда стенд повторил этот расчёт своими руками и получил
    /// вторую правду о взмахе. Спор двух копий не заметен глазом сразу: дуга «почти такая же», а
    /// расходится в мелочах — стороне бьющей руки, выборе плеча, точке острия, — и разбирать это потом
    /// приходится по кадрам.
    /// </remarks>
    public static class SwingArcGeometry
    {
        /// <summary>
        /// Плечо и остриё для тела в его ТЕКУЩЕЙ позе.
        /// </summary>
        /// <param name="body">Тело юнита; поза берётся как есть — кто её поставил, здесь неважно.</param>
        /// <param name="pivot">Центр вращения — плечо бьющей руки.</param>
        /// <param name="tip">Остриё оружия (или кисти, если бьют рукой).</param>
        /// <param name="missingShoulder">
        /// Плечо не найдено: взмах есть, а вращать сектор не вокруг чего. Отделено от обычного «нечем
        /// бить», потому что это разводка рига, и звать о ней надо громко — но решает это вызывающий:
        /// бою нужен дефект-репорт, стенду достаточно молча не показать дугу.
        /// </param>
        public static bool TryResolve(IUnitBodyVisual body, out Vector3 pivot, out Vector3 tip,
                                      out bool missingShoulder)
        {
            pivot = default;
            tip   = default;
            missingShoulder = false;

            if (body?.Parts == null) return false;
            if (!body.Parts.TryGetStrikeSource(HandSlot.None, out UnitPart source)) return false;
            if (!UnitPartGeometry.TryGetTip(source, out tip)) return false;

            // Дуга идёт вокруг ПЛЕЧА, а не вокруг кисти: рука — жёсткий рычаг, и вращается вся плоскость
            // удара. Взяв центром кисть, мы получили бы короткий веер вокруг запястья, которого в
            // движении нет. Сторона — та же, что у бьющей руки: у бойца с двумя клинками левый взмах
            // обязан идти от левого плеча.
            BodySide side = source.Slot == HandSlot.Left ? BodySide.Left
                          : source.Slot == HandSlot.Right ? BodySide.Right
                          : source.Side;

            if (!body.Parts.TryGetBone(RigNaming.ShoulderBone(side), side, out UnitPart shoulder)
                || shoulder.Renderer == null)
            {
                missingShoulder = true;
                return false;
            }

            pivot = shoulder.Renderer.transform.position;
            return true;
        }

        /// <summary>
        /// Место дуги В ПОРЯДКЕ ОТРИСОВКИ ТЕЛА: куда её положить и с каким порядком.
        /// </summary>
        /// <param name="body">Тело бьющего.</param>
        /// <param name="parent">Узел группы сортировки тела — внутрь него дуга и переезжает.</param>
        /// <param name="sortingOrder">Порядок дуги: на единицу ниже бьющей части.</param>
        /// <remarks>
        /// Требование дословно (Макс, 06.08.2026): «След дуги должен быть ЗА мечом, тут все ок. Его слой —
        /// "я меч, но чуть ниже меча", относительно других частей тела». Слоем такое не выражается: тело
        /// сортируется <c>SortingGroup</c>'ой целиком, и снаружи дуга может быть только над ВСЕМ юнитом
        /// или под ВСЕМ юнитом — «между мечом и рукой» снаружи не существует. Поэтому дуга физически
        /// переезжает внутрь группы и получает порядок относительно того, чем бьют.
        /// <para>
        /// Порядок берётся у БЬЮЩЕЙ ЧАСТИ, а не числом из данных: у бойца с двумя клинками левый и правый
        /// лежат в разных слоях тела, и число в ассете было бы верным ровно для одного из них.
        /// </para>
        /// </remarks>
        public static bool TryResolveAnchor(IUnitBodyVisual body, out Transform parent, out int sortingOrder)
        {
            parent       = null;
            sortingOrder = 0;

            if (body?.Parts == null) return false;

            parent = body.SortingRoot;
            if (parent == null) return false;

            if (!body.Parts.TryGetStrikeSource(HandSlot.None, out UnitPart source) || source.Renderer == null)
                return false;

            sortingOrder = source.Renderer.sortingOrder - 1;
            return true;
        }

        /// <summary>Имя кости плеча для стороны бьющей руки — для сообщений о разводке.</summary>
        public static string ShoulderBoneFor(IUnitBodyVisual body)
        {
            if (body?.Parts == null) return RigNaming.ShoulderBone(BodySide.Right);
            if (!body.Parts.TryGetStrikeSource(HandSlot.None, out UnitPart source))
                return RigNaming.ShoulderBone(BodySide.Right);

            BodySide side = source.Slot == HandSlot.Left ? BodySide.Left
                          : source.Slot == HandSlot.Right ? BodySide.Right
                          : source.Side;
            return RigNaming.ShoulderBone(side);
        }
    }
}
