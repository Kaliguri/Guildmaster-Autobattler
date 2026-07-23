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
    [DisplayName("Guildmaster Stat")]
    public class StatValueFormatter : FormatterBase
    {
        public override string[] DefaultNames => new[] { "stat" };

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
