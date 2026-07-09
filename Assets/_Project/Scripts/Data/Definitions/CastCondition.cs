namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Условие каста активной способности (блок D ГД-модели AI, вики «13» §2.1, шаг 4).
    /// «Дешёвое» решение «кастовать ли» — проверяется per-tick в <c>AbilitySystem</c>, не в мозге.
    /// Отмена условия (блок E) — отдельным порогом HP% кастующего на <see cref="AbilityData"/>.
    /// </summary>
    public enum CastCondition
    {
        /// <summary>Кастовать как только готова (ресурс/КД). Дефолт — поведение плейсхолдера Ф2.</summary>
        Immediately = 0,

        /// <summary>Кастовать, когда врагов в радиусе условия ≥ заданного X (кластер-ульта).</summary>
        EnemiesInRadius = 1,

        /// <summary>Кастовать, когда HP% выбранной цели ≤ CastConditionHpPct (блок D хилера: спасать раненого союзника).</summary>
        AllyTargetHpBelowPct = 2,
    }
}
