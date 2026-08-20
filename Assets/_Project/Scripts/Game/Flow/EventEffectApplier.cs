using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using UnityEngine;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Применяет последствия выбора текстового ивента (<see cref="EventEffect"/>) к <see cref="RunState"/>
    /// через <see cref="RunStateService"/> — единый центр-switch (план 11 §5.1). Тривиальное (золото, релик,
    /// вместимость) применяется реально; ещё не проведённое в бой (предмет) и произвольное (Custom) — пока
    /// дебаг-лог (как просил Макс: «хотя бы дебаг-лог»), но с честным хуком под будущую механику.
    /// </summary>
    public sealed class EventEffectApplier
    {
        private readonly RunStateService _runStates;
        // Золото и снятие реликвии — односторонние записи, они идут через шину команд и попадают в лог.
        // Выдача реликвии и вместимость остались прямыми: они спрашивают «вышло ли» синхронно, а это
        // транзакция (см. RunStateService.TrySpendGold, отложенный шаг транзакций в ТЗ кооп-вертикали).
        private readonly Guildmaster.Guild.Commands.IRunCommands _commands;

        public EventEffectApplier(RunStateService runStates,
            Guildmaster.Guild.Commands.IRunCommands commands)
        {
            _runStates = runStates;
            _commands  = commands;
        }

        /// <summary>Золото забега сейчас — экран ивента гасит по нему варианты не по карману.</summary>
        public int Gold => _runStates.Current?.Gold ?? 0;

        /// <summary>
        /// Применить последствия выбранного варианта. Золото проводится ОДНОЙ записью на нетто всех
        /// его последствий, до всего остального: не хватило — не применяется ничего.
        /// </summary>
        /// <remarks>
        /// <para>Раньше цена уходила той же односторонней записью, что и награда, а
        /// <c>RunStateService.AddGold</c> клампит результат в ноль — то есть выбор «купить за 50» с
        /// десятью золотыми списывал десять и всё равно выдавал награду.</para>
        /// <para><b>Почему нетто одной записью, а не по порядку.</b> Кламп в ноль ломает и сам порядок:
        /// «−30, потом +100» с пустым кошельком дало бы 100 вместо 70, потому что первое списание
        /// упёрлось в дно. Промежуточная сумма всё равно не видна нигде, кроме лога, а итог от порядка
        /// не зависит.</para>
        /// <para>Отказ громкий (<c>LogError</c>), потому что до applier дело доходит только через экран,
        /// а экран такой вариант уже погасил: сработавшая проверка означает разошедшийся гейт, а не
        /// поведение игрока.</para>
        /// </remarks>
        public bool Apply(IReadOnlyList<EventEffect> effects)
        {
            if (effects == null || effects.Count == 0) return true;

            int net = 0;
            for (int i = 0; i < effects.Count; i++)
                if (effects[i].Kind == EventEffectKind.Gold) net += effects[i].Amount;

            if (net < 0 && !_runStates.TrySpendGold(-net))
            {
                Debug.LogError($"[EventEffect] - вариант стоит {-net} золота, у игрока {Gold}. " +
                               "Не применено НИЧЕГО: экран обязан был погасить этот вариант.");
                return false;
            }

            if (net > 0) _commands.AddGold(net);
            if (net != 0) Debug.Log($"[EventEffect] - золото {net:+#;-#;0} → {_runStates.Current?.Gold}");

            for (int i = 0; i < effects.Count; i++)
                ApplyOne(effects[i]);

            _runStates.Autosave();
            return true;
        }

        private void ApplyOne(EventEffect e)
        {
            switch (e.Kind)
            {
                case EventEffectKind.Gold:
                    break; // золото проведено нетто-записью в Apply — здесь оно уже учтено

                case EventEffectKind.GrantRelic:
                    if (string.IsNullOrEmpty(e.ContentId)) { WarnNoId(e); break; }
                    bool added = _runStates.TryAddRelic(e.ContentId);
                    Debug.Log($"[EventEffect] - выдан релик '{e.ContentId}'" + (added ? "" : " — НЕ добавлен (запас полон)"));
                    break;

                case EventEffectKind.RemoveRelic:
                    if (string.IsNullOrEmpty(e.ContentId)) { WarnNoId(e); break; }
                    _commands.RemoveRelic(e.ContentId);
                    Debug.Log($"[EventEffect] - убран релик '{e.ContentId}'");
                    break;

                case EventEffectKind.GainRelicCapacity:
                    int gained = 0;
                    for (int k = 0; k < e.Amount; k++)
                        if (_runStates.IncreaseCapacity()) gained++; else break;
                    Debug.Log($"[EventEffect] - вместимость реликов +{gained} → {_runStates.Current?.RelicCapacity}");
                    break;

                case EventEffectKind.GrantItem:
                    // TODO(D1): проводка предмета в бой (RuntimeUnitFactory/party). Пока — дебаг-лог.
                    Debug.Log($"[EventEffect] - (заглушка) выдать предмет '{e.ContentId}' — проводка в бой позже");
                    break;

                case EventEffectKind.Custom:
                default:
                    Debug.Log($"[EventEffect] - (custom) {e.Note}");
                    break;
            }
        }

        private static void WarnNoId(EventEffect e) =>
            Debug.LogWarning($"[EventEffect] - {e.Kind}: пустой ContentId, пропущено");
    }
}
