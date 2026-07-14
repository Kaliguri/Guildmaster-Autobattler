using System;
using System.Collections.Generic;
using Guildmaster.Core.Persistence;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Владелец durable-состояния забега (<see cref="RunState"/>) и единая точка его сейва/загрузки и правил
    /// вместимости коллекции реликов (план 11 §5.4). Три точки автосейва (вики «7» §5) зовут <see cref="Autosave"/>
    /// на переходах флоу. Сетевой-ready: хост владеет состоянием, автосейв = снапшот для репликации/реконнекта.
    /// </summary>
    public sealed class RunStateService
    {
        private const string SaveKey = "run";

        private readonly ISaveService _save;
        private readonly GameConfig   _config;

        public RunState Current { get; private set; }

        public RunStateService(ISaveService save, GameConfig config)
        {
            _save   = save;
            _config = config;
        }

        /// <summary>Есть ли автосейв забега на диске (для «Продолжить» в меню).</summary>
        public bool HasSave => _save.Exists(SaveKey);

        /// <summary>Начать новый забег: свежий <see cref="RunState"/> с базовой вместимостью реликов из конфига.</summary>
        public RunState NewRun(long seed, RosterSlot[] guild)
        {
            guild ??= Array.Empty<RosterSlot>();
            Current = new RunState
            {
                Seed          = seed,
                RelicCapacity = _config.RelicCapacityBase,
                Guild         = guild,
                SlotOwner     = new int[guild.Length], // соло: все 0
            };
            return Current;
        }

        /// <summary>Загрузить забег из автосейва (или null, если нет). Устанавливает <see cref="Current"/>.</summary>
        public RunState Load()
        {
            Current = _save.Load<RunState>(SaveKey);
            return Current;
        }

        /// <summary>Изменить золото забега (±). Клампится в ноль снизу. No-op без активного забега.</summary>
        public void AddGold(int delta)
        {
            if (Current == null) return;
            Current.Gold = System.Math.Max(0, Current.Gold + delta);
        }

        /// <summary>Снапшот текущего забега на диск (точка автосейва). No-op без активного забега.</summary>
        public void Autosave()
        {
            if (Current != null) _save.Save(SaveKey, Current);
        }

        /// <summary>Удалить автосейв (конец/сброс забега).</summary>
        public void DeleteSave() => _save.Delete(SaveKey);

        // ── Вместимость коллекции реликов (план 11 §5.4) ─────────────────────

        /// <summary>Полон ли запас реликов (нельзя взять награду без сброса).</summary>
        public bool RelicInventoryFull =>
            Current != null && Current.RelicInventory.Length >= Current.RelicCapacity;

        /// <summary>Добавить релик в запас, если есть место. false = полно (игрок должен сбросить/пропустить).</summary>
        public bool TryAddRelic(string relicId)
        {
            if (Current == null || string.IsNullOrEmpty(relicId) || RelicInventoryFull) return false;
            var list = new List<string>(Current.RelicInventory) { relicId };
            Current.RelicInventory = list.ToArray();
            return true;
        }

        /// <summary>Убрать один экземпляр релика из запаса (сброс ради места под награду).</summary>
        public void RemoveRelic(string relicId)
        {
            if (Current == null) return;
            var list = new List<string>(Current.RelicInventory);
            if (list.Remove(relicId)) Current.RelicInventory = list.ToArray();
        }

        /// <summary>Увеличить вместимость на 1 (товар магазина/награда), до потолка. false = уже потолок.</summary>
        public bool IncreaseCapacity()
        {
            if (Current == null || Current.RelicCapacity >= _config.RelicCapacityMax) return false;
            Current.RelicCapacity++;
            return true;
        }
    }
}
