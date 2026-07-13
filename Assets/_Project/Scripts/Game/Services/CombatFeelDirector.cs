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
        private readonly TimeScaleService _time;
        private readonly IScreenShake     _shake;
        private readonly CombatFeelConfig _cfg;

        private IDisposable _subscriptions;
        private float _lastKillSlowmo = float.NegativeInfinity;

        public CombatFeelDirector(
            ISubscriber<DamageDealtEvent> damageSub,
            ISubscriber<BattleEndedEvent> endedSub,
            TimeScaleService time,
            IScreenShake shake,
            CombatFeelConfig cfg)
        {
            _damageSub = damageSub;
            _endedSub  = endedSub;
            _time      = time;
            _shake     = shake;
            _cfg       = cfg;
        }

        public void Start()
        {
            var bag = DisposableBag.CreateBuilder();
            _damageSub.Subscribe(OnDamage).AddTo(bag);
            _endedSub.Subscribe(OnBattleEnded).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose() => _subscriptions?.Dispose();

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
        }

        // Конец боя → сильное slowmo с удержанием и возвратом по кривой (финишер-момент) + сильная тряска.
        private void OnBattleEnded(BattleEndedEvent e)
        {
            _time.CinematicPulse(_cfg.BattleEndFactor, _cfg.BattleEndHold, _cfg.BattleEndRelease, _cfg.BattleEndReleaseCurve);
            _shake.Shake(_cfg.BattleEndShake);
        }
    }
}
