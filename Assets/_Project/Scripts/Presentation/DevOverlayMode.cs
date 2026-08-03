using Guildmaster.Combat.Tape;
using Guildmaster.Core.Simulation;

namespace Guildmaster.Presentation
{
    /// <summary>Откуда dev-оверлей берёт состояние: из показанного кадра или из живой симуляции.</summary>
    public enum DevOverlaySource
    {
        /// <summary>Кадр ленты — то же «сейчас», что видит игрок. Оверлей совпадает с картинкой.</summary>
        Presentation = 0,

        /// <summary>Живой сим — он впереди показа на окно опережения. Режим отладки САМОЙ ленты.</summary>
        Simulation = 1,
    }

    /// <summary>
    /// Режим dev-оверлеев боя (<see cref="CombatDebugDraw"/>, <see cref="CombatStatusOverlay"/>) —
    /// один владелец на бой, чтобы оверлеи не расходились между собой. Переключается командой
    /// <c>gm_overlay_source</c>.
    /// <para><b>Почему по умолчанию ПОКАЗ, хотя ТЗ обещало сим.</b> Оба оверлея рисуют в МИРОВЫХ
    /// координатах поверх арены. Сим уходит вперёд на окно опережения, поэтому в режиме «сим» кольца и
    /// радиусы висят там, где на экране юнитов ещё нет: инструмент наглядности превращается в мусор и
    /// рождает ложные баги («статус горит, а на экране ничего»). Правда модели остаётся доступной, но
    /// как осознанно выбранный режим — для отладки ленты, где расхождение и есть предмет наблюдения.</para>
    /// <para>Подпись режима <see cref="Describe"/> живёт здесь же: текст один, а печатают его оверлеи
    /// каждый своим способом (экранный GUI, гизмо-надпись). Это dev-инструмент, поэтому текст
    /// намеренно без лок-ключей.</para>
    /// </summary>
    public sealed class DevOverlayMode
    {
        private readonly BattleTapePlayback _playback;

        public DevOverlayMode(BattleTapePlayback playback) => _playback = playback;

        /// <summary>Текущий источник состояния для оверлеев.</summary>
        public DevOverlaySource Source { get; set; } = DevOverlaySource.Presentation;

        /// <summary>Читает ли оверлей живую симуляцию (то есть будущее относительно картинки).</summary>
        public bool ReadsSimulation => Source == DevOverlaySource.Simulation;

        /// <summary>Переключить источник и вернуть новый — для dev-команды.</summary>
        public DevOverlaySource Toggle()
        {
            Source = ReadsSimulation ? DevOverlaySource.Presentation : DevOverlaySource.Simulation;
            return Source;
        }

        /// <summary>
        /// Подпись режима для человека: что за источник и насколько он разъехался с картинкой. Именно
        /// второе и делает подпись нужной — «сим» без числа не объясняет, почему оверлей не там, где бой.
        /// </summary>
        public string Describe() => Describe(_playback != null ? _playback.Lead : 0);

        /// <summary>Чистая формулировка подписи — тестируется без сцены и ленты.</summary>
        public static string Describe(DevOverlaySource source, int leadTicks)
        {
            float leadSeconds = leadTicks / (float)SimConstants.TickRate;
            return source == DevOverlaySource.Simulation
                ? $"[dev] оверлеи: СИМ — впереди картинки на {leadSeconds:0.0} с"
                : $"[dev] оверлеи: ПОКАЗ — как на экране (сим впереди на {leadSeconds:0.0} с)";
        }

        private string Describe(int leadTicks) => Describe(Source, leadTicks);
    }
}
