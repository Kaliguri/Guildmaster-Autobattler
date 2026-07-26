using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Presentation.Arena;
using MessagePipe;
using UnityEngine;
using VContainer;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Облик тест-зоны: полигон — это ТО ЖЕ место, показанное моделью, поэтому арена уходит в цифровой
    /// каркас (<see cref="ArenaDigitalOverlay"/>), а не подменяется серым дублем пола.
    /// <para>Почему не серый: серый — язык интерфейса, «выключено, недоступно», а полигон не сломан. Цифра
    /// говорит верное — «это модель места». Вдобавок она бесплатна: тест-зона оказывается застывшим вторым
    /// актом того же перехода, что играет при входе в боевой узел (вход = акт 1, выход = акт 3).</para>
    /// <para>Слушает СОСТОЯНИЕ (<see cref="TestZoneChangedEvent.Active"/>), а не тумблер: самотог на каждый
    /// бродкаст расходился с владельцем, если тот бродкаст игнорировал (QA #28/#31).</para>
    /// </summary>
    public sealed class TestZoneArenaSkin : MonoBehaviour
    {
        [Tooltip("Цифровой слой арены. Пусто — найдём в сцене сами.")]
        [SerializeField] private ArenaDigitalOverlay _digital;

        private ISubscriber<TestZoneChangedEvent> _sub;
        private IDisposable _subscription;

        [Inject]
        public void Construct(ISubscriber<TestZoneChangedEvent> sub) => _sub = sub;

        private void Start()
        {
            if (_digital == null) _digital = FindFirstObjectByType<ArenaDigitalOverlay>();
            if (_digital == null)
            {
                Debug.LogWarning("[TestZoneArenaSkin] - цифровой слой не найден → тест-зона будет неотличима от арены.");
                return;
            }

            _subscription = _sub?.Subscribe(e => _digital.SetDigital(e.Active));
            _digital.SetDigital(false); // старт — настоящая арена
        }

        private void OnDestroy() => _subscription?.Dispose();
    }
}
