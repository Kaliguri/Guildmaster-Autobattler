using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Guildmaster.Guild;
using MessagePipe;

namespace Guildmaster.Game.Flow
{
    /// <summary>
    /// A3-реализация <see cref="IMapNodeChooser"/>: выбор узла через экран карты. Публикует <see cref="OpenMapRequest"/>
    /// (UI открывает <c>MapScreen</c>), ждёт клик игрока по доступному узлу и возвращает его. Закрытие без выбора
    /// (id == null) → возвращает null (петля трактует как Aborted). Подменяет <c>AutoFirstNodeChooser</c> в DI.
    /// </summary>
    public sealed class MapScreenNodeChooser : IMapNodeChooser
    {
        private readonly IPublisher<OpenMapRequest> _openMapPub;

        public MapScreenNodeChooser(IPublisher<OpenMapRequest> openMapPub) => _openMapPub = openMapPub;

        public async UniTask<MapNode> ChooseAsync(MapState map, IReadOnlyList<MapNode> available, CancellationToken ct = default)
        {
            var ids = new List<string>(available.Count);
            foreach (var node in available) ids.Add(node.Id);

            var tcs = new UniTaskCompletionSource<string>();
            // ct несётся в запрос: отмена забега («В меню») закрывает экран карты через навигатор (QA #37),
            // а AttachExternalCancellation ниже размотает это ожидание в OperationCanceledException.
            _openMapPub.Publish(new OpenMapRequest(map, ids, id => tcs.TrySetResult(id), ct));

            string chosenId = await tcs.Task.AttachExternalCancellation(ct);
            foreach (var node in available)
                if (node.Id == chosenId) return node;
            return null;
        }
    }
}
