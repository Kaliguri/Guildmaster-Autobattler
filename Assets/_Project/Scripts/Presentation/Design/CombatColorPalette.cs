using UnityEngine;

namespace Guildmaster.Presentation.Design
{
    /// <summary>
    /// Первый SO дизайн-системы боя: палитра цветов боевого UI. Пока — только цвета HP-бара по
    /// принадлежности юнита к СМОТРЯЩЕМУ (союзник / враг): классическая читаемость «свои — одним
    /// цветом, чужие — другим». Единый источник правды для цвета HP-бара; презентация тянет цвет
    /// отсюда, а не хардкодит его на префабе.
    /// <para>
    /// English: first combat-UI design-system ScriptableObject. For now holds only the HP-bar colors
    /// by the unit's relation to the local viewer (ally / enemy). Single source of truth for HP-bar
    /// color — presentation reads it from here instead of hardcoding on the prefab.
    /// </para>
    /// <para>Расширяется позже (цвета ресурс-бара, статус-иконок, дельты урона/хила и т.п.) — не сейчас (YAGNI).</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Design/Combat Color Palette", fileName = "CombatColorPalette")]
    public sealed class CombatColorPalette : ScriptableObject
    {
        [Header("HP-бар: цвет по принадлежности к смотрящему")]
        [Tooltip("HP-бар союзника смотрящего (его команда).")]
        [SerializeField] private Color _allyHp = new Color(0.30f, 0.85f, 0.35f);

        [Tooltip("HP-бар врага смотрящего (чужая команда).")]
        [SerializeField] private Color _enemyHp = new Color(0.90f, 0.25f, 0.25f);

        public Color AllyHp => _allyHp;
        public Color EnemyHp => _enemyHp;

        /// <summary>Цвет HP-бара по признаку «союзник ли юнит для смотрящего».</summary>
        public Color HealthBarColor(bool isAllyOfViewer) => isAllyOfViewer ? _allyHp : _enemyHp;
    }
}
