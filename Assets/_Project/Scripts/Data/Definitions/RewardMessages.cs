using System;
using System.Collections.Generic;

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

        /// <summary>Колбэк результата выбора (ровно один вызов): взятый релик + опц. сброшенный, либо пропуск.</summary>
        public readonly Action<RewardChoiceResult> OnResolved;

        public OpenRewardRequest(IReadOnlyList<RelicData> choices, bool inventoryFull,
                                 IReadOnlyList<string> currentInventory, Action<RewardChoiceResult> onResolved)
        {
            Choices          = choices;
            InventoryFull    = inventoryFull;
            CurrentInventory = currentInventory;
            OnResolved       = onResolved;
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
