using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Data.Definitions;
using Guildmaster.Guild;
using UnityEngine;

namespace Guildmaster.Game.Flow
{
    /// <summary>Показ экрана награды после узла (витрина 1-из-N → запись выбора в RunState).</summary>
    public interface IRewardPresenter
    {
        /// <summary>Скатать витрину тира, показать её, дождаться выбора и применить к <see cref="RunState"/>.
        /// <paramref name="ct"/> прерывает ожидание при выходе из забега (QA #37).</summary>
        UniTask PresentAsync(RewardTier tier, CancellationToken ct = default);
    }

    /// <summary>
    /// Презентер награды: катит витрину через <see cref="RewardService"/>, показывает её ВСЕЙ группе и
    /// применяет к <see cref="RunState"/> то, на чём сошлись.
    /// </summary>
    /// <remarks>
    /// <b>Награду берут все вместе</b> (заказ Макса 07.08.2026: «должны тыкнуть все обязательно»).
    /// Клик по карточке — голос, а не взятие; экран закрывается признаком срабатывания от общего
    /// решения. Пока выбор был локальным, первый нажавший забирал реликвию за группу.
    /// <para><b>Витрину катит хозяин и раздаёт результат раската</b> — гость собирает из id ту же
    /// витрину. Второй раскат у него дал бы другие три реликвии: бросок случаен.</para>
    /// <para><b>В соло ничего не меняется:</b> участник один, решение срабатывает в тот же кадр.</para>
    /// <para><b>Витрину показывает не он.</b> Презентер объявляет шаг узла, а экран открывает общий для
    /// обеих ролей потребитель (<c>NodeStageScreens</c>). Пока показ жил здесь, к витрине вело два пути
    /// — этот и гостевой, — и во втором признак «запас полон» был зашит в <c>false</c>.</para>
    /// </remarks>
    public sealed class RewardPresenter : IRewardPresenter
    {
        private readonly RewardService   _rewards;
        private readonly RunStateService _runStates;
        // Сброс реликвии ради места — односторонняя запись, идёт через шину и попадает в лог. Взятие
        // награды осталось прямым: оно спрашивает «влезло ли» синхронно, то есть транзакция.
        private readonly Guildmaster.Guild.Commands.IRunCommands _commands;
        private readonly Core.Net.ISharedDecision _decision;
        // Объявление витрины: и гостям, у которых нет ни генератора, ни забега, и своему же показу.
        private readonly Session.Net.HostNodeStage _stage;

        public RewardPresenter(RewardService rewards, RunStateService runStates,
                               Guildmaster.Guild.Commands.IRunCommands commands,
                               Core.Net.ISharedDecision decision,
                               Session.Net.HostNodeStage stage)
        {
            _rewards   = rewards;
            _runStates = runStates;
            _commands  = commands;
            _decision  = decision;
            _stage     = stage;
        }

        public async UniTask PresentAsync(RewardTier tier, CancellationToken ct = default)
        {
            IReadOnlyList<RelicData> choices = _rewards.RollChoices(tier);
            if (choices.Count == 0)
            {
                Debug.LogWarning("[RewardPresenter] - пул наград пуст (нет реликов в контент-БД) → без награды");
                return;
            }

            RunState run  = _runStates.Current;
            bool     full = _runStates.RelicInventoryFull;

            var chosen = new UniTaskCompletionSource<string>();

            // Ключ взводим ДО показа: гость получит витрину и счёт одним разом, а не «сначала карточки,
            // потом откуда-то счёт».
            _decision?.Bind(Core.Net.DecisionKeys.RewardPick, option => chosen.TrySetResult(option));

            // Витрину не показываем сами: объявляем шаг узла, а экран открывает общий для обеих ролей
            // потребитель (NodeStageScreens). Пока показ жил здесь, у витрины было ДВА пути — этот и
            // гостевой, — и во втором признак «запас полон» был зашит в false (HARD «равные игроки»).
            _stage?.Announce(Session.Net.NodeStageState.Reward(IdsOf(choices), full));

            try
            {
                string option = await chosen.Task.AttachExternalCancellation(ct);
                Apply(option, choices);
            }
            finally
            {
                // Снимаем и ключ, и объявление: экрана больше нет. Брошенный ключ показал бы счёт там,
                // где выбирать нечего, а брошенное объявление — витрину подключившемуся следом.
                _decision?.Unbind(Core.Net.DecisionKeys.RewardPick);
                _stage?.Clear();
            }
        }

        /// <summary>Применить то, на чём сошлись: взять, обменять или уйти ни с чем.</summary>
        private void Apply(string option, IReadOnlyList<RelicData> choices)
        {
            if (!RewardOptions.TryParse(option, out string relicId, out string dropId))
            {
                Debug.Log("[RewardPresenter] - награда пропущена");
                return;
            }

            // Сверяемся с ВИТРИНОЙ, а не с реестром: голос обязан указывать на то, что группе показали.
            RelicData relic = Find(choices, relicId);
            if (relic == null)
            {
                Debug.LogWarning($"[RewardPresenter] - голос за '{relicId}', которого нет в витрине → без награды");
                return;
            }

            if (!string.IsNullOrEmpty(dropId)) _commands.RemoveRelic(dropId);

            bool added = _runStates.TryAddRelic(relic.Id);
            Debug.Log($"[RewardPresenter] - награда: взят '{relic.Id}'" +
                      (dropId != null ? $" (сброшен '{dropId}')" : "") +
                      (added ? "" : " — НЕ добавлен (нет места?)"));
            _runStates.Autosave();
        }

        private static RelicData Find(IReadOnlyList<RelicData> choices, string id)
        {
            for (int i = 0; i < choices.Count; i++)
                if (choices[i] != null && choices[i].Id == id) return choices[i];

            return null;
        }

        private static string[] IdsOf(IReadOnlyList<RelicData> choices)
        {
            var ids = new string[choices.Count];
            for (int i = 0; i < choices.Count; i++) ids[i] = choices[i] != null ? choices[i].Id : string.Empty;
            return ids;
        }
    }
}
