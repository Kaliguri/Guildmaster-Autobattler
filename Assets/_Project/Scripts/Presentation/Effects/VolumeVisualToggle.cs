using UnityEngine;
using UnityEngine.Rendering;
using VContainer;

namespace Guildmaster.Presentation.Effects
{
    /// <summary>
    /// Делает Volume постобработки переключаемым из общего реестра эффектов: можно погасить весь профиль
    /// и сразу увидеть кадр без него. Вешается на объект с <see cref="Volume"/>.
    /// </summary>
    [RequireComponent(typeof(Volume))]
    public sealed class VolumeVisualToggle : MonoBehaviour
    {
        [Tooltip("Имя эффекта в реестре — по нему переключают из консоли (gm_fx_off post.map).")]
        [SerializeField] private string _id = "post.map";

        [Tooltip("Описание для списка gm_fx.")]
        [SerializeField] private string _description = "Постобработка карты (виньетка, грейдинг, bloom, зерно)";

        private VisualToggles _toggles;
        private Volume _volume;

        [Inject]
        public void Construct(VisualToggles toggles) => _toggles = toggles;

        private void Start()
        {
            _volume = GetComponent<Volume>();
            _toggles?.Register(_id, _description, on => { if (_volume != null) _volume.enabled = on; });
        }

        private void OnDestroy() => _toggles?.Unregister(_id);
    }
}
