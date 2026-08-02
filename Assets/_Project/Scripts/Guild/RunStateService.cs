using System;
using System.Collections.Generic;
using Guildmaster.Core.Persistence;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Владелец durable-состояния забега (<see cref="RunState"/>) и единая точка его сейва/загрузки и правил
    /// вместимости коллекции реликов (план 11 §5.4). Три точки автосейва (вики «7» §5) зовут <see cref="Autosave"/>
    /// на переходах флоу.
    /// </summary>
    /// <remarks>
    /// <b>Живёт в скоупе Сессии и только у владельца сейва.</b> Гость играет в чужом состоянии — этого
    /// сервиса у него нет вовсе, поэтому «случайно записать чужой забег» ему нечем (см.
    /// <c>SessionInstaller</c>). Тем, кто переживает сеанс, видна только читающая половина —
    /// <see cref="IRunStateView"/> через роутер.
    /// </remarks>
    public sealed class RunStateService : IRunStateView
    {
        private readonly ISaveService    _save;
        private readonly GameConfig      _config;
        private readonly IProfileService _profiles;
        // Звук награды за бой. Опционален: сервис создают и в тестах, где звука нет вовсе.
        private readonly Core.Audio.IAudioService _audio;

        public RunState Current { get; private set; }

        public RunStateService(ISaveService save, GameConfig config, IProfileService profiles,
            Core.Audio.IAudioService audio = null)
        {
            _save     = save;
            _config   = config;
            _profiles = profiles;
            _audio    = audio;
        }

        /// <summary>
        /// Куда пишется забег: у каждой гильдии свой файл, потому что гильдия и есть слот сохранения
        /// (ТЗ [[save-system]] §3). Пустая строка = активной гильдии нет, писать некуда.
        /// </summary>
        private string SaveKey => _profiles.RunKey;

        /// <summary>
        /// Есть ли автосейв забега на диске. Тот же вопрос задаёт главное меню, но ему спрашивать
        /// некого — оно живёт вне сессии, — поэтому знание вынесено в <see cref="RunSaves"/>, а здесь
        /// осталась удобная дорога к нему.
        /// </summary>
        public bool HasSave
        {
            get
            {
                return RunSaves.Exists(_save, _profiles);
            }
        }

        /// <summary>Начать новый забег: свежий <see cref="RunState"/> с базовой вместимостью реликов из конфига.</summary>
        public RunState NewRun(long seed, RosterSlot[] guild)
        {
            guild ??= Array.Empty<RosterSlot>();
            Current = new RunState
            {
                Seed          = seed,
                Gold          = _config.StartGold,
                RelicCapacity = _config.RelicCapacityBase,
                Guild         = guild,
                SlotOwner     = new int[guild.Length], // соло: все 0
            };
            return Current;
        }

        /// <summary>
        /// Начать новый забег со СТАНДАРТНОЙ стартовой гильдией (уточн. Макса 2026-07-17): гильдия — это
        /// <see cref="GameConfig.GuildSize"/> одинаковых сосудов, у каждого базовый релик (пустой кит);
        /// прогрессия — реликвии, которые игрок навешивает на них в лоадауте. Сосуд-контента пока нет →
        /// <c>VesselId</c> пуст. Стартовые позиции — колонка на стороне team 0 (Free-расстановка перед боем
        /// позволяет переставить). Незаполненные поля конфига подставляются, но с красной ошибкой: владелец
        /// экономики — ассет, и тихая подстановка прятала бы то, что ассет не заполнен.
        /// </summary>
        public RunState NewDefaultRun(long seed)
        {
            // Владелец обоих значений — ассет GameConfig (HARD-правило проекта). Пустое поле здесь не
            // «дефолт», а незаполненный ассет: подставляем, чтобы забег вообще стартовал, но говорим об
            // этом — тихая подстановка делала бы правку ассета невидимой (аудит фолбэков 2026-07-26).
            int size = _config.GuildSize;
            if (size <= 0)
            {
                UnityEngine.Debug.LogError($"[RunStateService] - GameConfig.GuildSize = {size}: беру 4, но это незаполненный ассет");
                size = 4;
            }

            string relicId = _config.StartingRelicId;
            if (string.IsNullOrEmpty(relicId))
            {
                UnityEngine.Debug.LogError($"[RunStateService] - GameConfig.StartingRelicId пуст: беру '{ContentIds.BaseRelic}', но это незаполненный ассет");
                relicId = ContentIds.BaseRelic;
            }

            var guild = new RosterSlot[size];
            float top = (size - 1) * 0.5f; // центрируем колонку по вертикали
            for (int i = 0; i < size; i++)
            {
                guild[i] = new RosterSlot
                {
                    VesselId      = string.Empty,
                    RelicId       = relicId,
                    SavedPosition = new UnityEngine.Vector2(-6f, (top - i) * 1.5f),
                };
            }
            return NewRun(seed, guild);
        }

        /// <summary>
        /// Загрузить забег из автосейва. Возвращает <b>исход</b>, а не голое значение: «сейва нет» и
        /// «сейв из более новой версии игры» требуют разного ответа игроку, а <see cref="Current"/>
        /// подменяется только при успехе — иначе первый же автосейв затёр бы чужой прогресс.
        /// </summary>
        public SaveLoadResult<RunState> TryLoad()
        {
            string key = SaveKey;
            if (string.IsNullOrEmpty(key)) return SaveLoadResult<RunState>.Missing();

            SaveLoadResult<RunState> result = _save.TryLoad<RunState>(key);
            if (result.IsOk) Current = result.Value;
            return result;
        }

        /// <summary>
        /// Сгенерировать карту текущего акта, если её ещё нет (план [[act-map-run-loop]] §3.1). Детерминирована
        /// под-сидом <c>Seed + CurrentActIndex</c> — перезаход в тот же акт даёт ту же карту; при загрузке из
        /// автосейва карта уже есть, повторно не генерим. No-op без активного забега.
        /// </summary>
        public void BeginAct(MapGenConfig mapConfig = null)
        {
            if (Current == null) return;
            if (Current.Map != null && Current.Map.Nodes.Length > 0) return; // уже сгенерирована/загружена

            Current.RestartsRemaining = _config.RestartsPerAct; // пул перезапусков на акт (реш. №65)

            var rng = new XorShiftRng(unchecked((ulong)(Current.Seed + Current.CurrentActIndex)));
            Current.Map = MapGenerator.Generate(rng, mapConfig ?? new MapGenConfig());
        }

        /// <summary>
        /// Изменить золото забега (±). Клампится в ноль снизу. No-op без активного забега.
        /// <para><b>internal:</b> снаружи сборки золото меняют через <c>IRunCommands.AddGold</c> — запись в
        /// забег обязана попасть в лог команд, иначе реконнект и аудит увидят состояние без причины.</para>
        /// </summary>
        internal void AddGold(int delta)
        {
            if (Current == null) return;
            Current.Gold = System.Math.Max(0, Current.Gold + delta);
        }

        /// <summary>Текущее золото забега (0 без активного забега).</summary>
        public int Gold => Current?.Gold ?? 0;

        /// <summary>
        /// Списать золото, если хватает. false = недостаточно (ничего не списано) или нет забега.
        /// </summary>
        /// <remarks>
        /// <b>Осталось публичным сознательно — это транзакция, а не односторонняя запись.</b> Метод
        /// отвечает «вышло или нет» СРАЗУ, а в коопе ответ даёт хост, и вызывающий обязан уметь ждать и
        /// откатывать оптимистичный показ. Завернуть это в шину, не переделав магазин, награды и лоадаут,
        /// нельзя — а их переделка и есть отложенный шаг «транзакции над экранами подготовки»
        /// ([[tech/40-planning/coop-vertical|Planning - Coop Vertical]] §10). Пока сюда ходят напрямую, и
        /// в логе команд этих изменений НЕТ: в коопе такой путь даст расхождение, поэтому первым делом
        /// перед транзакциями сюда и возвращаемся. Товарищи по той же причине:
        /// <see cref="TryAddRelic"/>, <see cref="TrySpendRestart"/>, <see cref="IncreaseCapacity"/>.
        /// </remarks>
        public bool TrySpendGold(int amount)
        {
            if (Current == null || amount < 0 || Current.Gold < amount) return false;
            Current.Gold -= amount;
            return true;
        }

        /// <summary>
        /// Начислить награду золотом за победу в бою (из <see cref="GameConfig"/>).
        /// <para><b>internal:</b> см. <see cref="AddGold"/> — снаружи через <c>IRunCommands</c>.</para>
        /// </summary>
        internal void AwardBattleReward()
        {
            AddGold(_config.BattleGoldReward);
            _audio?.Play("run.gold_gain.ui"); // звенят только НАГРАДНЫЕ монеты: у продажи в лавке свой звук
        }

        // ── Перезапуски боя на акт (реш. №65) ────────────────────────────────

        /// <summary>Оставшиеся перезапуски боя в текущем акте.</summary>
        public int RestartsRemaining => Current?.RestartsRemaining ?? 0;

        /// <summary>
        /// Потратить один перезапуск, если есть. false = пул пуст (поражение = конец забега).
        /// <para>Публичный по той же причине, что <see cref="TrySpendGold"/>.</para>
        /// </summary>
        public bool TrySpendRestart()
        {
            if (Current == null || Current.RestartsRemaining <= 0) return false;
            Current.RestartsRemaining--;
            return true;
        }

        /// <summary>Снапшот текущего забега на диск (точка автосейва). No-op без активного забега.</summary>
        public void Autosave()
        {
            if (Current == null) return;

            string key = SaveKey;
            if (string.IsNullOrEmpty(key))
            {
                // Активной гильдии нет — забег писать некуда. Молчать нельзя: игрок продолжал бы играть,
                // веря, что прогресс сохраняется, и потерял бы его целиком на выходе.
                UnityEngine.Debug.LogError("[RunStateService] - нет активной гильдии: забег НЕ сохранён");
                return;
            }

            _save.Save(key, Current);
        }

        /// <summary>Удалить автосейв (конец/сброс забега).</summary>
        public void DeleteSave()
        {
            string key = SaveKey;
            if (!string.IsNullOrEmpty(key)) _save.Delete(key);
        }

        // ── Вместимость коллекции реликов (план 11 §5.4) ─────────────────────

        /// <summary>Полон ли запас реликов (нельзя взять награду без сброса).</summary>
        public bool RelicInventoryFull =>
            Current != null && Current.RelicInventory.Length >= Current.RelicCapacity;

        /// <summary>
        /// Добавить релик в запас, если есть место. false = полно (игрок должен сбросить/пропустить).
        /// <para>Публичный по той же причине, что <see cref="TrySpendGold"/>: транзакция с синхронным
        /// ответом, её переезд в шину — отложенный шаг транзакций.</para>
        /// </summary>
        public bool TryAddRelic(string relicId)
        {
            if (Current == null || string.IsNullOrEmpty(relicId) || RelicInventoryFull) return false;
            var list = new List<string>(Current.RelicInventory) { relicId };
            Current.RelicInventory = list.ToArray();
            return true;
        }

        /// <summary>
        /// Убрать один экземпляр релика из запаса (сброс ради места под награду).
        /// <para><b>internal:</b> снаружи через <c>IRunCommands.RemoveRelic</c>.</para>
        /// </summary>
        internal void RemoveRelic(string relicId)
        {
            if (Current == null) return;
            var list = new List<string>(Current.RelicInventory);
            if (list.Remove(relicId)) Current.RelicInventory = list.ToArray();
        }

        /// <summary>
        /// Увеличить вместимость на 1 (товар магазина/награда), до потолка. false = уже потолок.
        /// <para>Публичный по той же причине, что <see cref="TrySpendGold"/>.</para>
        /// </summary>
        public bool IncreaseCapacity()
        {
            if (Current == null || Current.RelicCapacity >= _config.RelicCapacityMax) return false;
            Current.RelicCapacity++;
            return true;
        }

        // ── Лоадаут: надеть/снять релик на сосуд гильдии (кольцо реликвий, Фаза 2) ──

        /// <summary>Id «пустого» кита (базовый релик). Из конфига, дефолт <c>relic.base</c>.</summary>
        private string BaseRelicId => string.IsNullOrEmpty(_config.StartingRelicId) ? ContentIds.BaseRelic : _config.StartingRelicId;

        /// <summary>
        /// Надеть релик из запаса на сосуд слота (лоадаут-хаб): релик снимается с запаса и встаёт на слот, а
        /// прежний кит слота (если не базовый) возвращается в запас — это свап, не потеря. Требует, чтобы
        /// <paramref name="relicId"/> лежал в запасе. false = нет забега / слот вне ростера / релика нет в запасе.
        /// </summary>
        public bool EquipRelic(int slotIndex, string relicId)
        {
            RosterSlot slot = SlotAt(slotIndex);
            if (slot == null || string.IsNullOrEmpty(relicId)) return false;

            var inv = new List<string>(Current.RelicInventory);
            if (!inv.Remove(relicId)) return false; // релик должен лежать в запасе

            string prev = slot.RelicId;
            slot.RelicId = relicId;
            if (!string.IsNullOrEmpty(prev) && prev != BaseRelicId)
                inv.Add(prev); // прежний кит уходит обратно в запас (свап)

            Current.RelicInventory = inv.ToArray();
            return true;
        }

        /// <summary>
        /// Снять релик со слота обратно в запас; слот возвращается к базовому киту. No-op, если на слоте уже
        /// базовый кит. false также при полном запасе (некуда вернуть — релик не теряем).
        /// </summary>
        public bool UnequipRelic(int slotIndex)
        {
            RosterSlot slot = SlotAt(slotIndex);
            if (slot == null) return false;

            string cur = slot.RelicId;
            if (string.IsNullOrEmpty(cur) || cur == BaseRelicId) return false; // уже пусто
            if (RelicInventoryFull) return false;                              // некуда вернуть

            var inv = new List<string>(Current.RelicInventory) { cur };
            Current.RelicInventory = inv.ToArray();
            slot.RelicId = BaseRelicId;
            return true;
        }

        // ── Расстановка: позиция и кит слота прямо с арены (фаза Deployment) ──
        // Отдельно от EquipRelic/UnequipRelic: там лоадаут-хаб гоняет реликвии ЧЕРЕЗ запас (свап со списанием),
        // а здесь игрок правит отряд руками на поле — источник кита не запас, а то, что он притащил из грида.
        // Когда инвентарь начнёт показывать реальный запас забега (сейчас — весь контент), эти два пути стоит
        // свести в один, и тогда SetSlotRelic уйдёт.

        /// <summary>
        /// Запомнить позицию сосуда на арене (перетаскивание в расстановке). false = слот вне ростера.
        /// <para><b>internal:</b> снаружи через <c>IRunCommands.SetSlotPosition</c> — в коопе расстановку
        /// правят двое, и «кто передвинул» обязано остаться в логе.</para>
        /// </summary>
        internal bool SetSlotPosition(int slotIndex, UnityEngine.Vector2 position)
        {
            RosterSlot slot = SlotAt(slotIndex);
            if (slot == null) return false;
            slot.SavedPosition = position;
            return true;
        }

        /// <summary>
        /// Поставить кит на сосуд НАПРЯМУЮ, минуя запас (drag реликвии на юнита в расстановке): запас не
        /// трогается, прежний кит не возвращается. false = слот вне ростера / пустой id.
        /// <para><b>internal:</b> снаружи через <c>IRunCommands.SetSlotRelic</c>.</para>
        /// </summary>
        internal bool SetSlotRelic(int slotIndex, string relicId)
        {
            RosterSlot slot = SlotAt(slotIndex);
            if (slot == null || string.IsNullOrEmpty(relicId)) return false;
            slot.RelicId = relicId;
            return true;
        }

        // ── Предметы сосуда (Vessel-скоуп, лимит GameConfig.VesselItemSlots) ──

        /// <summary>Сколько предметов помещается на одного сосуда.</summary>
        public int MaxVesselItems => _config.VesselItemSlots;

        /// <summary>Предметы, надетые на сосуд слота (пустой массив, если слот вне ростера).</summary>
        public IReadOnlyList<string> VesselItems(int slotIndex)
        {
            RosterSlot slot = SlotAt(slotIndex);
            return slot != null ? slot.VesselItemIds : Array.Empty<string>();
        }

        /// <summary>Надеть предмет на сосуд, если есть свободный слот. false = слот полон или индекс невалиден.</summary>
        public bool TryAddVesselItem(int slotIndex, string itemId)
        {
            RosterSlot slot = SlotAt(slotIndex);
            if (slot == null || string.IsNullOrEmpty(itemId)) return false;
            if (slot.VesselItemIds.Length >= _config.VesselItemSlots) return false;

            var list = new List<string>(slot.VesselItemIds) { itemId };
            slot.VesselItemIds = list.ToArray();
            return true;
        }

        /// <summary>Снять один экземпляр предмета с сосуда слота.</summary>
        public void RemoveVesselItem(int slotIndex, string itemId)
        {
            RosterSlot slot = SlotAt(slotIndex);
            if (slot == null) return;
            var list = new List<string>(slot.VesselItemIds);
            if (list.Remove(itemId)) slot.VesselItemIds = list.ToArray();
        }

        // ── Баннеры отряда (Party-скоуп, лимит GameConfig.PartyBannerSlots) ──

        /// <summary>Сколько баннеров можно держать активными на весь отряд.</summary>
        public int MaxPartyBanners => _config.PartyBannerSlots;

        /// <summary>Активные баннеры отряда.</summary>
        public IReadOnlyList<string> Banners =>
            Current != null ? Current.PartyItemIds : Array.Empty<string>();

        /// <summary>Взять баннер, если есть свободный слот. false = слотов нет или нет забега.</summary>
        public bool TryAddBanner(string bannerId)
        {
            if (Current == null || string.IsNullOrEmpty(bannerId)) return false;
            if (Current.PartyItemIds.Length >= _config.PartyBannerSlots) return false;

            var list = new List<string>(Current.PartyItemIds) { bannerId };
            Current.PartyItemIds = list.ToArray();
            return true;
        }

        /// <summary>Убрать баннер из активных.</summary>
        public void RemoveBanner(string bannerId)
        {
            if (Current == null) return;
            var list = new List<string>(Current.PartyItemIds);
            if (list.Remove(bannerId)) Current.PartyItemIds = list.ToArray();
        }

        private RosterSlot SlotAt(int slotIndex)
        {
            if (Current?.Guild == null || slotIndex < 0 || slotIndex >= Current.Guild.Length) return null;
            return Current.Guild[slotIndex];
        }
    }
}
