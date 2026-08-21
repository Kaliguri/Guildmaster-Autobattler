using System;
using Guildmaster.Core.Persistence;
using Guildmaster.Guild;
using VContainer.Unity;

namespace Guildmaster.Game.Session
{
    /// <summary>
    /// Итог забега в статистику профиля: сколько забегов закончено, сколько выиграно, как далеко зашли.
    /// </summary>
    /// <remarks>
    /// <b>Один код на обе роли</b> (HARD-правило «равные игроки»). Записывает не петля акта, а
    /// объявленный ШАГ узла — тот самый, по которому экран исхода видят и хозяин, и гость. Повесить
    /// запись на петлю было проще, но петля есть только у владельца: гость прошёл тот же акт, а в его
    /// профиле не прибавилось бы ничего, и разницу эту никто не заказывал.
    ///
    /// <para><b>Считается ЗАВЕРШЁННЫЙ забег.</b> Брошенный на середине сюда не приходит по построению:
    /// уход в главное меню отменяет токен занятия, и шаг исхода не объявляется вовсе. Победа — акт
    /// пройден до конца (решение Макса 21.08.2026); поражение — отряд лёг и перезапусков не осталось.</para>
    ///
    /// <para><b>Пишем на ПЕРЕХОДЕ в исход, а не на каждом объявлении.</b> Шаг узла — это состояние, и
    /// хозяин объявляет его повторно тому, кто вошёл посреди забега. Без этой проверки переподключение
    /// на экране исхода добавляло бы профилю ещё один забег на каждый повтор.</para>
    /// </remarks>
    public sealed class RunStatsRecorder : IStartable, IDisposable
    {
        private readonly Net.ISessionStageView _stage;
        private readonly ISessionRunState      _run;
        private readonly IProfileService       _profiles;

        /// <summary>Прошлый шаг был исходом: повторное объявление того же исхода не считается.</summary>
        private bool _wasOutcome;

        public RunStatsRecorder(Net.ISessionStageView stage, ISessionRunState run, IProfileService profiles)
        {
            _stage    = stage;
            _run      = run;
            _profiles = profiles;
        }

        public void Start()
        {
            if (_stage == null) return;

            _stage.Changed += OnStageChanged;
        }

        public void Dispose()
        {
            if (_stage == null) return;

            _stage.Changed -= OnStageChanged;
        }

        private void OnStageChanged(Net.SessionStageState stage)
        {
            if (!stage.TryOpenOutcome(out Net.OutcomeStage outcome))
            {
                _wasOutcome = false;
                return;
            }

            if (_wasOutcome) return;
            _wasOutcome = true;

            // Длину забега берём из карты: она есть у обеих ролей — у владельца своя, у гостя
            // присланная снимком. Карты нет (забег уже свёрнут) — пишем ноль узлов, но сам забег
            // засчитываем: исход объявлен, а значит он состоялся.
            int nodes = MapTraversal.ClearedCount(_run?.Current?.Map);

            _profiles?.RecordRunFinished(outcome.Victory, nodes);
        }
    }
}
