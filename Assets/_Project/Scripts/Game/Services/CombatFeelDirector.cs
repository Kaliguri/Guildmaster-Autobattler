using System;
using Guildmaster.Combat;
using Guildmaster.Data.Stats;
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
        private const float BattleEndFactor  = 0.1f;   // концовка боя — сильное slowmo (мир почти замер)
        private const float BattleEndRecover = 3.0f;   // и медленно, за 3с, возвращается к норме (финишер-момент)

        private const float KillShake      = 0.55f;    // тряска на добивающий удар
        private const float BattleEndShake = 0.75f;    // тряска на конец боя
        private const float HeavyHitFrac   = 0.15f;    // порог: доля урона от MaxHP цели, ниже — без тряски
        private const float HeavyShakeMin  = 0.2f;     // тряска на пороговый тяжёлый удар
        private const float HeavyShakeMax  = 0.5f;     // тряска на «в полздоровья» удар

        private readonly ISubscriber<DamageDealtEvent> _damageSub;
        private readonly ISubscriber<BattleEndedEvent> _endedSub;
        private readonly TimeScaleService _time;
        private readonly IScreenShake     _shake;

        private IDisposable _subscriptions;
        private float _lastKillSlowmo = float.NegativeInfinity;

        public CombatFeelDirector(
            ISubscriber<DamageDealtEvent> damageSub,
            ISubscriber<BattleEndedEvent> endedSub,
            TimeScaleService time,
            IScreenShake shake)
        {
            _damageSub = damageSub;
            _endedSub  = endedSub;
            _time      = time;
            _shake     = shake;
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
                if (now - _lastKillSlowmo >= KillSlowCooldown)
                {
                    _lastKillSlowmo = now;
                    _time.CinematicPulse(KillSlowFactor, KillSlowRecover);
                }
                _shake.Shake(KillShake);
                return;
            }

            // Тяжёлый (не добивающий) удар → только тряска, по доле урона от MaxHP цели, выше порога.
            RuntimeUnit target = e.Target;
            float maxHp = target != null ? target.Stats.Get(StatType.MaxHP) : 0f;
            if (maxHp <= 0f) return;
            float frac = e.Result.TotalDamage / maxHp;
            if (frac < HeavyHitFrac) return;
            float k = Mathf.Clamp01((frac - HeavyHitFrac) / (1f - HeavyHitFrac));
            _shake.Shake(Mathf.Lerp(HeavyShakeMin, HeavyShakeMax, k));
        }

        // Конец боя → более выраженный slowmo (перебивает kill-пульс) + сильная тряска.
        private void OnBattleEnded(BattleEndedEvent e)
        {
            _time.CinematicPulse(BattleEndFactor, BattleEndRecover);
            _shake.Shake(BattleEndShake);
        }
    }
}
