using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Guildmaster.Presentation.Arena
{
    /// <summary>
    /// Помечает корень тайлмап как ОБЛИК арены («скин») с известным id. Источники в сцене выключены —
    /// они не рендерятся, а служат хранилищем тайлов: <see cref="ArenaSkinSwapper"/> снимает с них
    /// снапшот и переносит клетки в живой корень.
    /// <para>Слои сопоставляются ПО ИМЕНИ объекта (<c>Layer 1 - Wall</c>, <c>Layer 1 - Grass</c>): скины —
    /// близнецы по структуре, и имя тут надёжнее порядка в иерархии, который легко переставить мышкой.</para>
    /// </summary>
    public sealed class ArenaSkinSource : MonoBehaviour
    {
        [Tooltip("Идентификатор облика: 'arena' — боевая, остальные — по месту (болото, подземелье...).")]
        [SerializeField] private string _skinId = "arena";

        [Tooltip("Этот корень рендерится и ПРИНИМАЕТ подмены. Ровно один такой на арену.")]
        [SerializeField] private bool _isLive;

        private readonly List<Tilemap> _layers = new List<Tilemap>();
        private bool _collected;

        public string SkinId => _skinId;
        public bool IsLive => _isLive;

        /// <summary>Тайлмап-слои этого облика. Собираются лениво: источник выключен, Awake на нём не придёт.</summary>
        public IReadOnlyList<Tilemap> Layers
        {
            get
            {
                if (!_collected)
                {
                    _layers.Clear();
                    GetComponentsInChildren<Tilemap>(true, _layers);
                    _collected = true;
                }
                return _layers;
            }
        }
    }
}
