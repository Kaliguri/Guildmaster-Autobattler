using System;
using Guildmaster.Combat;
using Guildmaster.Data.Stats;
using Guildmaster.Presentation;
using Guildmaster.Presentation.Design;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Режиссёр «сочности» боя: единая политика ЗНАЧИМОСТИ поверх sim-событий. Одно место решает, какое
    /// событие достойно global-эффекта (slowmo, дальше — screenshake), чтобы это не растекалось по
    /// <see cref="UnitView"/>. Per-hit локальный фидбэк (вспышка/сплющивание/hitstop) остаётся в презентации;
    /// сюда приходят только КРУПНЫЕ моменты — добивающий удар и конец боя. Подписка на MessagePipe (развязка
    /// от симуляции, как у <c>AudioPresenter</c>). Крита в модели нет — «момент» = <c>KilledTarget</c>.
    /// <para><b>Предчувствие смерти (Ф6 ленты боя):</b> замедление начинается ЧУТЬ РАНЬШЕ смертельного
    /// удара. Это невозможно без лага показа — «раньше» неоткуда взять, если о смерти узнаёшь в тот же
    /// кадр. Здесь режиссёр смотрит события ленты, до которых показ ещё не дошёл, и запускает slowmo
    /// заранее. Когда запаса нет (первые кадры боя), остаётся честная деградация: замедление щёлкает
    /// в момент удара, как было до ленты.</para>
    /// </summary>
    public sealed class CombatFeelDirector : IStartable, ITickable, IDisposable
    {
        private readonly ISubscriber<DamageDealtEvent> _damageSub;
        private readonly ISubscriber<BattleEndedEvent> _endedSub;
        private readonly CombatSimulation _sim;
        private readonly TimeScaleService _time;
        private readonly IScreenShake     _shake;
        private readonly CombatFeelConfig _cfg;
        private readonly Core.Audio.IAudioService _audio;
        private readonly Combat.Tape.BattleTape         _tape;
        private readonly Combat.Tape.BattleTapePlayback _playback;

        private IDisposable _subscriptions;
        private float _lastKillSlowmo = float.NegativeInfinity;

        // Буфер под заглядывание вперёд: переиспользуется, чтобы не мусорить каждый кадр.
        private readonly System.Collections.Generic.List<Combat.Tape.TapeEvent> _upcoming =
            new System.Collections.Generic.List<Combat.Tape.TapeEvent>(32);

        // Тик смертельного удара, под который замедление УЖЕ запущено предчувствием. NoTick = нет такого.
        private int _foreseenKillTick = Combat.Tape.BattleTape.NoTick;

        public CombatFeelDirector(
            ISubscriber<DamageDealtEvent> damageSub,
            ISubscriber<BattleEndedEvent> endedSub,
            CombatSimulation sim,
            TimeScaleService time,
            IScreenShake shake,
            CombatFeelConfig cfg,
            Core.Audio.IAudioService audio,
            Combat.Tape.BattleTape tape,
            Combat.Tape.BattleTapePlayback playback)
        {
            _tape      = tape;
            _playback  = playback;
            _damageSub = damageSub;
            _endedSub  = endedSub;
            _sim       = sim;
            _time      = time;
            _shake     = shake;
            _cfg       = cfg;
            _audio     = audio;
        }

        public void Start()
        {
            var bag = DisposableBag.CreateBuilder();
            _damageSub.Subscribe(OnDamage).AddTo(bag);
            _endedSub.Subscribe(OnBattleEnded).AddTo(bag);
            _subscriptions = bag.Build();

            if (_sim != null) _sim.OnBattleReset += OnBattleReset;
        }

        public void Dispose()
        {
            if (_sim != null) _sim.OnBattleReset -= OnBattleReset;
            _subscriptions?.Dispose();
        }

        /// <summary>
        /// Заглянуть вперёд показа: если смертельный удар случится в пределах <c>KillSlowLeadSeconds</c>,
        /// начать замедление СЕЙЧАС — тогда игрок входит в момент смерти уже в slowmo, а не узнаёт о нём
        /// постфактум.
        /// </summary>
        public void Tick()
        {
            float leadSeconds = _cfg != null ? _cfg.KillSlowLeadSeconds : 0f;
            if (leadSeconds <= 0f || _playback == null || !_playback.IsPlaying) return;

            int leadTicks = Mathf.RoundToInt(leadSeconds * Core.Simulation.SimConstants.TickRate);
            if (leadTicks <= 0) return;

            int viewTick = _playback.ViewTick;
            _tape.CollectEvents(viewTick + 1, viewTick + leadTicks, _upcoming);

            for (int i = 0; i < _upcoming.Count; i++)
            {
                Combat.Tape.TapeEvent ev = _upcoming[i];
                if (ev.Kind != Combat.Tape.TapeEventKind.DamageDealt) continue;
                if (!_tape.GetDamage(ev.PayloadIndex).KilledTarget) continue;
                if (ev.Tick == _foreseenKillTick) continue;   // под этот удар slowmo уже запущено

                float now = Time.unscaledTime;
                if (now - _lastKillSlowmo < _cfg.KillSlowCooldown) return;

                // Держим замедление до самого удара и ещё немного после: «чуть раньше и чуть позже»
                // — ровно то, ради чего затевался лаг показа.
                float untilHit = (ev.Tick - viewTick) / (float)Core.Simulation.SimConstants.TickRate;
                _lastKillSlowmo   = now;
                _foreseenKillTick = ev.Tick;
                _time.CinematicPulse(_cfg.KillSlowFactor, untilHit, _cfg.KillSlowRelease);
                return;
            }
        }

        // Перезапуск боя (dev-R): снять застрявший slowmo/финишер-секвенцию и остаточную тряску, сбросить
        // кулдаун килл-слоумо — иначе новый бой идёт в замедлении и первый килл может не «щёлкнуть».
        private void OnBattleReset()
        {
            _foreseenKillTick = Combat.Tape.BattleTape.NoTick;
            _time.Reset();
            _shake.ResetShake();
            _lastKillSlowmo = float.NegativeInfinity;
        }

        private void OnDamage(DamageDealtEvent e)
        {
            // Добивающий удар → slowmo-момент (не чаще кулдауна, unscaled — на толпе киллов много) + тряска.
            if (e.Result.KilledTarget)
            {
                float now = Time.unscaledTime;

                // Замедление могло начаться ЗАРАНЕЕ (предчувствие в Tick) — тогда повторно не щёлкаем,
                // иначе момент смерти сбросит уже идущее замедление на его начало.
                bool alreadyForeseen = _foreseenKillTick != Combat.Tape.BattleTape.NoTick;
                _foreseenKillTick = Combat.Tape.BattleTape.NoTick;

                if (!alreadyForeseen && now - _lastKillSlowmo >= _cfg.KillSlowCooldown)
                {
                    _lastKillSlowmo = now;
                    _time.CinematicPulse(_cfg.KillSlowFactor, 0f, _cfg.KillSlowRelease);
                    // Стингер идёт под тем же кулдауном, что и слоумо: на пачке добиваний он иначе
                    // наложится сам на себя и превратится в кашу.
                    _audio?.Play("feel.kill.stinger");   // стингер — событие всего боя, не точки на поле
                }
                _shake.Shake(_cfg.KillShake);
                return;
            }

            // Тяжёлый (не добивающий) удар → только тряска, по доле урона от MaxHP цели, выше порога.
            // MaxHP и точка берутся из СОБЫТИЯ (снято с показанного тика), а не с живого юнита: тот уже
            // на окно опережения впереди, и тряска пришла бы от позиции, которой игрок ещё не видел.
            float maxHp = e.TargetMaxHp;
            if (maxHp <= 0f) return;
            float frac = e.Result.TotalDamage / maxHp;
            if (frac < _cfg.HeavyHitFrac) return;
            float k = Mathf.Clamp01((frac - _cfg.HeavyHitFrac) / (1f - _cfg.HeavyHitFrac));
            _shake.Shake(Mathf.Lerp(_cfg.HeavyShakeMin, _cfg.HeavyShakeMax, k));
            // Басовый слой поверх обычного удара — из точки удара, как и сам удар: иначе бас
            // приходит из центра, а хруст сбоку, и они разъезжаются.
            _audio?.PlayAt("feel.heavy_hit.hit", e.TargetPosition);
        }

        // Конец боя → финишер-таймлайн ступенями (совпадает с секвенсом смерти на scaled-времени):
        // 1) полная пауза на хит-эффекте → 2) slowmo анимации смерти → 3) сильное slowmo разлёта → 4) возврат.
        private void OnBattleEnded(BattleEndedEvent e)
        {
            var segments = new[]
            {
                new CinematicSegment(0f,                        _cfg.FinisherPause),            // 1: полный стоп
                new CinematicSegment(_cfg.FinisherDeathFactor,  _cfg.FinisherDeathDuration),    // 2: death slowmo
                new CinematicSegment(_cfg.FinisherShatterFactor, _cfg.FinisherShatterDuration), // 3: shatter slowmo
                new CinematicSegment(1f, _cfg.FinisherReturn, ramp: true, curve: _cfg.FinisherReturnCurve), // 4: возврат
            };
            _time.PlayCinematicSequence(segments);
            _shake.Shake(_cfg.BattleEndShake);
            _audio?.Play("feel.finisher.stinger"); // звук входа в финишер-слоумо, поверх победы/поражения
        }
    }
}
