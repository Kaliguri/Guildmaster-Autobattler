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

        /// <summary>Применить список последствий по порядку. Пишет автосейв, если что-то изменилось.</summary>
        public void Apply(IReadOnlyList<EventEffect> effects)
        {
            if (effects == null || effects.Count == 0) return;

            for (int i = 0; i < effects.Count; i++)
                ApplyOne(effects[i]);

            _runStates.Autosave();
        }

        private void ApplyOne(EventEffect e)
        {
            switch (e.Kind)
            {
                case EventEffectKind.Gold:
                    _commands.AddGold(e.Amount);
                    Debug.Log($"[EventEffect] - золото {e.Amount:+#;-#;0} → {_runStates.Current?.Gold}");
                    break;

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
