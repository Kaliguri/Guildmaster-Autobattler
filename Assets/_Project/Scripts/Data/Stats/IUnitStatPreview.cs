using System.Collections.Generic;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Data.Stats
{
    /// <summary>
    /// Одна строка стат-сводки для интерфейса: ключ локализации подписи + уже отформатированное
    /// значение. Форматирование живёт на стороне реализации (одно место на игру), UI только рисует.
    /// </summary>
    public readonly struct UnitStatLine
    {
        /// <summary>Ключ локализации подписи (<c>ui.stat.*</c>).</summary>
        public readonly string LabelKey;

        /// <summary>Подпись-фолбэк на RU, если ключ не резолвится.</summary>
        public readonly string LabelFallback;

        /// <summary>Готовое к показу значение («120», «1.2»).</summary>
        public readonly string Value;

        public UnitStatLine(string labelKey, string labelFallback, string value)
        {
            LabelKey      = labelKey;
            LabelFallback = labelFallback;
            Value         = value;
        }
    }

    /// <summary>
    /// Шов «покажи базовые статы этого кита» для интерфейса. Живёт в Data, потому что UI по asmdef
    /// не видит боевую сборку (<c>Guildmaster.Combat</c>), а дублировать формулу каскада в UI нельзя —
    /// числа разошлись бы с боем. Реализация — <c>Guildmaster.Combat.UnitStatPreview</c>,
    /// регистрируется в корневом скоупе.
    /// <para>Здесь ТОЛЬКО базовая восьмёрка «быстрого чтения». Полная таблица всех 30 статов —
    /// отдельный экран (реш. Макса 2026-07-25), этот шов под неё не растягивать.</para>
    /// </summary>
    public interface IUnitStatPreview
    {
        /// <summary>Базовая сводка кита; порядок строк стабилен. <c>null</c> data → пустой список.</summary>
        IReadOnlyList<UnitStatLine> Basic(UnitData data);
    }
}
