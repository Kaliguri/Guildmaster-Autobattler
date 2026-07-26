using System.ComponentModel;
using Guildmaster.Data.Stats;
using UnityEngine.Localization.SmartFormat.Core.Extensions;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Smart-Format форматтер разобранных статов: делает <c>«Наносит {dmg} урона»</c> рабочим
    /// шаблоном, куда слой описаний подаёт <see cref="FormattedStat"/>
    /// (план UI-реворка §II.10.2).
    /// </summary>
    /// <remarks>
    /// Главное свойство — форматтер БЕЗ СОСТОЯНИЯ: режим детализации и подписи единиц приезжают
    /// внутри аргумента, а не читаются откуда-то снаружи. Поэтому одна и та же строка в таблице
    /// рисует и краткий, и подробный вид, и второго текста описания заводить не нужно.
    /// <para>
    /// Регистрируется в списке Formatters в LocalizationSettings (это ассет — правится в
    /// редакторе, не кодом).
    /// </para>
    /// </remarks>
    // Список Formatters в LocalizationSettings хранится через [SerializeReference] — без атрибута
    // Unity не может записать наш форматтер в ассет и ругается на каждом импорте.
    [System.Serializable]
    [DisplayName("Guildmaster Stat")]
    public class StatValueFormatter : FormatterBase
    {
        /// <summary>
        /// «stat» — явный вызов (<c>{dmg:stat}</c>); пустое имя — согласие обслуживать БЕЗЫМЯННЫЙ
        /// плейсхолдер <c>{dmg}</c>. Без пустого имени описания пришлось бы писать с суффиксом на каждом
        /// числе, а забытый суффикс молча печатал бы <c>ToString()</c> структуры.
        /// </summary>
        public override string[] DefaultNames => new[] { "stat", "" };

        public override bool TryEvaluateFormat(IFormattingInfo formattingInfo)
        {
            if (formattingInfo.CurrentValue is FormattedStat stat)
            {
                formattingInfo.Write(StatFormat.Describe(stat));
                return true;
            }

            // Не наш тип — отдаём следующему форматтеру, а не притворяемся, что справились.
            return false;
        }
    }
}
