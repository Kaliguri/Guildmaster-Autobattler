using System;
using Guildmaster.Data.Definitions;
using MessagePipe;
using UnityEngine;
using VContainer;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// «Серая зона» тест-арены (QA #2): держит два корня пола — цветной (боевая арена) и grayscale-дубль
    /// (те же тайлы Cainos в серых версиях) — и свапает их по СОСТОЯНИЮ тест-зоны (<see cref="TestZoneChangedEvent"/>).
    /// Вне тест-зоны — цветной; в тест-зоне — серый (визуальный маркер «полигон, не настоящий бой»). Живёт в
    /// WorldScene (persist); подписку инъектит <c>WorldLifetimeScope</c>.
    /// <para>Ф5: слушает СОСТОЯНИЕ (Active), а не тумблер — прежний самотог (<c>SetGray(!_gray)</c>) на каждый
    /// бродкаст расходился с владельцем, если тот бродкаст игнорировал (QA #28/#31). Теперь источник один.</para>
    /// </summary>
    public sealed class TestZoneArenaSkin : MonoBehaviour
    {
        [Tooltip("Корень цветной боевой арены (тайлмапы Cainos). Виден вне тест-зоны.")]
        [SerializeField] private GameObject _colorRoot;

        [Tooltip("Корень grayscale-дубля (те же тайлмапы с серыми тайлами). Виден в тест-зоне.")]
        [SerializeField] private GameObject _grayRoot;

        private ISubscriber<TestZoneChangedEvent> _sub;
        private IDisposable _subscription;

        [Inject]
        public void Construct(ISubscriber<TestZoneChangedEvent> sub) => _sub = sub;

        private void Start()
        {
            _subscription = _sub?.Subscribe(e => SetGray(e.Active)); // состояние, не тумблер
            SetGray(false); // старт — цветная арена
        }

        private void OnDestroy() => _subscription?.Dispose();

        private void SetGray(bool gray)
        {
            if (_colorRoot != null) _colorRoot.SetActive(!gray);
            if (_grayRoot  != null) _grayRoot.SetActive(gray);
        }
    }
}
