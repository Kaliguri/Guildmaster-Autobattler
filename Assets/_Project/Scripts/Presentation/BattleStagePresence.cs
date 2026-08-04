using System;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Презентационный сигнал «на сцене идёт бой, его надо кадрировать». Ортогонален часам забега
    /// <see cref="Guildmaster.Data.Definitions.IBattleClock"/>: живой бой поднимает и часы (забег), и
    /// сцену (показ) — они совпадают, потому что бой у нас бывает внутри забега. Повтор поднимает ТОЛЬКО
    /// сцену: камера кадрирует бой, а забеговый UI, ввод и звук про него не знают.
    /// </summary>
    /// <remarks>
    /// Заведён, потому что «идёт показ боя» и «игрок в боевом узле забега» — разные понятия, и повторы
    /// (фон меню, дальше пересмотр боя и спектейт) их расщепляют: бой показывается без забега. Гнать ради
    /// камеры общие часы в <c>Fighting</c> нельзя — их читают навигатор ввода и забеговый звук
    /// (журнал 2026-08-04-battle-on-stage-vs-the-run-clock). Живёт в мире: его читает камера, ставят —
    /// те, кто показывает бой вне забега.
    /// </remarks>
    public sealed class BattleStagePresence
    {
        private bool _onStage;

        /// <summary>Идёт ли показ боя, который камере надо кадрировать.</summary>
        public bool OnStage => _onStage;

        /// <summary>Сцена поднялась или опустилась. Камера пересчитывает по нему боевое кадрирование.</summary>
        public event Action Changed;

        public void SetOnStage(bool on)
        {
            if (_onStage == on) return;
            _onStage = on;
            Changed?.Invoke();
        }
    }
}
