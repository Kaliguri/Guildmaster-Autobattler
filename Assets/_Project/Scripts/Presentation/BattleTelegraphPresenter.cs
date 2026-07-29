using System;
using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Телеграфы: подводки к событиям, которые ЕЩЁ НЕ ПОКАЗАНЫ. Читает ленту боя впереди момента показа и
    /// заранее говорит виду «сейчас на тебе появится вот это» — щит «Оплота» так поднимается ДО удара, а не
    /// в его кадр.
    /// <para><b>Это и есть проверка всей схемы «сим впереди, показ с лагом».</b> Без лага такую подводку
    /// можно было делать только предикцией, которая иногда врёт; здесь она — знание: сим уже посчитал
    /// наложение щита, лента его держит, показ до него ещё не дошёл.</para>
    /// <para><b>Владелец времени — ассет</b> (<see cref="EffectData.TelegraphSeconds"/>): эффект сам
    /// объявляет, за сколько его анонсировать. Кода это не касается, и телеграф достаётся любому эффекту,
    /// который его попросит, а не одному киту.</para>
    /// </summary>
    public sealed class BattleTelegraphPresenter : ITickable, IDisposable
    {
        // Сколько тиков вперёд имеет смысл смотреть: самый долгий телеграф — секунда (Range в ассете).
        private const int MaxTelegraphTicks = SimConstants.TickRate;

        // Порог чистки памяти о сыгранных подводках: до него множество мало и трогать его незачем.
        private const int ForgetThreshold = 64;

        private readonly BattleTape         _tape;
        private readonly BattleTapePlayback _playback;
        private readonly CombatPresenter    _presenter;
        private readonly CombatSimulation   _simulation;

        // Переиспользуемый буфер заглядывания вперёд: Tick идёт каждый кадр, мусорить тут нельзя.
        private readonly List<TapeEvent> _upcoming = new List<TapeEvent>(32);

        // Тики событий, чья подводка уже сыграна: иначе она перезапускалась бы каждый кадр, пока событие
        // приближается, и вместо нарастания получилось бы мигание. Ключ — тик события плюс носитель.
        private readonly HashSet<long> _announced = new HashSet<long>();

        public BattleTelegraphPresenter(
            BattleTape tape,
            BattleTapePlayback playback,
            CombatPresenter presenter,
            CombatSimulation simulation)
        {
            _tape       = tape;
            _playback   = playback;
            _presenter  = presenter;
            _simulation = simulation;

            // Рестарт боя — служебное событие: лента уже чиста, и память о подводках прошлого боя тоже
            // должна уйти, иначе тики нового боя сочтутся уже объявленными.
            _simulation.OnBattleReset += Reset;
        }

        public void Dispose() => _simulation.OnBattleReset -= Reset;

        public void Tick()
        {
            if (!_playback.IsPlaying) return;

            int viewTick = _playback.ViewTick;
            if (viewTick == BattleTape.NoTick) return;

            _tape.CollectEvents(viewTick + 1, viewTick + MaxTelegraphTicks, _upcoming);

            for (int i = 0; i < _upcoming.Count; i++)
            {
                TapeEvent ev = _upcoming[i];
                if (ev.Kind != TapeEventKind.EffectApplied) continue;

                EffectData def = _tape.GetEffect(ev.PayloadIndex);
                if (def == null || def.TelegraphSeconds <= 0f) continue;

                // Пора ли: до события осталось ровно столько, за сколько эффект просил себя объявить.
                int leadTicks = Mathf.RoundToInt(def.TelegraphSeconds * SimConstants.TickRate);
                if (leadTicks <= 0 || ev.Tick - viewTick > leadTicks) continue;

                long key = Key(ev.Tick, ev.TargetId);
                if (!_announced.Add(key)) continue;             // эту подводку уже играли

                if (!_presenter.TryGetView(ev.TargetId, out UnitView view) || view == null) continue;

                // Цвет — щита, из палитры дизайн-системы (её единственный владелец — презентер). Пока
                // телеграф в игре один; когда появятся другие, здесь встанет выбор цвета по тегу эффекта,
                // а не новый цвет в коде.
                view.ShowTelegraph(_presenter.ShieldColor, def.TelegraphSeconds);
            }

            ForgetPast(viewTick);
        }

        /// <summary>Забыть сыгранные подводки (рестарт боя): тики нового боя объявляются заново.</summary>
        public void Reset() => _announced.Clear();

        // Ключ «событие + носитель» в одном long: тик в старших битах, id в младших.
        private static long Key(int tick, int unitId) => ((long)tick << 32) ^ (uint)unitId;

        // Подводки прошлого больше не нужны: чистим показанное, чтобы множество не росло весь бой.
        private void ForgetPast(int viewTick)
        {
            if (_announced.Count < ForgetThreshold) return;

            _announced.RemoveWhere(key => (int)(key >> 32) <= viewTick);
        }
    }
}
