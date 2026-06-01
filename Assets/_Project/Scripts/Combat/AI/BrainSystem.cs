using System.Collections.Generic;
using Guildmaster.Core.Simulation;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Тик AI (вики «13» §3.2, §4.1). Заменяет <c>TargetingSystem</c> в порядке тика.
    /// Каденс 10 Гц поверх 30-Гц сим: юнит думает на тике, где <c>tick % AiTickInterval == BrainPhase</c>
    /// (стаггер по <c>Id</c> — нагрузка распределена), ИЛИ когда взведён <c>BrainDirty</c> (событийное
    /// прерывание — цель погибла). Между AI-тиками интент «залипает»: движение/атака исполняют прошлое
    /// решение. Обход строго по индексу списка — порядок решений фиксирован ⇒ детерминизм (гейт S1).
    /// </summary>
    public sealed class BrainSystem
    {
        // Дефолтный мозг для юнитов без явного профиля (TargetingSystem «поглощается», §3.5):
        // профиль по умолчанию = Nearest-враг — ровно прежнее поведение Фазы 1.
        private readonly IUnitBrain _defaultBrain = new ProfileBrain(null);

        /// <summary>Переоценить решения тех юнитов, у кого совпала фаза или взведён dirty.</summary>
        public void Tick(IReadOnlyList<RuntimeUnit> units, IBattleView view)
        {
            int tick = view.CurrentTick;

            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                if (u.IsDead) continue;

                bool onPhase = tick % SimConstants.AiTickInterval == u.BrainPhase;
                // Прерывание: цель погибла → переоценка немедленно, не дожидаясь своей фазы.
                // Явный BrainDirty (взводит event-queue, §3.4) — задел; покрывает «погибшая цель» и здесь.
                bool dirty = u.BrainDirty || (u.CurrentTarget != null && u.CurrentTarget.IsDead);
                if (!onPhase && !dirty) continue;

                (u.Brain ?? _defaultBrain).Decide(u, view);
                u.BrainDirty = false;
            }
        }
    }
}
