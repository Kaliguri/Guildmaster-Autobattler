using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Presentation.Arena;
using MessagePipe;
using UnityEngine;
using VContainer;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Облик тест-зоны: полигон — это серая версия ТОЙ ЖЕ локации. Перекраска идёт процедурно
    /// (<see cref="ArenaDesaturation"/>), а не подменой на нарисованный серый дубль, поэтому переживёт
    /// любую новую арену.
    /// <para>Смена облика прячется под коротким цифровым всполохом (<see cref="ArenaDigitalOverlay.Blink"/>):
    /// голубой шейдер — язык ПЕРЕХОДА, само состояние показывает цвет арены. Держать цифру постоянно
    /// нельзя — на экране, где игрок стоит минутами, это вечная анимация (находка Макса на play-QA).</para>
    /// <para>Слушает СОСТОЯНИЕ (<see cref="TestZoneChangedEvent.Active"/>), а не тумблер: самотог на каждый
    /// бродкаст расходился с владельцем, если тот бродкаст игнорировал (QA #28/#31).</para>
    /// </summary>
    public sealed class TestZoneArenaSkin : MonoBehaviour
    {
        [Tooltip("Цифровой слой арены (всполох на переходе). Пусто — найдём в сцене сами.")]
        [SerializeField] private ArenaDigitalOverlay _digital;

        [Tooltip("Обесцвечивание арены. Пусто — найдём в сцене сами.")]
        [SerializeField] private ArenaDesaturation _desaturation;

        private ISubscriber<TestZoneChangedEvent> _sub;
        private IDisposable _subscription;

        [Inject]
        public void Construct(ISubscriber<TestZoneChangedEvent> sub) => _sub = sub;

        private void Start()
        {
            if (_digital == null)      _digital = FindFirstObjectByType<ArenaDigitalOverlay>();
            if (_desaturation == null) _desaturation = FindFirstObjectByType<ArenaDesaturation>();

            if (_desaturation == null)
            {
                Debug.LogWarning("[TestZoneArenaSkin] - обесцвечивание не найдено → полигон будет неотличим от арены.");
                return;
            }

            _subscription = _sub?.Subscribe(e => Apply(e.Active));
            _desaturation.SetGrey(false); // старт — настоящая арена в своём цвете
        }

        // Перекраска происходит В ПИКЕ всполоха, под цифрой: смена цвета не должна быть видна как щелчок.
        private void Apply(bool testZone)
        {
            if (_desaturation.IsGrey == testZone) return;

            if (_digital != null) _digital.Blink(() => _desaturation.SetGrey(testZone));
            else                  _desaturation.SetGrey(testZone);
        }

        private void OnDestroy() => _subscription?.Dispose();
    }
}
