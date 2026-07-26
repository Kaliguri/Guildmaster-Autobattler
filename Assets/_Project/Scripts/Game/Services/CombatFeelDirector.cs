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
    /// </summary>
    public sealed class CombatFeelDirector : IStartable, IDisposable
    {
        private readonly ISubscriber<DamageDealtEvent> _damageSub;
        private readonly ISubscriber<BattleEndedEvent> _endedSub;
        private readonly CombatSimulation _sim;
        private readonly TimeScaleService _time;
        private readonly IScreenShake     _shake;
        private readonly CombatFeelConfig _cfg;
        private readonly Core.Audio.IAudioService _audio;

        private IDisposable _subscriptions;
        private float _lastKillSlowmo = float.NegativeInfinity;

        public CombatFeelDirector(
            ISubscriber<DamageDealtEvent> damageSub,
            ISubscriber<BattleEndedEvent> endedSub,
            CombatSimulation sim,
            TimeScaleService time,
            IScreenShake shake,
            CombatFeelConfig cfg,
            Core.Audio.IAudioService audio)
        {
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

        // Перезапуск боя (dev-R): снять застрявший slowmo/финишер-секвенцию и остаточную тряску, сбросить
        // кулдаун килл-слоумо — иначе новый бой идёт в замедлении и первый килл может не «щёлкнуть».
        private void OnBattleReset()
        {
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
                if (now - _lastKillSlowmo >= _cfg.KillSlowCooldown)
                {
                    _lastKillSlowmo = now;
                    _time.CinematicPulse(_cfg.KillSlowFactor, 0f, _cfg.KillSlowRelease);
                    // Стингер идёт под тем же кулдауном, что и слоумо: на пачке добиваний он иначе
                    // наложится сам на себя и превратится в кашу.
                    _audio?.Play("feel.kill.stinger");
                }
                _shake.Shake(_cfg.KillShake);
                return;
            }

            // Тяжёлый (не добивающий) удар → только тряска, по доле урона от MaxHP цели, выше порога.
            RuntimeUnit target = e.Target;
            float maxHp = target != null ? target.Stats.Get(StatType.MaxHP) : 0f;
            if (maxHp <= 0f) return;
            float frac = e.Result.TotalDamage / maxHp;
            if (frac < _cfg.HeavyHitFrac) return;
            float k = Mathf.Clamp01((frac - _cfg.HeavyHitFrac) / (1f - _cfg.HeavyHitFrac));
            _shake.Shake(Mathf.Lerp(_cfg.HeavyShakeMin, _cfg.HeavyShakeMax, k));
            // Басовый слой поверх обычного удара: тряска без звука читается как «экран дёрнулся сам».
            _audio?.Play("feel.heavy_hit.hit");
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
