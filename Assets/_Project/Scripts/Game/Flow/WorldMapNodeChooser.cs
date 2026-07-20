using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Guild;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// Фаза D-реализация <see cref="IMapNodeChooser"/>: выбор узла на world-карте. Сам ничего не рисует —
    /// говорит владельцу карты (<see cref="WorldMapController"/>) «жду выбор из этих узлов» и ждёт результат.
    /// Ровно с этого момента узлы и становятся доступными: до него (пока игрок не нажал «Продолжить»)
    /// карту можно открыть, но гореть на ней нечему.
    /// <para>Контракт петли (<c>ActRunner</c>) не меняется — подменяет <see cref="MapScreenNodeChooser"/>
    /// в DI одной строкой, и такой же одной строкой откатывается обратно на UITK-карту.</para>
    /// </summary>
    public sealed class WorldMapNodeChooser : IMapNodeChooser
    {
        private readonly WorldMapController _map;

        public WorldMapNodeChooser(WorldMapController map) => _map = map;

        public async UniTask<MapNode> ChooseAsync(MapState map, IReadOnlyList<MapNode> available, CancellationToken ct = default)
        {
            var tcs = new UniTaskCompletionSource<string>();
            _map.BeginChoose(available, id => tcs.TrySetResult(id));

            try
            {
                // AttachExternalCancellation: отмена забега («В меню») размотает ожидание исключением, а finally
                // ниже всё равно снимет карту — иначе она осталась бы висеть в мире (QA #37).
                string chosenId = await tcs.Task.AttachExternalCancellation(ct);
                foreach (MapNode node in available)
                    if (node.Id == chosenId) return node;
                return null;
            }
            finally
            {
                _map.EndChoose();
            }
        }
    }
}
