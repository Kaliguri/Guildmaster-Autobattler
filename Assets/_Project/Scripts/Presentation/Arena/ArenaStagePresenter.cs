using System;
using Guildmaster.Core.Flow;
using Guildmaster.Data.Definitions;
using MessagePipe;
using UnityEngine;
using VContainer;

namespace Guildmaster.Presentation.Arena
{
    /// <summary>
    /// Являет место боя, когда игрок вошёл в узел (<see cref="ArenaRevealRequest"/>): снимает серый полигон
    /// и прогоняет цифровой переход. Решает ЗДЕСЬ, а не в боевом потоке: сколько актов играть и когда
    /// начинать — вопрос подачи, и меняться он должен без правок флоу.
    /// <para>Главное — ждать шторку. Вход в узел приходит, пока экран ещё закрыт чернилами (карта ныряет
    /// в точку и гасит кадр); заиграй переход сразу, игрок увидел бы только его хвост. Поэтому старт
    /// откладывается до момента, когда шторка снова открыта.</para>
    /// </summary>
    public sealed class ArenaStagePresenter : MonoBehaviour
    {
        [Tooltip("Свопер обликов. Пусто — найдём в сцене сами.")]
        [SerializeField] private ArenaSkinSwapper _swapper;

        [Tooltip("Цифровой слой. Пусто — найдём в сцене сами.")]
        [SerializeField] private ArenaDigitalOverlay _digital;

        [Tooltip("Обесцвечивание. Пусто — найдём в сцене сами.")]
        [SerializeField] private ArenaDesaturation _desaturation;

        [Tooltip("Пауза после открытия шторки перед проявлением — чтобы кадр успел «сесть».")]
        [SerializeField, Range(0f, 1f)] private float _delayAfterCurtain = 0.12f;

        private ISubscriber<ArenaRevealRequest> _revealSub;
        private ISubscriber<ScreenFadeChangedEvent> _fadeSub;
        private IDisposable _revealSubscription;
        private IDisposable _fadeSubscription;

        private bool   _pending;          // проявление заказано, ждём открытого кадра
        private string _pendingSkin;
        private float  _curtain;          // насколько закрыта шторка: 1 — темнота, 0 — открыто
        private float  _wait;

        [Inject]
        public void Construct(ISubscriber<ArenaRevealRequest> revealSub, ISubscriber<ScreenFadeChangedEvent> fadeSub)
        {
            _revealSub = revealSub;
            _fadeSub   = fadeSub;
        }

        private void Start()
        {
            if (_swapper == null)      _swapper = FindFirstObjectByType<ArenaSkinSwapper>();
            if (_digital == null)      _digital = FindFirstObjectByType<ArenaDigitalOverlay>();
            if (_desaturation == null) _desaturation = FindFirstObjectByType<ArenaDesaturation>();

            _revealSubscription = _revealSub?.Subscribe(OnReveal);
            _fadeSubscription   = _fadeSub?.Subscribe(e => _curtain = e.Progress);
        }

        private void OnDestroy()
        {
            _revealSubscription?.Dispose();
            _fadeSubscription?.Dispose();
        }

        private void Update()
        {
            if (!_pending) return;

            // Пока чернила на экране — ждём. Показывать переход в темноту бессмысленно.
            if (_curtain > 0.01f) { _wait = 0f; return; }

            _wait += Time.unscaledDeltaTime;
            if (_wait < _delayAfterCurtain) return;

            _pending = false;
            Reveal(_pendingSkin);
        }

        private void OnReveal(ArenaRevealRequest request)
        {
            if (request.Instant)
            {
                _desaturation?.SetGrey(false);
                if (!string.IsNullOrEmpty(request.SkinId)) _swapper?.ApplyInstant(request.SkinId);
                _pending = false;
                return;
            }

            _pendingSkin = request.SkinId;
            _pending     = true;
            _wait        = 0f;
        }

        private void Reveal(string skinId)
        {
            bool needsSkinSwap = !string.IsNullOrEmpty(skinId) &&
                                 _swapper != null && skinId != _swapper.CurrentSkinId;

            if (needsSkinSwap)
            {
                // Полный трёхакт: облик места меняется на глазах, а серый снимается под цифрой в середине.
                _swapper.Play(skinId);
                if (_desaturation != null) _digital?.Blink(() => _desaturation.SetGrey(false));
                else                       _digital?.Blink();
                return;
            }

            // Облик тот же — играть подгрузку текстур нечего. Остаётся всполох, под которым полигон
            // возвращает себе цвет: место «оживает» из серой модели в настоящее.
            if (_desaturation != null && _desaturation.IsGrey) _digital?.Blink(() => _desaturation.SetGrey(false));
            else                                               _digital?.Blink();
        }
    }
}
