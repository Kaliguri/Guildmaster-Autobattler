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
        private ISubscriber<ActivityChangedEvent> _activitySub;
        private IDisposable _activitySubscription;

        // Показ боя ВНЕ мероприятия (повтор за меню): арена являётся по тому же сигналу, что кадрирует
        // камера — «на сцене идёт бой». Мероприятий у меню нет, а место показать надо (журнал
        // 2026-08-04-battle-on-stage-vs-the-run-clock).
        private BattleStagePresence _stagePresence;

        // Родной облик арены: запоминаем ДО первой пряталки, потому что прячем мы её тем же свопером —
        // и после этого спросить «а какой был настоящий» уже не у кого.
        private string _homeSkin;
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
                              ISubscriber<ActivityChangedEvent> activitySub,
                              Guildmaster.Core.Input.IInputService input,
                              BattleStagePresence stagePresence)
        {
            _revealSub       = revealSub;
            _fadeSub         = fadeSub;
            _testZoneSub     = testZoneSub;
            _battleEndedSub  = battleEndedSub;
            _activitySub     = activitySub;
            _input           = input;
            _stagePresence   = stagePresence;
        }

        private void Start()
        {
            if (_swapper == null)      _swapper = FindAnyObjectByType<ArenaSkinSwapper>();
            if (_digital == null)      _digital = FindAnyObjectByType<ArenaDigitalOverlay>();
            if (_desaturation == null) _desaturation = FindAnyObjectByType<ArenaDesaturation>();

            _revealSubscription   = _revealSub?.Subscribe(OnReveal);
            _fadeSubscription     = _fadeSub?.Subscribe(e => _curtain = e.Progress);
            _testZoneSubscription = _testZoneSub?.Subscribe(e => OnTestZone(e.Active));
            // Бой кончился — полигон, в который вернётся игрок, уже другое место (та самая арена, где всё
            // произошло). Возврат туда достоин перехода, в отличие от простого щелчка табом.
            _battleEndedSubscription = _battleEndedSub?.Subscribe(_ => _placeChanged = true);
            _activitySubscription = _activitySub?.Subscribe(OnActivityChanged);

            if (_input != null) _input.SkipRequested += OnSkip;

            // Вне мероприятия арены НЕТ. Прежде мир стартовал с готовой цветной ареной, и игрок видел её
            // мельком ещё в главном меню — место без повода (наход. Макса 02.08.2026). Место появляется
            // тогда, когда во что-то играют, и появляется на глазах.
            _homeSkin = _swapper != null ? _swapper.CurrentSkinId : null;
            HideArena();

            // Показ боя вне мероприятия (повтор за меню): арену являем/прячем по тому же сигналу, что
            // кадрирует камеру. Мероприятие своим путём (OnActivityChanged/зона) арену не трогает —
            // повторы его не поднимают, конфликта нет.
            if (_stagePresence != null)
            {
                _stagePresence.Changed += OnStagePresenceChanged;
                if (_stagePresence.OnStage) ShowHomeArena();
            }
        }

        private void OnDestroy()
        {
            _revealSubscription?.Dispose();
            _fadeSubscription?.Dispose();
            _testZoneSubscription?.Dispose();
            _battleEndedSubscription?.Dispose();
            _activitySubscription?.Dispose();
            if (_input != null) _input.SkipRequested -= OnSkip;
            if (_stagePresence != null) _stagePresence.Changed -= OnStagePresenceChanged;
        }

        // Бой встал на сцену (повтор) — являем родное место цветным; ушёл — убираем целиком.
        private void OnStagePresenceChanged()
        {
            if (_stagePresence == null) return;
            if (_stagePresence.OnStage) ShowHomeArena();
            else HideArena();
        }

        /// <summary>
        /// Явить родную арену сразу и цветной — для показа боя вне мероприятия (повтор). Без цифрового
        /// перехода: бой уже идёт, место просто есть. Включает рендер (<c>SetVisible</c>), возвращает
        /// тайлы родного облика и снимает серость.
        /// </summary>
        private void ShowHomeArena()
        {
            _desaturation?.SetVisible(true);
            if (_swapper != null && !string.IsNullOrEmpty(_homeSkin)) _swapper.ApplyInstant(_homeSkin);
            _desaturation?.SetGrey(false);
            _spawned = true;
        }

        /// <summary>
        /// Мероприятие сменилось. Место принадлежит МЕРОПРИЯТИЮ, а не миру: кончилось оно — кончилось и
        /// место, вместе со всем, что показ о нём помнил.
        /// </summary>
        /// <remarks>
        /// Из-за того, что «арена уже собрана» жило в компоненте persist-мира и не сбрасывалось никогда,
        /// второй заход на Ристалище проходил молча: сборка играется только первый раз за жизнь мира, а
        /// мир у нас один на всю сессию (наход. Макса 02.08.2026).
        /// <para>Забег получает место сразу и без подачи: переход там играет вход в узел, которому есть
        /// что рассказать. Площадка являет себя сама, когда встаёт серая зона.</para>
        /// </remarks>
        private void OnActivityChanged(ActivityChangedEvent e)
        {
            if (!e.IsOpen)
            {
                _placeChanged = false;
                _pending      = false;
                HideArena();   // «собрано» гасит она сама — владелец флага один
                return;
            }

            if (e.Setup.Kind == ActivityKind.Campaign)
            {
                _desaturation?.SetVisible(true);
                if (!string.IsNullOrEmpty(_homeSkin)) _swapper?.ApplyInstant(_homeSkin);
                _desaturation?.SetGrey(false);
            }
        }

        /// <summary>Убрать место с экрана: пустой облик и никаких тайлов. Не «серая арена», а ничего.</summary>
        /// <remarks>
        /// <b>Серость снимается вместе с тайлами, и это не косметика.</b> Вход на площадку сравнивает
        /// «просят серую» с «сейчас серая» и при совпадении не делает НИЧЕГО — считает, что мир уже в
        /// нужном виде. Оставь мы флаг поднятым, второй заход упёрся бы ровно в это: тайлов нет,
        /// собирать их никто не станет, а на экране останутся только декор и зоны расстановки (наход.
        /// Макса 02.08.2026). Место убрано целиком — значит и серость убрана.
        /// <para><b>«Собрано» гасится здесь же, и это единственное место, где ему можно верить.</b>
        /// Флаг отвечает на вопрос «стоят ли тайлы», а тайлы сносит ровно этот метод — кто бы его ни
        /// позвал. Пока сброс жил в одной только смене мероприятия, бой за главным меню поднимал флаг
        /// собой (<see cref="ShowHomeArena"/>), уход из меню сносил тайлы, а вход на Ристалище видел
        /// «уже собрано» и ограничивался покраской пустоты: площадка вставала без арены вовсе
        /// (наход. Макса 04.08.2026, прогон кооп).</para>
        /// </remarks>
        private void HideArena()
        {
            _spawned = false;
            _desaturation?.SetGrey(false);
            // Трава и камни — отдельные спрайты, подмену облика они переживают. «Места нет» означает и
            // их тоже, иначе на пустом поле остаётся висеть один декор.
            _desaturation?.SetVisible(false);

            if (_swapper == null) return;

            EnsureEmptySkin();
            _swapper.ApplyInstant(EmptySkinId);
        }

        /// <summary>Зарегистрировать пустой облик (все слои без тайлов), если его ещё нет.</summary>
        private void EnsureEmptySkin()
        {
            var empty = new System.Collections.Generic.Dictionary<string,
                System.Collections.Generic.Dictionary<Vector3Int, UnityEngine.Tilemaps.TileBase>>();
            foreach (string layer in _swapper.LayerNames)
                empty[layer] = new System.Collections.Generic.Dictionary<Vector3Int, UnityEngine.Tilemaps.TileBase>();

            _swapper.RegisterSkin(EmptySkinId, empty);
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
            // Место снова показываем ДО сборки: рендереры включены, но рисовать им пока нечего — пол
            // пуст, а декор придержит reveal ниже. Он и проявит траву по тем же клеткам, что и пол.
            _desaturation?.SetVisible(true);

            if (_swapper == null) { _desaturation?.SetGrey(true); return; }

            // Домой возвращаемся к ЗАПОМНЕННОМУ облику, а не к текущему: текущий сейчас пустой — мы сами
            // его таким сделали, пока мероприятия не было.
            string home = !string.IsNullOrEmpty(_homeSkin) ? _homeSkin : _swapper.CurrentSkinId;

            EnsureEmptySkin();

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
            _desaturation?.SetVisible(true); // являем место — значит, оно вообще должно рисоваться

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
