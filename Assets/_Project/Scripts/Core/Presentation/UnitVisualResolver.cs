using UnityEngine;

namespace Guildmaster.Core.Presentation
{
    /// <summary>
    /// ЕДИНЫЙ источник правил визуала юнита (тинт + разворот) — чистые функции от имени юнита и команды.
    /// Раньше правила были размазаны: тинт жил в <c>CombatPresenter.TintFor</c>, флип — в <c>UnitView.Bind</c>.
    /// Из-за этого превью вне боя (roster-stage, таро-карточки) не могло получить «как в бою», не протащив
    /// за собой весь боевой презентер. Теперь все потребители (боевой презентер, карточки, roster-stage)
    /// зовут ОДИН источник — юнит выглядит одинаково везде.
    /// <para>Пока у всех юнитов один placeholder-спрайт, тинт по хэшу имени даёт стабильный уникальный
    /// оттенок на персонажа (dev-различение). Когда придут разные спрайты — визуал уже един из одной точки.</para>
    /// </summary>
    public static class UnitVisualResolver
    {
        /// <summary>
        /// Тинт тела по персонажу: у именованного юнита — стабильный оттенок от имени (HSV по хэшу),
        /// у безымянного болванчика — по команде (сине/красный). Совпадает с прежним <c>CombatPresenter.TintFor</c>.
        /// </summary>
        public static Color TintFor(string unitName, int team)
        {
            if (!string.IsNullOrEmpty(unitName))
            {
                float hue = (Mathf.Abs(unitName.GetHashCode()) % 360) / 360f;
                return Color.HSVToRGB(hue, 0.5f, 1f);
            }
            return team == 0 ? new Color(0.7f, 0.8f, 1f) : new Color(1f, 0.7f, 0.7f);
        }

        /// <summary>
        /// Горизонтальный разворот: спрайты нарисованы лицом вправо. Команда 0 (слева) смотрит вправо,
        /// команда врага (справа) — влево. Тот же инвариант, что стартовый флип в <c>UnitView.Bind</c>.
        /// </summary>
        public static bool FlipXFor(int team) => team != 0;
    }
}
