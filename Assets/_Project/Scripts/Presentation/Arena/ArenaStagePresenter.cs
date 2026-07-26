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

        private const string EmptySkinId = "__empty";

        private ISubscriber<ArenaRevealRequest> _revealSub;
        private ISubscriber<ScreenFadeChangedEvent> _fadeSub;
        private ISubscriber<TestZoneChangedEvent> _testZoneSub;
        private ISubscriber<BattleEndedEvent> _battleEndedSub;
        private IDisposable _battleEndedSubscription;
        private Guildmaster.Core.Input.IInputService _input;
        private IDisposable _revealSubscription;
        private IDisposable _fadeSubscription;
        private IDisposable _testZoneSubscription;

        private bool _spawned;   // первый показ полигона играется сборкой из пустоты, дальше — обычным всполохом

        // Место успело смениться с прошлого показа полигона? Переход — это рассказ о СМЕНЕ, и играть его
        // надо там, где сменять есть что: начало забега, заход на узел, конец боя. А щёлканье табами
        // «карта ↔ полигон» ничего не меняет, и мир, каждый раз пересобирающийся заново, читался как сбой.
        private bool _placeChanged;

        private bool   _pending;          // проявление заказано, ждём открытого кадра
        private string _pendingSkin;
        private float  _curtain;          // насколько закрыта шторка: 1 — темнота, 0 — открыто
        private float  _wait;

        [Inject]
        public void Construct(ISubscriber<ArenaRevealRequest> revealSub,
                              ISubscriber<ScreenFadeChangedEvent> fadeSub,
                              ISubscriber<TestZoneChangedEvent> testZoneSub,
                              ISubscriber<BattleEndedEvent> battleEndedSub,
                              Guildmaster.Core.Input.IInputService input)
        {
            _revealSub       = revealSub;
            _fadeSub         = fadeSub;
            _testZoneSub     = testZoneSub;
            _battleEndedSub  = battleEndedSub;
            _input           = input;
        }

        private void Start()
        {
            if (_swapper == null)      _swapper = FindFirstObjectByType<ArenaSkinSwapper>();
            if (_digital == null)      _digital = FindFirstObjectByType<ArenaDigitalOverlay>();
            if (_desaturation == null) _desaturation = FindFirstObjectByType<ArenaDesaturation>();

            _revealSubscription   = _revealSub?.Subscribe(OnReveal);
            _fadeSubscription     = _fadeSub?.Subscribe(e => _curtain = e.Progress);
            _testZoneSubscription = _testZoneSub?.Subscribe(e => OnTestZone(e.Active));
            // Бой кончился — полигон, в который вернётся игрок, уже другое место (та самая арена, где всё
            // произошло). Возврат туда достоин перехода, в отличие от простого щелчка табом.
            _battleEndedSubscription = _battleEndedSub?.Subscribe(_ => _placeChanged = true);

            if (_input != null) _input.SkipRequested += OnSkip;

            _desaturation?.SetGrey(false); // старт — настоящая арена в своём цвете
        }

        private void OnDestroy()
        {
            _revealSubscription?.Dispose();
            _fadeSubscription?.Dispose();
            _testZoneSubscription?.Dispose();
            _battleEndedSubscription?.Dispose();
            if (_input != null) _input.SkipRequested -= OnSkip;
        }

        /// <summary>
        /// Скип подачи. Слушаем ЗДЕСЬ, а не в свопере: подача — это два слоя сразу (подмена тайлов и цифра),
        /// и половина прогонов вообще не меняет облик. Со скипом внутри свопера пропускалась только та
        /// половина, где менялись текстуры, — цветовые прогоны докручивались до конца сами по себе.
        /// </summary>
        private void OnSkip()
        {
            _swapper?.Rush();
            _digital?.Rush();
        }

        /// <summary>
        /// Полигон включили или выключили. Первый вход играется СБОРКОЙ ИЗ ПУСТОТЫ: мир не должен просто
        /// оказаться на экране готовым — он собирается на глазах, клетка за клеткой. Дальше это уже
        /// знакомое место, и хватает короткого всполоха.
        /// </summary>
        private void OnTestZone(bool active)
        {
            if (_desaturation == null || _desaturation.IsGrey == active) return;

            if (active && !_spawned)
            {
                _spawned = true;
                _placeChanged = false;
                SpawnFromNothing();
                return;
            }

            // Полный переход — только если с прошлого раза место действительно сменилось (был бой, был узел).
            // Иначе просто ставим нужный цвет: игрок вернулся туда же, откуда уходил, и пересказывать ему
            // это заново незачем.
            if (active && _placeChanged)
            {
                _placeChanged = false;
                SweepColour(true);
                return;
            }

            _desaturation.SetGrey(active);
        }

        /// <summary>
        /// Смена цвета арены полным переходом: все три акта, цвет возвращается ПОКЛЕТОЧНО вслед за цифрой.
        /// Короткий всполох с мгновенной перекраской в середине был ошибкой ритма — договаривались, что
        /// дорога из полигона в настоящее место занимает время, а не мгновение.
        /// </summary>
        private void SweepColour(bool grey)
        {
            if (_digital == null) { _desaturation.SetGrey(grey); return; }

            _digital.Sweep();
            _desaturation.SweepGrey(grey, _digital);
        }

        /// <summary>
        /// Сборка арены: облик подменяется на ПУСТОЙ, а затем родной возвращается обычным переходом —
        /// тот же разнобой по клеткам, что и при смене места, только собирать приходится с нуля.
        /// Отдельного механизма спавн не требует: «появиться» — это частный случай «смениться».
        /// <para>Собираемся не из пустого экрана, а в уже стоящий цифровой чертёж места: пустота перед
        /// спавном читалась как сбой загрузки. Декор проявляется по тем же клеткам, что и пол под ним —
        /// иначе трава стоит готовой посреди недостроенного мира.</para>
        /// </summary>
        private void SpawnFromNothing()
        {
            if (_swapper == null) { _desaturation?.SetGrey(true); return; }

            string home = _swapper.CurrentSkinId;

            var empty = new System.Collections.Generic.Dictionary<string,
                System.Collections.Generic.Dictionary<Vector3Int, UnityEngine.Tilemaps.TileBase>>();
            foreach (string layer in _swapper.LayerNames)
                empty[layer] = new System.Collections.Generic.Dictionary<Vector3Int, UnityEngine.Tilemaps.TileBase>();

            _swapper.RegisterSkin(EmptySkinId, empty);

            _desaturation.SetGrey(true);            // цвет полигона ставим сразу: собираться должна уже серая
            if (_digital != null) _digital.OutlineFromTarget = true; // чертёж места стоит с первого кадра
            _swapper.ApplyInstant(EmptySkinId);     // тайлов нет — есть только чертёж
            _swapper.Play(home);                    // и мир достраивается в него

            // Декор вне тайлмапа идёт по тем же клеткам: появляется вместе с полом под собой.
            if (_digital != null) _desaturation.SweepGrey(true, _digital, reveal: true);
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

            // Облик тот же — текстурам меняться не на что. Но полигон возвращает себе цвет, и это тоже
            // смена, которую есть чем растянуть: гоним полный переход, цвет приходит клетка за клеткой.
            if (_desaturation != null && _desaturation.IsGrey) SweepColour(false);
            else                                               _digital?.Blink();
        }
    }
}
