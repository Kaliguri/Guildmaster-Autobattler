using System.Collections.Generic;

namespace Guildmaster.Combat.Tape
{
    /// <summary>
    /// Лента боя: сим пишет в неё вперёд, показ читает из неё с задержкой. Держит два разных по
    /// природе потока (вики: «сим впереди, показ с лагом»):
    /// <list type="bullet">
    /// <item><b>Снимки состояния</b> — кольцевое ОКНО. Нужны только вокруг текущего момента показа
    /// (и чуть впереди — для подводок камеры), поэтому хранить весь бой незачем.</item>
    /// <item><b>События</b> — за ВЕСЬ бой. Они дёшевы, и именно они дают режиссуру: знать за секунды,
    /// что будет клатч, что цель сейчас умрёт, что «Оплот» вот-вот сработает.</item>
    /// </list>
    /// <para>Отсюда следствие, снимающее ложную развилку «бежать чуть впереди или просчитать целиком»:
    /// сим может уехать хоть до конца боя — окно снимков просто едет вместе с показом.</para>
    /// <para><b>Аллокации:</b> кадры окна выделяются один раз и переиспользуются (<c>Clear</c> +
    /// <c>Add</c> в пределах ёмкости), поэтому запись тика не мусорит. Списки событий растут по ходу
    /// боя — это редкие вызовы, а не тик.</para>
    /// </summary>
    public sealed class BattleTape
    {
        /// <summary>Один тик состояния. Класс, а не структура: живёт в кольце и переиспользуется.</summary>
        private sealed class Frame
        {
            public int Tick = NoTick;
            public readonly List<UnitSnapshot>       Units       = new List<UnitSnapshot>(InitialUnitCapacity);
            public readonly List<ProjectileSnapshot> Projectiles = new List<ProjectileSnapshot>(InitialUnitCapacity);
        }

        /// <summary>«Тика нет» — для пустого кадра и незаписанной ленты.</summary>
        public const int NoTick = -1;

        private const int InitialUnitCapacity = 32;

        private readonly Frame[]            _ring;
        private readonly List<TapeEvent>    _events   = new List<TapeEvent>(256);
        private readonly List<TapeDamage>   _damage   = new List<TapeDamage>(256);
        private readonly List<AreaHit>      _areaHits = new List<AreaHit>(64);
        private readonly List<BattleOutcome> _outcomes = new List<BattleOutcome>(2);

        // Определения эффектов — ССЫЛКИ на ассеты, а не состояние: они неизменны, тащить их копией незачем.
        private readonly List<Data.Definitions.EffectData> _effectDefs = new List<Data.Definitions.EffectData>(64);

        // Определения способностей — по той же причине ссылками. Показу нужен не только факт каста, но и
        // чем он исполнен (CastSource): без определения телеграф светит всем одним и тем же.
        private readonly List<Data.Definitions.AbilityData> _abilityDefs = new List<Data.Definitions.AbilityData>(64);

        private int _frontTick = NoTick;

        /// <param name="windowTicks">Глубина окна снимков в тиках: опережение плюс запас на подводки.</param>
        public BattleTape(int windowTicks)
        {
            WindowTicks = windowTicks > 0 ? windowTicks : 1;
            _ring = new Frame[WindowTicks];
            for (int i = 0; i < _ring.Length; i++) _ring[i] = new Frame();
        }

        /// <summary>Глубина окна снимков в тиках. Тик старше окна из ленты уже не достать.</summary>
        public int WindowTicks { get; }

        /// <summary>Последний записанный тик — фронт симуляции. <see cref="NoTick"/> = лента пуста.</summary>
        public int FrontTick => _frontTick;

        /// <summary>Самый старый тик, ещё лежащий в окне.</summary>
        public int OldestTick => _frontTick == NoTick ? NoTick : System.Math.Max(0, _frontTick - WindowTicks + 1);

        /// <summary>Сколько событий записано за бой (события не выбрасываются, в отличие от снимков).</summary>
        public int EventCount => _events.Count;

        public TapeEvent GetEvent(int index) => _events[index];

        public DamageResult GetDamage(int payloadIndex) => _damage[payloadIndex].Result;

        public AreaHit GetAreaHit(int payloadIndex) => _areaHits[payloadIndex];

        /// <summary>Исход боя, на который ссылается <see cref="TapeEventKind.BattleEnded"/>.</summary>
        public BattleOutcome GetOutcome(int payloadIndex) => _outcomes[payloadIndex];

        /// <summary>Определение эффекта для <c>EffectApplied</c> / <c>EffectEnded</c>.</summary>
        public Data.Definitions.EffectData GetEffect(int payloadIndex) => _effectDefs[payloadIndex];

        /// <summary>
        /// Определение способности для <c>AbilityCast</c> / <c>AbilityCastStarted</c>, или <c>null</c>,
        /// если событие записано без него (<paramref name="payloadIndex"/> = -1).
        /// </summary>
        public Data.Definitions.AbilityData GetAbility(int payloadIndex) =>
            payloadIndex >= 0 && payloadIndex < _abilityDefs.Count ? _abilityDefs[payloadIndex] : null;

        /// <summary>
        /// Записать состояние тика. Зовётся ровно раз за тик, после того как тик досчитан — иначе в
        /// кадр попадёт полусобранное состояние.
        /// </summary>
        public void CaptureTick(
            int tick, IReadOnlyList<RuntimeUnit> units, IReadOnlyList<Projectile> projectiles = null)
        {
            Frame frame = _ring[Slot(tick)];
            frame.Tick = tick;
            frame.Units.Clear();
            for (int i = 0; i < units.Count; i++) frame.Units.Add(UnitSnapshot.From(units[i]));

            frame.Projectiles.Clear();
            if (projectiles != null)
                for (int i = 0; i < projectiles.Count; i++)
                    if (projectiles[i].IsAlive) frame.Projectiles.Add(ProjectileSnapshot.From(projectiles[i]));

            if (tick > _frontTick) _frontTick = tick;
        }

        /// <summary>
        /// Положить в ленту ГОТОВЫЕ снимки тика — вход для того, кто получил их со стороны, а не снял с
        /// живого мира: клиент кооп-сессии, реплей с диска, стенд.
        /// <para>Отдельно от <see cref="CaptureTick"/>, потому что тот снимает состояние с
        /// <see cref="RuntimeUnit"/>, а у приёмника живых юнитов нет вовсе — у него есть только то, что
        /// приехало. Один метод на два случая заставил бы клиента поднимать симуляцию, которую он не
        /// считает.</para>
        /// </summary>
        public void CaptureSnapshots(
            int tick, IReadOnlyList<UnitSnapshot> units, IReadOnlyList<ProjectileSnapshot> projectiles = null)
        {
            Frame frame = _ring[Slot(tick)];
            frame.Tick = tick;

            frame.Units.Clear();
            if (units != null)
                for (int i = 0; i < units.Count; i++) frame.Units.Add(units[i]);

            frame.Projectiles.Clear();
            if (projectiles != null)
                for (int i = 0; i < projectiles.Count; i++) frame.Projectiles.Add(projectiles[i]);

            if (tick > _frontTick) _frontTick = tick;
        }

        /// <summary>
        /// Кадр тика, если он ещё в окне. <c>false</c> — тик либо не записан, либо уже вытеснен
        /// (показ отстал больше, чем на окно: это не потеря кадров, а причина растить окно).
        /// </summary>
        public bool TryGetFrame(int tick, out IReadOnlyList<UnitSnapshot> units)
            => TryGetFrame(tick, out units, out _);

        /// <summary>Кадр целиком: юниты и снаряды одного и того же тика.</summary>
        public bool TryGetFrame(
            int tick, out IReadOnlyList<UnitSnapshot> units, out IReadOnlyList<ProjectileSnapshot> projectiles)
        {
            if (tick >= 0 && tick <= _frontTick)
            {
                Frame frame = _ring[Slot(tick)];
                if (frame.Tick == tick)
                {
                    units       = frame.Units;
                    projectiles = frame.Projectiles;
                    return true;
                }
            }

            units       = null;
            projectiles = null;
            return false;
        }

        /// <summary>
        /// Записать событие, держа ленту отсортированной по паре (тик, доля тика).
        /// <para><b>Почему вставкой, а не просто <c>Add</c>:</b> подача идёт линейным курсором, и событие
        /// с долей 0.9, попавшее в список раньше события с долей 0.1 того же тика, задержало бы второе до
        /// своей доли — то есть sub-tick точность съела бы сама себя. Сортировка внутри тика делает
        /// порядок подачи порядком МОМЕНТОВ, а не порядком обхода юнитов.</para>
        /// <para>Цена почти нулевая: события приходят по возрастанию тика, поэтому сдвиг случается только
        /// среди событий последнего тика — их единицы. Равные доли сохраняют порядок записи (вставка
        /// стабильна), поэтому у событий без доли поведение прежнее.</para>
        /// </summary>
        public void Record(in TapeEvent ev)
        {
            int i = _events.Count;
            while (i > 0)
            {
                TapeEvent prev = _events[i - 1];
                if (prev.Tick < ev.Tick) break;
                if (prev.Tick == ev.Tick && prev.SubTick <= ev.SubTick) break;
                i--;
            }

            if (i == _events.Count) _events.Add(ev);
            else                    _events.Insert(i, ev);
        }

        /// <summary>
        /// Записать урон: подробности едут в свой список, событие — ссылкой на них.
        /// <paramref name="subTick"/> — доля тика, в которую пришёлся контакт (0 = на границе тика).
        /// </summary>
        public void RecordDamage(int tick, int sourceId, int targetId, in DamageResult result,
            float subTick = 0f)
        {
            _damage.Add(new TapeDamage(in result));
            Record(new TapeEvent(
                TapeEventKind.DamageDealt, tick, sourceId, targetId,
                payloadIndex: _damage.Count - 1, subTick: subTick));
        }

        /// <summary>
        /// Записать зону удара: геометрия в свой список, событие — ссылкой на неё.
        /// <paramref name="subTick"/> — доля тика контакта: зона это удар, и её вспышка обязана попасть в
        /// тот же кадр, что и цифры по накрытым целям.
        /// </summary>
        public void RecordAreaHit(int tick, in AreaHit hit, float subTick = 0f)
        {
            _areaHits.Add(hit);
            Record(new TapeEvent(
                TapeEventKind.AreaHit, tick, payloadIndex: _areaHits.Count - 1, subTick: subTick));
        }

        /// <summary>
        /// Записать конец боя. Исход едет payload'ом, а не числом в <c>Flags</c>: он несёт и вид
        /// (победа/ничья), и команду-победителя, а плющить это в одно поле — потерять половину.
        /// </summary>
        public void RecordBattleEnded(int tick, in BattleOutcome outcome)
        {
            _outcomes.Add(outcome);
            Record(new TapeEvent(TapeEventKind.BattleEnded, tick, payloadIndex: _outcomes.Count - 1));
        }

        /// <summary>Записать наложение или спад эффекта: определение едет ссылкой в свой список.</summary>
        public void RecordEffect(int tick, TapeEventKind kind, int targetId, Data.Definitions.EffectData def)
        {
            _effectDefs.Add(def);
            Record(new TapeEvent(kind, tick, targetId: targetId, payloadIndex: _effectDefs.Count - 1));
        }

        /// <summary>
        /// Записать каст с его определением: <paramref name="amount"/> несёт длительность подготовки для
        /// <c>AbilityCastStarted</c> и не используется мгновенным <c>AbilityCast</c>.
        /// </summary>
        public void RecordAbility(int tick, TapeEventKind kind, int casterId,
            Data.Definitions.AbilityData def, float amount = 0f)
        {
            _abilityDefs.Add(def);
            Record(new TapeEvent(kind, tick, casterId, amount: amount, payloadIndex: _abilityDefs.Count - 1));
        }

        /// <summary>
        /// Индекс первого события с тиком не меньше <paramref name="tick"/>, или <see cref="EventCount"/>,
        /// если таких нет. События пишутся в порядке тиков, поэтому поиск двоичный.
        /// </summary>
        public int FindFirstEventAtOrAfter(int tick)
        {
            int lo = 0, hi = _events.Count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (_events[mid].Tick < tick) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// Собрать события в диапазоне тиков включительно. Для показа (что уже пора показать) и для
        /// режиссуры (что случится в ближайшие N тиков — то самое знание будущего).
        /// </summary>
        public void CollectEvents(int fromTick, int toTick, List<TapeEvent> into)
        {
            into.Clear();
            for (int i = FindFirstEventAtOrAfter(fromTick); i < _events.Count; i++)
            {
                TapeEvent ev = _events[i];
                if (ev.Tick > toTick) break;
                into.Add(ev);
            }
        }

        /// <summary>
        /// Полностью очистить ленту. Обязательна при dev-рестарте боя на месте: снимки и события
        /// прошлого боя в буфере дают показу призрачные смерти.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _ring.Length; i++)
            {
                _ring[i].Tick = NoTick;
                _ring[i].Units.Clear();
                _ring[i].Projectiles.Clear();
            }
            _events.Clear();
            _damage.Clear();
            _areaHits.Clear();
            _outcomes.Clear();
            _effectDefs.Clear();
            _abilityDefs.Clear();
            _frontTick = NoTick;
        }

        private int Slot(int tick) => tick % WindowTicks;
    }
}
