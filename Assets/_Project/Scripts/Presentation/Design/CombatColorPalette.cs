using UnityEngine;

namespace Guildmaster.Presentation.Design
{
    /// <summary>
    /// Цвета боевого UI: полосы HP по принадлежности юнита к СМОТРЯЩЕМУ (свои — одним цветом, чужие —
    /// другим) и полоса щита. Единственный вход презентации за этими цветами; хардкодить их на префабе
    /// нельзя (T-12/T-13 — тема, которая возвращалась дважды).
    /// <para><b>Своих значений здесь больше нет.</b> Цвет живёт в палитре проекта
    /// (<c>UI/Theme/tokens.*.uss</c> → <see cref="Guildmaster.Data.Definitions.GuildmasterPalette"/>),
    /// а этот ассет только называет роли. Иначе бой и интерфейс расходятся молча — ровно так уже
    /// случилось с картой акта, где три цвета уехали от токенов, которые сами же называли.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Design/Combat Color Palette", fileName = "CombatColorPalette")]
    public sealed class CombatColorPalette : ScriptableObject
    {
        [Tooltip("Снимок токенов дизайн-системы: отсюда берутся цвета полос HP и щита " +
                 "(--gm-color-combat-*). Пересобрать — Alebardium → Дизайн-система → Пересобрать палитру.")]
        [SerializeField] private Guildmaster.Data.Definitions.GuildmasterPalette _palette;

        /// <summary>Полоса щита (absorb-пул над HP). Одна на всех, принадлежность не меняет её.</summary>
        public Color Shield => Role("--gm-color-combat-shield");

        // Сырых AllyHp/EnemyHp наружу нет: цвет по принадлежности отдаёт HealthBarColor, и он
        // единственный способ его получить — иначе у одного факта снова два входа (T-12/T-13).

        /// <summary>Цвет HP-бара по признаку «союзник ли юнит для смотрящего».</summary>
        public Color HealthBarColor(bool isAllyOfViewer) =>
            Role(isAllyOfViewer ? "--gm-color-combat-hp-ally" : "--gm-color-combat-hp-enemy");

        /// <summary>
        /// Цвет роли из палитры. Пустая ссылка или неизвестное имя — баг разводки: говорим вслух и
        /// отдаём пурпур. Тихо подставлять «похожий» цвет нельзя: полосы HP — то, по чему игрок читает
        /// бой, и неверный цвет здесь врёт про принадлежность юнита.
        /// </summary>
        private Color Role(string token)
        {
            if (_palette == null)
            {
                Debug.LogError($"[CombatColorPalette] - палитра не назначена, цвет '{token}' взять неоткуда " +
                               $"(ассет {name}).");
                return Color.magenta;
            }

            if (_palette.TryGet(token, out Color color)) return color;

            Debug.LogError($"[CombatColorPalette] - в палитре нет роли '{token}'. Пересобери снимок: " +
                           "Alebardium → Дизайн-система → Пересобрать палитру.");
            return Color.magenta;
        }
    }
}
