using System;
using System.Collections.Generic;
using System.Threading;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Запрос показать экран награды после боя (публикует флоу забега, слушает UI). Несёт витрину реликов
    /// и <see cref="OnResolved"/>-колбэк выбора — так Data-слой не зависит от UniTask (флоу оборачивает колбэк
    /// в await сам). Тем же приёмом, что <see cref="OpenLoadoutRequest"/>: пейлоад — только Data-типы.
    /// </summary>
    public readonly struct OpenRewardRequest
    {
        /// <summary>Витрина: 1..N реликов на выбор (обычно 3).</summary>
        public readonly IReadOnlyList<RelicData> Choices;

        /// <summary>Полон ли запас реликов: если да — взять награду можно, только сбросив один существующий.</summary>
        public readonly bool InventoryFull;

        /// <summary>Текущий запас реликов (content id) — для выбора, что сбросить при полном инвентаре.</summary>
        public readonly IReadOnlyList<string> CurrentInventory;

        /// <summary>
        /// Отдать свой голос: вариант из <see cref="RewardOptions"/>. Зовётся столько раз, сколько игрок
        /// передумывает.
        /// </summary>
        /// <remarks>
        /// <b>Не «результат», а голос</b> (07.08.2026). Награда общая, поэтому берут её все вместе:
        /// экран закрывается не по клику, а когда сошлись все — признаком срабатывания от общего
        /// решения. Пока здесь был колбэк результата, первый нажавший забирал реликвию за группу, а
        /// остальные узнавали об этом по исчезнувшему экрану.
        /// </remarks>
        public readonly Action<string> OnVote;

        /// <summary>Токен отмены забега (QA #37): отмена закрывает награду через навигатор.</summary>
        public readonly CancellationToken Cancellation;

        public OpenRewardRequest(IReadOnlyList<RelicData> choices, bool inventoryFull,
                                 IReadOnlyList<string> currentInventory, Action<string> onVote,
                                 CancellationToken cancellation = default)
        {
            Choices          = choices;
            InventoryFull    = inventoryFull;
            CurrentInventory = currentInventory;
            OnVote           = onVote;
            Cancellation     = cancellation;
        }
    }

    /// <summary>
    /// Варианты решения о награде: что группа делает с витриной.
    /// </summary>
    /// <remarks>
    /// <b>Вариант — строка, потому что общее решение сравнивает голоса как строки</b> и ничего не знает
    /// ни про реликвии, ни про инвентарь. Обмен поэтому едет ОДНИМ вариантом («взять это, выбросив то»),
    /// а не двумя решениями подряд: имущество общее целиком, и согласиться группа должна на обе половины
    /// сразу (решение Макса 07.08.2026). Разведи их по двум решениям — и между ними появится состояние
    /// «награду взяли, а место ещё не освободили», которого в забеге быть не должно.
    /// </remarks>
    public static class RewardOptions
    {
        /// <summary>Ничего не берём. Пропуск — такой же общий выбор, как и взятие.</summary>
        public const string Skip = "skip";

        /// <summary>Разделитель половин обмена. В id контента не встречается: там только `domain.name`.</summary>
        private const char SwapMark = '>';

        /// <summary>Взять реликвию — место есть.</summary>
        public static string Take(string relicId) => relicId;

        /// <summary>Взять реликвию, выбросив другую: инвентарь полон.</summary>
        public static string Swap(string relicId, string dropId) =>
            string.IsNullOrEmpty(dropId) ? relicId : relicId + SwapMark + dropId;

        /// <summary>
        /// Разобрать вариант. <c>false</c> — это пропуск: брать нечего и выбрасывать нечего.
        /// </summary>
        public static bool TryParse(string option, out string relicId, out string dropId)
        {
            relicId = null;
            dropId  = null;
            if (string.IsNullOrEmpty(option) || option == Skip) return false;

            int mark = option.IndexOf(SwapMark);
            if (mark < 0) { relicId = option; return true; }

            relicId = option.Substring(0, mark);
            dropId  = option.Substring(mark + 1);
            return true;
        }
    }

    /// <summary>Результат экрана награды (план 11 §5.4): что взяли и что сбросили ради места.</summary>
    public readonly struct RewardChoiceResult
    {
        /// <summary>Взятый релик, либо null = награда пропущена.</summary>
        public readonly RelicData Chosen;

        /// <summary>Content id сброшенного ради места релика, либо null (место было).</summary>
        public readonly string DropRelicId;

        public RewardChoiceResult(RelicData chosen, string dropRelicId)
        {
            Chosen      = chosen;
            DropRelicId = dropRelicId;
        }

        /// <summary>Награда пропущена (ничего не взято).</summary>
        public bool Skipped => Chosen == null;

        public static RewardChoiceResult Skip => new RewardChoiceResult(null, null);
        public static RewardChoiceResult Take(RelicData relic) => new RewardChoiceResult(relic, null);
        public static RewardChoiceResult Swap(RelicData relic, string dropId) => new RewardChoiceResult(relic, dropId);
    }
}
