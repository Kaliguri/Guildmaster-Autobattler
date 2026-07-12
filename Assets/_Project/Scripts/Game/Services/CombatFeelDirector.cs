using System;
using Guildmaster.Presentation;
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
        // --- Тюнеры (пороги/сила). Константы; при желании вынести в SO-конфиг. ---
        private const float KillSlowFactor   = 0.4f;   // во сколько замедлить мир на добивающий удар
        private const float KillSlowRecover  = 0.5f;   // сек возврата к норме (unscaled)
        private const float KillSlowCooldown = 2.0f;   // сек между kill-slowmo — на толпе киллов много
        private const float BattleEndFactor  = 0.25f;  // концовка боя — драматичнее
        private const float BattleEndRecover = 1.4f;

        private readonly ISubscriber<DamageDealtEvent> _damageSub;
        private readonly ISubscriber<BattleEndedEvent> _endedSub;
        private readonly TimeScaleService _time;

        private IDisposable _subscriptions;
        private float _lastKillSlowmo = float.NegativeInfinity;

        public CombatFeelDirector(
            ISubscriber<DamageDealtEvent> damageSub,
            ISubscriber<BattleEndedEvent> endedSub,
            TimeScaleService time)
        {
            _damageSub = damageSub;
            _endedSub  = endedSub;
            _time      = time;
        }

        public void Start()
        {
            var bag = DisposableBag.CreateBuilder();
            _damageSub.Subscribe(OnDamage).AddTo(bag);
            _endedSub.Subscribe(OnBattleEnded).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose() => _subscriptions?.Dispose();

        // Добивающий удар → короткий slowmo-момент, но не чаще кулдауна (unscaled — считаем реальное время).
        private void OnDamage(DamageDealtEvent e)
        {
            if (!e.Result.KilledTarget) return;
            float now = Time.unscaledTime;
            if (now - _lastKillSlowmo < KillSlowCooldown) return;
            _lastKillSlowmo = now;
            _time.CinematicPulse(KillSlowFactor, KillSlowRecover);
        }

        // Конец боя → более выраженный slowmo (перебивает kill-пульс).
        private void OnBattleEnded(BattleEndedEvent e)
        {
            _time.CinematicPulse(BattleEndFactor, BattleEndRecover);
        }
    }
}
