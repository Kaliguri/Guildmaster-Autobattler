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
    public sealed class RunStateService : ISessionRunState
    {
        private readonly ISaveService    _save;
        private readonly GameConfig      _config;
        private readonly IProfileService _profiles;

        // Каталог контента: по нему резолвятся последствия боёв (ступень раны, срок истечения, пул для
        // ролла). Обязателен, а не опционален: без каталога травма не легла бы молча, и забег пошёл бы
        // дальше без цены за смерть — то есть игра стала бы другой, ничего не сообщив.
        private readonly IContentDatabase _content;

        // Звук награды за бой. Опционален: сервис создают и в тестах, где звука нет вовсе.
        private readonly Core.Audio.IAudioService _audio;

        public RunState Current { get; private set; }

        /// <summary>
        /// Состояние забега целое и зафиксировано: начат новый забег, загружен сохранённый или сделан
        /// автосейв. Не «что-то поменялось» — именно те точки, на которых состояние считается готовым к
        /// отправке наружу.
        /// </summary>
        /// <remarks>
        /// Существует ради коопа: гость играет в присланном состоянии, и точки его обновления обязаны
        /// совпадать с точками сохранения — гость получает ровно то же, что диск. Разъедься они, и
        /// «у меня не так, как у него» отличалось бы от «у меня не так, как в сейве».
        /// </remarks>
        public event Action<RunState> Committed;

        public RunStateService(ISaveService save, GameConfig config, IProfileService profiles,
            IContentDatabase content, Core.Audio.IAudioService audio = null)
        {
            _save     = save;
            _config   = config;
            _profiles = profiles;
            _content  = content;
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
            };
            Committed?.Invoke(Current);
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

            // В бой выходят не все: мест в отряде до восьми, на арену идут четверо
            // (ГДД preparation-screens §2.1). Позиции центруются по БОЕВЫМ, а не по всему отряду —
            // иначе колонка старта уезжала бы вверх от числа запасных, которых на арене нет.
            int battle = _config.BattleSlots;
            if (battle <= 0)
            {
                UnityEngine.Debug.LogError($"[RunStateService] - GameConfig.BattleSlots = {battle}: беру 4, но это незаполненный ассет");
                battle = 4;
            }
            if (battle > size) battle = size;

            var guild = new RosterSlot[size];
            float top = (battle - 1) * 0.5f;
            for (int i = 0; i < size; i++)
            {
                guild[i] = new RosterSlot
                {
                    VesselId      = string.Empty,
                    RelicId       = relicId,
                    InBattle      = i < battle,
                    SavedPosition = new UnityEngine.Vector2(-6f, (top - i) * 1.5f),
                };
            }

            RunState run = NewRun(seed, guild);
            run.OpenSlots = ResolveOpenSlots(_config.GuildSlotsOpenAtStart, size);
            return run;
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
            if (result.IsOk)
            {
                Normalize(result.Value, _config);
                Current = result.Value;
                Committed?.Invoke(Current);
            }
            return result;
        }

        /// <summary>
        /// Дочитать забег, сохранённый до того, как отряд вырос с четырёх мест до восьми. Версия схемы
        /// ради этого НЕ поднимается (решение 2026-08-22): и недостающие места, и признак «в бою»
        /// выводятся из того, что в файле уже есть, — миграция писала бы то же самое дороже.
        /// <list type="bullet">
        /// <item>мест меньше потолка — массив дописывается пустыми слотами с базовым китом;</item>
        /// <item>никто не помечен «в бою» — боевыми становятся первые <c>BattleSlots</c> занятых мест;</item>
        /// <item><see cref="RunState.OpenSlots"/> нулевой — берётся база из конфига.</item>
        /// </list>
        /// Идемпотентна: свежий забег проходит через неё без изменений.
        /// </summary>
        public static void Normalize(RunState run, GameConfig config)
        {
            if (run == null || config == null) return;

            int size = config.GuildSize > 0 ? config.GuildSize : 4;
            int battle = config.BattleSlots > 0 ? config.BattleSlots : 4;
            if (battle > size) battle = size;

            RosterSlot[] guild = run.Guild ?? System.Array.Empty<RosterSlot>();
            if (guild.Length < size)
            {
                var grown = new RosterSlot[size];
                for (int i = 0; i < size; i++)
                    grown[i] = i < guild.Length && guild[i] != null
                        ? guild[i]
                        : new RosterSlot { RelicId = ContentIds.BaseRelic };
                guild = grown;
                run.Guild = guild;
            }

            bool anyInBattle = false;
            for (int i = 0; i < guild.Length; i++)
                if (guild[i] != null && guild[i].InBattle) { anyInBattle = true; break; }

            if (!anyInBattle)
            {
                // Старый сейв: в бой шёл весь ростер, поэтому «кто выходит» восстанавливаем по порядку —
                // первые занятые места. Пустые пропускаются: иначе четвёрка боя набралась бы дырами.
                int taken = 0;
                for (int i = 0; i < guild.Length && taken < battle; i++)
                {
                    if (guild[i] == null) continue;
                    guild[i].InBattle = true;
                    taken++;
                }
            }

            if (run.OpenSlots <= 0)
                run.OpenSlots = ResolveOpenSlots(config.GuildSlotsOpenAtStart, size);
        }

        /// <summary>
        /// Сколько мест отряда открыто: база из конфига, зажатая потолком. Пустое поле ассета — не
        /// «дефолт», а незаполненный ассет, поэтому подстановка кричит в лог (политика фолбэков).
        /// </summary>
        private static int ResolveOpenSlots(int fromConfig, int size)
        {
            if (fromConfig <= 0)
            {
                UnityEngine.Debug.LogError(
                    $"[RunStateService] - GameConfig.GuildSlotsOpenAtStart = {fromConfig}: беру {size}, но это незаполненный ассет");
                return size;
            }
            return fromConfig > size ? size : fromConfig;
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
            Committed?.Invoke(Current);
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
        /// Записать, что игроки входят в этот узел. Петля акта ждёт именно этой записи.
        /// <para><b>internal:</b> снаружи через <c>IRunCommands.ChooseNode</c> — как и всё, что меняет
        /// забег. Достижимость проверил применитель команды, здесь только запись.</para>
        /// </summary>
        internal void EnterNode(string nodeId)
        {
            if (Current?.Map == null) return;
            Current.Map.EnteringNodeId = nodeId;
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

        /// <summary>
        /// Вывести «Сосуда» на арену или увести в запас. Больше <c>GameConfig.BattleSlots</c> бойцов в
        /// бой не пускает, пустое место — тоже: выводить в бой некого.
        /// <para><b>internal:</b> снаружи через <c>IRunCommands.SetSlotInBattle</c>.</para>
        /// </summary>
        internal bool SetSlotInBattle(int slotIndex, bool inBattle)
        {
            RosterSlot slot = SlotAt(slotIndex);
            if (slot == null) return false;
            if (slot.InBattle == inBattle) return false;

            if (inBattle)
            {
                if (string.IsNullOrEmpty(slot.VesselId)) return false; // некого выводить
                if (slotIndex >= OpenSlotCount) return false;          // место ещё не открыто
                if (CountInBattle() >= BattleSlotCount) return false;  // на арене больше не помещается
            }

            slot.InBattle = inBattle;
            return true;
        }

        /// <summary>
        /// Поменять местами два места отряда — сортировка ленты, не смена состава. Слоты меняются
        /// целиком, поэтому «в бою» едет ВМЕСТЕ с человеком: признак принадлежит бойцу, а не позиции.
        /// <para>Иначе один и тот же жест значил бы две вещи разом — и порядок, и состав арены, — а
        /// ради устранения этой двусмысленности признак и сделали полем вместо позиции в массиве
        /// (журнал 2026-08-22 «The Battle Four Gets A Flag»). Вывод в бой — свой жест.</para>
        /// <para><b>internal:</b> снаружи через <c>IRunCommands.SwapSlots</c>.</para>
        /// </summary>
        internal bool SwapSlots(int a, int b)
        {
            if (a == b) return false;
            RosterSlot first = SlotAt(a);
            RosterSlot second = SlotAt(b);
            if (first == null || second == null) return false;
            if (a >= OpenSlotCount || b >= OpenSlotCount) return false; // закрытое место не участвует

            Current.Guild[a] = second;
            Current.Guild[b] = first;
            return true;
        }

        /// <summary>
        /// Положить вещь в слот «Сосуда» или снять её (пустой <paramref name="itemId"/>). Снятое уходит
        /// в склад забега, надетое уходит оттуда — вещь ни в один момент не существует в двух местах и
        /// не исчезает вовсе.
        /// <para>Надеть можно только то, что лежит в складе: иначе UI, отставший на снимок, наплодил бы
        /// копии предмета в коопе.</para>
        /// <para><b>internal:</b> снаружи через <c>IRunCommands.SetSlotItem</c>.</para>
        /// </summary>
        internal bool SetSlotItem(int slotIndex, int itemSlot, string itemId)
        {
            RosterSlot slot = SlotAt(slotIndex);
            if (slot == null) return false;
            if (itemSlot < 0 || itemSlot >= _config.VesselItemSlots) return false;

            string[] worn = slot.VesselItemIds ?? System.Array.Empty<string>();
            if (worn.Length < _config.VesselItemSlots)
            {
                var grown = new string[_config.VesselItemSlots];
                for (int i = 0; i < grown.Length; i++)
                    grown[i] = i < worn.Length ? worn[i] : string.Empty;
                worn = grown;
                slot.VesselItemIds = worn;
            }

            string previous = worn[itemSlot] ?? string.Empty;
            bool equipping = !string.IsNullOrEmpty(itemId);
            if (!equipping && string.IsNullOrEmpty(previous)) return false; // снимать нечего

            var stash = new List<string>(Current.ItemInventory ?? System.Array.Empty<string>());
            if (equipping)
            {
                if (!stash.Remove(itemId)) return false; // в складе такой вещи нет
            }
            if (!string.IsNullOrEmpty(previous)) stash.Add(previous);

            worn[itemSlot] = equipping ? itemId : string.Empty;
            Current.ItemInventory = stash.ToArray();
            return true;
        }

        /// <summary>Сколько мест отряда открыто сейчас. Ноль в состоянии читается как «все».</summary>
        private int OpenSlotCount
        {
            get
            {
                if (Current == null) return 0;
                int open = Current.OpenSlots;
                int size = Current.Guild?.Length ?? 0;
                if (open <= 0 || open > size) return size;
                return open;
            }
        }

        /// <summary>Сколько «Сосудов» помещается на арену. Пустое поле ассета — не дефолт, а ошибка.</summary>
        private int BattleSlotCount
        {
            get
            {
                int battle = _config.BattleSlots;
                if (battle > 0) return battle;
                UnityEngine.Debug.LogError($"[RunStateService] - GameConfig.BattleSlots = {battle}: беру 4, но это незаполненный ассет");
                return 4;
            }
        }

        /// <summary>Сколько «Сосудов» сейчас помечено «в бою».</summary>
        private int CountInBattle()
        {
            RosterSlot[] guild = Current?.Guild;
            if (guild == null) return 0;
            int n = 0;
            for (int i = 0; i < guild.Length; i++)
                if (guild[i] != null && guild[i].InBattle) n++;
            return n;
        }

        // ── Последствия боёв (травмы и закалка, ГДД injuries-mettle) ──

        /// <summary>
        /// Положить последствие на «Сосуд» слота: ступень уточняет каскад, конкретную рану выбирает
        /// ролл от <paramref name="rollSeed"/>. Возвращает исход каскада — по нему вызывающий узнаёт,
        /// поднялась ли ступень и не выбыл ли «Сосуд» из забега.
        /// <para><b>internal:</b> снаружи через <c>IRunCommands.InflictInjury</c> — как и всё, что
        /// меняет забег.</para>
        /// </summary>
        internal InjuryOutcome InflictInjury(int slotIndex, ulong rollSeed)
        {
            RosterSlot slot = SlotAt(slotIndex);
            if (slot == null) return default;

            return InjuryLedger.Inflict(slot, InjuryGrade.Bruise, _content, rollSeed);
        }

        /// <summary>
        /// Узел маршрута пройден: состарить раны и снять те, что проходят сами. Возвращает, сколько
        /// снялось.
        /// </summary>
        /// <remarks>
        /// Зовётся ровно там, где узел помечается пройденным, — иначе «сколько узлов прожила рана» и
        /// «сколько узлов прошёл отряд» разъедутся, а расхождения этого не увидит никто: рана просто
        /// проживёт не столько, сколько обещано в карточке.
        /// </remarks>
        /// <para><b>public, в отличие от прочих мутаторов:</b> это не действие игрока, а следствие уже
        /// совершённого хода — как и <see cref="Autosave"/>. Зовёт его тот, кто двигает карту.</para>
        public int AdvanceInjuries()
            => Current == null ? 0 : InjuryLedger.AdvanceNode(Current, _content);

        /// <summary>
        /// Снять одно последствие с «Сосуда» (торговец, привал). <paramref name="payGold"/> — списать ли
        /// цену из ассета. <c>false</c> = такого последствия нет, оно бесплатным не лечится или не
        /// хватило золота — во всех трёх случаях состояние остаётся нетронутым.
        /// <para><b>internal:</b> снаружи через <c>IRunCommands.HealInjury</c>.</para>
        /// </summary>
        /// <remarks>
        /// Оплата и снятие живут в ОДНОМ методе, а не двумя вызовами подряд: разнеси их — и появится
        /// состояние «золото списано, рана на месте», в которое попадёт любой отказ между ними.
        /// </remarks>
        internal bool HealInjury(int slotIndex, string consequenceId, bool payGold)
        {
            RosterSlot slot = SlotAt(slotIndex);
            if (slot == null || string.IsNullOrEmpty(consequenceId)) return false;
            if (!HasInjury(slot, consequenceId)) return false;

            if (payGold)
            {
                int cost = HealCost(consequenceId);
                if (cost > 0 && !TrySpendGold(cost)) return false;
            }

            return InjuryLedger.Remove(slot, consequenceId);
        }

        /// <summary>Во сколько золота обходится снятие последствия у торговца. 0 = каталог не знает такого.</summary>
        public int HealCost(string consequenceId) =>
            _content != null && _content.TryGet(consequenceId, out ConsequenceData def) ? def.HealCostGold : 0;

        private static bool HasInjury(RosterSlot slot, string consequenceId)
        {
            if (slot?.Injuries == null) return false;
            for (int i = 0; i < slot.Injuries.Length; i++)
                if (slot.Injuries[i]?.Id == consequenceId) return true;
            return false;
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

        // ── Предметы отряда (Party-скоуп) ──
        // Лимита слотов нет намеренно: Party Item копится весь забег, как реликвии в Slay the Spire
        // (решение 2026-08-21/2, тем же решением сняты Знамёна и поле GameConfig.PartyBannerSlots).

        /// <summary>Предметы отряда, набранные за забег. Порядок — в каком брали.</summary>
        public IReadOnlyList<string> PartyItems =>
            Current != null ? Current.PartyItemIds : Array.Empty<string>();

        /// <summary>Взять предмет отряда. false = нет забега или пустой id; лимита слотов нет.</summary>
        public bool AddPartyItem(string itemId)
        {
            if (Current == null || string.IsNullOrEmpty(itemId)) return false;

            var list = new List<string>(Current.PartyItemIds) { itemId };
            Current.PartyItemIds = list.ToArray();
            return true;
        }

        /// <summary>Убрать предмет отряда.</summary>
        public void RemovePartyItem(string itemId)
        {
            if (Current == null) return;
            var list = new List<string>(Current.PartyItemIds);
            if (list.Remove(itemId)) Current.PartyItemIds = list.ToArray();
        }

        private RosterSlot SlotAt(int slotIndex)
        {
            if (Current?.Guild == null || slotIndex < 0 || slotIndex >= Current.Guild.Length) return null;
            return Current.Guild[slotIndex];
        }
    }
}
