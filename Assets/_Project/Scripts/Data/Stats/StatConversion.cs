using System;
using UnityEngine;

namespace Guildmaster.Data.Stats
{
    /// <summary>Как стат превращается в прибавку к числу.</summary>
    public enum ScalingForm
    {
        /// <summary>Прямая: <c>итог = база + PerUnit × превышение</c>. Для чисел, которые должны РАСТИ.</summary>
        Linear = 0,

        /// <summary>
        /// Обратная: <c>итог = база / (1 + PerUnit × превышение)</c>. Для чисел, которые должны УБЫВАТЬ
        /// (кулдаун, время каста). Форма выбрана ради того, что она никогда не даёт ноль — значит и капа
        /// не требует (решения по Убийце и Магу молний, 2026-07-28).
        /// </summary>
        Inverse = 1,
    }

    /// <summary>
    /// Правило «стат носителя → число». Одна формула на все места, где стат должен во что-то
    /// конвертироваться: параметры способности (кулдаун, время каста, множитель удара), кулдаун зарядов
    /// реактива, сила усиления. До неё каждый такой пересчёт пришлось бы писать заново.
    /// <para><b>Числа:</b> <c>Baseline</c> — значение стата, при котором работает ровно база; в дело идёт
    /// только превышение над ним. <c>PerUnit</c> — прибавка на единицу превышения.</para>
    /// <para><b>Почему превышение, а не сырой стат:</b> сырое значение обессмысливает базовое число в
    /// ассете — «КД 5 с» читалось бы как «5 с только при нулевой скорости атаки». База должна значить
    /// то, что написано, для нормального носителя кита. Стат НИЖЕ базы прибавки не даёт: способность не
    /// становится хуже своей базы.</para>
    /// </summary>
    [Serializable]
    public struct StatConversion
    {
        [Tooltip("От какого стата считаем (обычно AttackSpeed).")]
        public StatType Source;

        [Tooltip("Прямая (число растёт) или обратная (число убывает и никогда не достигает нуля).")]
        public ScalingForm Form;

        [Tooltip("Значение стата, при котором работает ровно база. Ниже базы прибавки нет.")]
        public float Baseline;

        [Tooltip("Прибавка на единицу превышения базы. 0 = правило выключено.")]
        public float PerUnit;

        /// <summary>Применить правило. Читатель <c>null</c>, нулевой <c>PerUnit</c> или стат ниже базы — база без изменений.</summary>
        public float Apply(float baseValue, IStatReader stats)
        {
            if (stats == null || PerUnit == 0f) return baseValue;

            float excess = stats.Get(Source) - Baseline;
            if (excess <= 0f) return baseValue;

            return Form == ScalingForm.Inverse
                ? baseValue / (1f + PerUnit * excess)
                : baseValue + PerUnit * excess;
        }

        /// <summary>Свести список правил на одно базовое число, по порядку из данных.</summary>
        /// <remarks>
        /// Порядок берётся из ассета, а не из сортировки: у обратной формы он значим, и одно и то же
        /// содержимое обязано давать одно и то же число между сборками.
        /// </remarks>
        public static float ApplyAll(StatConversion[] rules, float baseValue, IStatReader stats)
        {
            if (rules == null || rules.Length == 0) return baseValue;

            float value = baseValue;
            for (int i = 0; i < rules.Length; i++) value = rules[i].Apply(value, stats);
            return value;
        }
    }
}
