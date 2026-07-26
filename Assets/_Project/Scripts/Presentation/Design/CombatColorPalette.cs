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

        [Tooltip("HP-бар врага смотрящего (чужая команда). Алый/оранжево-красный (vermilion): контрастит и с " +
                 "зелёным полем, и с красными телами юнитов, отстроен от маны-синего и хил-зелёного.")]
        [SerializeField] private Color _enemyHp = new Color(1.0f, 0.40f, 0.13f);

        [Header("Щит (absorb-пул над HP)")]
        [Tooltip("Цвет полоски щита. Пастельно-циан/бело-голубой — отстроен от HP (зелёный/красный) и " +
                 "маны (синий) по яркости и оттенку; читается без опоры на hue (дальтоник-safe). Один для " +
                 "всех, вне зависимости от принадлежности юнита.")]
        [SerializeField] private Color _shield = new Color(0.62f, 0.86f, 1.0f);

        public Color Shield => _shield;

        // Сырых AllyHp/EnemyHp здесь нет: цвет по принадлежности отдаёт HealthBarColor, и он
        // единственный способ его получить — иначе у одного факта снова два входа (T-12/T-13).

        /// <summary>Цвет HP-бара по признаку «союзник ли юнит для смотрящего».</summary>
        public Color HealthBarColor(bool isAllyOfViewer) => isAllyOfViewer ? _allyHp : _enemyHp;
    }
}
