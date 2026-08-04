using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Guild;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Выбор узла на world-карте: зажигает достижимые узлы и ждёт, пока выбор не появится В СОСТОЯНИИ
    /// забега. Сам ничего не рисует и никого не слушает — ни клик, ни сеть.
    /// </summary>
    /// <remarks>
    /// <b>Ждём состояние, а не клик</b> (решение Макса 04.08.2026: «чисто, понятно и один источник
    /// правды»). Прежде клик по карте резолвил здешний <c>UniTaskCompletionSource</c> — то есть «куда мы
    /// идём» жило в стеке одной машины. Из этого следовало сразу три вещи: напарник не мог выбрать
    /// вовсе (будить чужой стек нечем), узлы у него не горели, а реконнект посреди узла не знал, где
    /// игроки находятся.
    /// <para>Теперь клик — команда <c>ChooseNode</c>, применитель пишет
    /// <see cref="MapState.EnteringNodeId"/>, а мы ждём эту запись. Чей был клик, здесь неизвестно и
    /// неважно; лог команд по-прежнему воспроизводит забег целиком, потому что петля стала реакцией на
    /// состояние.</para>
    /// <para><b>Уже записанный выбор не ждём заново:</b> так забег переживает реконнект и перезапуск —
    /// игрок возвращается в тот узел, в который вошёл, а не на развилку перед ним.</para>
    /// <para>Контракт петли (<c>ActRunner</c>) не изменился: она просит выбрать узел и получает его.</para>
    /// </remarks>
    public sealed class WorldMapNodeChooser : IMapNodeChooser
    {
        private readonly WorldMapController _map;
        private readonly RunStateService    _runStates;

        public WorldMapNodeChooser(WorldMapController map, RunStateService runStates)
        {
            _map       = map;
            _runStates = runStates;
        }

        public async UniTask<MapNode> ChooseAsync(MapState map, IReadOnlyList<MapNode> available,
                                                  CancellationToken ct = default, bool openMap = true)
        {
            // Выбор уже сделан и узел ещё не пройден — входим в него, ничего не спрашивая.
            MapNode pending = Find(available, map?.EnteringNodeId);
            if (pending != null) return pending;

            var tcs = new UniTaskCompletionSource<string>();

            void OnCommitted(RunState run)
            {
                string chosen = run?.Map?.EnteringNodeId;
                if (!string.IsNullOrEmpty(chosen)) tcs.TrySetResult(chosen);
            }

            _runStates.Committed += OnCommitted;
            _map.BeginChoose(available, openMap);

            try
            {
                // Между подпиской и этой строкой команда могла уже примениться — перечитываем состояние,
                // иначе редкая гонка оставила бы петлю ждать того, что уже случилось.
                OnCommitted(_runStates.Current);

                // AttachExternalCancellation: отмена забега («В меню») размотает ожидание исключением, а
                // finally всё равно погасит узлы — иначе они остались бы гореть в мире (QA #37).
                string chosenId = await tcs.Task.AttachExternalCancellation(ct);
                return Find(available, chosenId);
            }
            finally
            {
                _runStates.Committed -= OnCommitted;
                _map.EndChoose();
            }
        }

        private static MapNode Find(IReadOnlyList<MapNode> available, string id)
        {
            if (string.IsNullOrEmpty(id) || available == null) return null;

            for (int i = 0; i < available.Count; i++)
                if (available[i].Id == id) return available[i];

            return null;
        }
    }
}
