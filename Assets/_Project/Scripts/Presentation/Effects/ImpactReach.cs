using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Presentation.Effects
{
    /// <summary>
    /// Радиус круга атаки для выбора зоны попадания — тот же, которым бой отвечает на вопрос
    /// «достал ли этот удар».
    /// </summary>
    /// <remarks>
    /// <para>
    /// Класс существует ради одного инварианта: <b>показ и симуляция считают досягаемость одной
    /// формулой</b>. Пока формула жила только в презентере, она знала единственный круг
    /// (<c>CombatPositioning.ReachCenter</c>) — а линейная авто-атака бьёт ПОЛОСОЙ, которая длиннее
    /// этого круга во столько раз, во сколько её удлиняет кит
    /// (<c>AutoAttackSystem.DealLineDamage</c>: <c>Reach * AutoAttackLengthMult</c>), и задевает всех
    /// врагов в ней, а не только выбранную цель. Копейщик на этом и ловился: бой засчитывал удар
    /// второму врагу в полосе, показ не находил на нём ни одной достижимой зоны и ругался дефектом
    /// на совершенно штатном ударе (найдено 2026-08-20).
    /// </para>
    /// <para>
    /// Формулу держит тест, а не комментарий: она живёт по обе стороны шва sim→presentation, и
    /// нарушить её можно из боевого файла, не открыв этот.
    /// </para>
    /// </remarks>
    public static class ImpactReach
    {
        /// <summary>
        /// Круг попадания авто-атаки: для одиночного удара — <paramref name="reachCenter"/> как есть,
        /// для полосы — расстояние до её дальнего УГЛА.
        /// </summary>
        /// <param name="reachCenter">Круг симуляции: зазор атаки плюс радиусы обоих тел.</param>
        /// <param name="shape">Форма авто-атаки кита (<see cref="UnitData.AutoAttackShape"/>).</param>
        /// <param name="lengthMult">Во сколько раз полоса длиннее круга выбора цели.</param>
        /// <param name="width">Полная ширина полосы; вбок от оси цель пускают на её половину.</param>
        public static float ForAutoAttack(float reachCenter, AreaShape shape, float lengthMult, float width)
        {
            if (shape != AreaShape.Line) return reachCenter;

            // Множитель повторяется БУКВАЛЬНО, без страховочного Max(1): полоса короче круга — это
            // решение контента, и показ обязан согласиться с ним, а не спорить.
            float length = reachCenter * lengthMult;
            if (length <= 0f) return reachCenter;

            // Угол полосы, а не её конец: narrow-phase в QueryUnitsInLine пускает цель на полуширину
            // вбок от оси, и такая цель лежит от бьющего дальше, чем на length.
            float halfWidth = Mathf.Max(0f, width) * 0.5f;
            return Mathf.Sqrt(length * length + halfWidth * halfWidth);
        }
    }
}
