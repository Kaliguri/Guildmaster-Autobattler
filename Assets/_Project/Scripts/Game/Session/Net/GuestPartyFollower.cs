using System;
using System.Text;
using Guildmaster.Game.Flow;
using Guildmaster.Guild;
using MessagePipe;
using VContainer.Unity;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Гостевая половина показа отряда: ставит бойцов на арену вслед за снимком забега.
    /// </summary>
    /// <remarks>
    /// <b>Зачем понадобился отдельный мост.</b> Отряд на арену кладёт <see cref="WorldStageController"/>,
    /// и зовут его двое: событие «отряд забега готов» из петли акта и закрытие боя. Петли акта у гостя
    /// нет вовсе — он не ведёт забег, — поэтому до первого боя арена у него оставалась пустой: игрок
    /// заходил в чужую кампанию и видел голое поле (наход. Макса 03.08.2026). Состояние при этом
    /// доезжало исправно: <see cref="GuestRunState"/> объявлял <c>SnapshotReceived</c>, но подписчиков
    /// у события не было ни одного.
    /// <para><b>Почему не публикуем «отряд готов» на каждый снимок.</b> На том же событии сидит звук:
    /// оно означает «забег начался» и играет стингер. Снимки же приходят на каждое изменение состояния,
    /// включая перестановку бойца в расстановке, — и стингер зазвучал бы на каждый сдвиг. Поэтому
    /// событие публикуется РОВНО ОДИН раз, на первом снимке, а дальше тела переставляются напрямую.</para>
    /// <para><b>И не на каждый снимок вообще:</b> <c>PlaceParty</c> пересобирает тела заново, а снимок
    /// приходит и когда изменилось золото. Пересборка сверяется с отпечатком отряда — составом,
    /// китами и позициями, то есть ровно тем, что видно на арене.</para>
    /// <para><b>Карта перерисовывается здесь же</b>, и не для красоты: просьба показать её приходит
    /// ДРУГИМ каналом, чем сам забег, и вполне может обогнать данные. Показ это переживает, но кто-то
    /// обязан вернуться к нему, когда данные доехали, — этот кто-то здесь.</para>
    /// </remarks>
    public sealed class GuestPartyFollower : IStartable, IDisposable
    {
        private readonly GuestRunState  _runs;
        private readonly IPartyStage     _stage;
        private readonly IActMapPresence _map;
        private readonly IPublisher<RunPartyReadyEvent> _partyReadyPub;

        private readonly StringBuilder _fingerprint = new StringBuilder(128);

        private bool   _announced;              // «забег начался» объявлено — второй раз не надо
        private string _shown = string.Empty;   // отпечаток того, что сейчас стоит на арене

        public GuestPartyFollower(GuestRunState runs, IPartyStage stage, IActMapPresence map,
                                  IPublisher<RunPartyReadyEvent> partyReadyPub)
        {
            _runs          = runs  ?? throw new ArgumentNullException(nameof(runs));
            _stage         = stage ?? throw new ArgumentNullException(nameof(stage));
            _map           = map;
            _partyReadyPub = partyReadyPub;
        }

        public void Start()
        {
            _runs.SnapshotReceived += HandleSnapshot;

            // Снимок мог доехать раньше, чем родился этот подписчик: приёмник состояния и мы — два
            // разных энтрипоинта, и VContainer запускает их по очереди, а не одновременно.
            if (_runs.Current != null) HandleSnapshot(_runs.Current);
        }

        public void Dispose() => _runs.SnapshotReceived -= HandleSnapshot;

        private void HandleSnapshot(RunState run)
        {
            if (!_announced)
            {
                _announced = true;
                _partyReadyPub?.Publish(new RunPartyReadyEvent()); // он же поставит отряд в первый раз
                _shown = Fingerprint(run);
            }
            else
            {
                string current = Fingerprint(run);
                if (current != _shown)
                {
                    _shown = current;
                    _stage.PlaceParty();
                }
            }

            // Карта перечитывается на КАЖДЫЙ снимок, без отпечатка: в ней меняется не состав, а пройденные
            // узлы и текущее место — то есть ровно то, ради чего гость на неё и смотрит. Ничего не стоит,
            // если карту не просили показывать.
            _map?.Refresh();
        }

        /// <summary>
        /// Отпечаток того, что видно на арене: кто стоит, с каким китом и где. Золото, карта и прогресс
        /// в него не входят намеренно — на телах они никак не отражаются.
        /// </summary>
        private string Fingerprint(RunState run)
        {
            _fingerprint.Clear();
            if (run?.Guild == null) return string.Empty;

            foreach (RosterSlot slot in run.Guild)
            {
                if (slot == null) { _fingerprint.Append('_').Append(';'); continue; }

                _fingerprint.Append(slot.VesselId).Append('|')
                            .Append(slot.RelicId).Append('|')
                            .Append(slot.SavedPosition.x).Append(',').Append(slot.SavedPosition.y)
                            .Append(';');
            }
            return _fingerprint.ToString();
        }
    }
}
