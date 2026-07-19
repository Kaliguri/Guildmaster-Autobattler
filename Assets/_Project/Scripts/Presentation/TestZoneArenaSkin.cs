using System;
using Guildmaster.Data.Definitions;
using MessagePipe;
using UnityEngine;
using VContainer;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// «Серая зона» тест-арены (QA #2): держит два корня пола — цветной (боевая арена) и grayscale-дубль
    /// (те же тайлы Cainos в серых версиях) — и свапает их по тумблеру тест-зоны (<see cref="ToggleTestZoneRequest"/>).
    /// Вне тест-зоны — цветной; в тест-зоне — серый (визуальный маркер «полигон, не настоящий бой»). Живёт в
    /// WorldScene (persist); подписку инъектит <c>WorldLifetimeScope</c>.
    /// </summary>
    public sealed class TestZoneArenaSkin : MonoBehaviour
    {
        [Tooltip("Корень цветной боевой арены (тайлмапы Cainos). Виден вне тест-зоны.")]
        [SerializeField] private GameObject _colorRoot;

        [Tooltip("Корень grayscale-дубля (те же тайлмапы с серыми тайлами). Виден в тест-зоне.")]
        [SerializeField] private GameObject _grayRoot;

        private ISubscriber<ToggleTestZoneRequest> _sub;
        private IDisposable _subscription;
        private bool _gray;

        [Inject]
        public void Construct(ISubscriber<ToggleTestZoneRequest> sub) => _sub = sub;

        private void Start()
        {
            _subscription = _sub?.Subscribe(_ => SetGray(!_gray));
            SetGray(false); // старт — цветная арена
        }

        private void OnDestroy() => _subscription?.Dispose();

        private void SetGray(bool gray)
        {
            _gray = gray;
            if (_colorRoot != null) _colorRoot.SetActive(!gray);
            if (_grayRoot  != null) _grayRoot.SetActive(gray);
        }
    }
}
