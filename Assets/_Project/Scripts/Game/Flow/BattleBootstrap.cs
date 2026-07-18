using System;
using Guildmaster.Combat;
using Guildmaster.Presentation;
using MessagePipe;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Дочерний EntryPoint боевого скоупа (план 11 §4 A2): связывает готовую боевую клетку с макро-флоу через
    /// <see cref="IBattleSession"/>. На старте забирает запрос боя (если бой запущен из <see cref="BattleFlow"/>,
    /// а не вручную dev-панелью) и грузит его; репортит исход обратно во флоу по <c>BattleEndedEvent</c>;
    /// отдаёт делегат рестарта на месте (для ретраев поражения). Регистрируется ПОСЛЕ
    /// <c>DeploymentController</c>, чтобы его подписка на Free-расстановку успела встать до <c>LoadPreset</c>.
    /// </summary>
    public sealed class BattleBootstrap : IStartable, IDisposable
    {
        private readonly IBattleSession               _session;
        private readonly EncounterLoader              _loader;
        private readonly ISubscriber<BattleEndedEvent> _endedSub;

        private IDisposable _endedSubscription;

        public BattleBootstrap(IBattleSession session, EncounterLoader loader,
                               ISubscriber<BattleEndedEvent> endedSub)
        {
            _session  = session;
            _loader   = loader;
            _endedSub = endedSub;
        }

        public void Start()
        {
            // Исход боя → флоу. Подписка живёт весь боевой скоуп (ретраи переиспользуют её).
            _endedSubscription = _endedSub.Subscribe(OnBattleEnded);

            // Ретрай = перезапуск последнего боя на месте (dev-R путь), без перезагрузки сцены.
            _session.BindRestart(() => _loader.Reload());

            // Persist-мир: боевой скоуп живёт всю сессию, поэтому «запуск боя» — не создание сцены,
            // а команда в живой sim. RequestLaunch из BattleFlow дёрнет этот делегат (см. IBattleSession).
            _session.BindLaunch(LaunchBattle);

            // Legacy-совместимость: если бой всё же положили через SetPending (старый путь до persist), заберём его.
            if (_session.TryConsumePending(out Data.Definitions.BattlePresetData pending))
            {
                if (pending != null) LaunchBattle(pending);
                else Debug.LogWarning("[BattleBootstrap] - pending-запрос пуст (preset == null)");
            }
        }

        public void Dispose()
        {
            _session.UnbindLaunch();
            _session.UnbindRestart();
            _endedSubscription?.Dispose();
        }

        // Запуск боя в живом боевом скоупе. Пока — полный LoadPreset (отряд+враги из пресета, как раньше);
        // в Ф2.3 отряд переедет на persist (split-spawn), и здесь останется только доспавн врагов.
        private void LaunchBattle(Data.Definitions.BattlePresetData preset)
        {
            if (preset == null) { Debug.LogWarning("[BattleBootstrap] - LaunchBattle: preset == null"); return; }
            _loader.LoadPreset(preset);
        }

        private void OnBattleEnded(BattleEndedEvent e) => _session.ReportOutcome(e.Outcome);
    }
}
