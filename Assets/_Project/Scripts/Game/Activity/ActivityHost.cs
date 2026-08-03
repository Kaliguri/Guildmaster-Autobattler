using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Game.Services;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Guildmaster.Game.Activity
{
    /// <summary>
    /// Владелец жизненного цикла Занятия: рождает его скоуп на входе в мероприятие (забег, Ристалище,
    /// PvP, дев-арена) и хоронит на выходе в хаб. Он же — дорога к содержимому занятия для тех, кто
    /// живёт дольше: верхней петли игры и корневого UI.
    /// </summary>
    /// <remarks>
    /// <b>Живёт в корне, а рождает от сессии.</b> Мероприятие идёт внутри сеанса владения состоянием:
    /// иерархия выходит задуманной (Мир → Сессия → Занятие → Бой), и всё, что мероприятие заказывает,
    /// видит и состояние забега, и роль этого клиента. Пережить сеанс, в котором началось, оно не может
    /// по построению.
    /// <para><b>Наружу отдаём узкие фасады, а не резолвер.</b> Верхней петле нужен раннер акта, UI —
    /// часы боя. Отдать контейнер значило бы разрешить кому угодно дотянуться до чего угодно в чужой
    /// жизни и получить ссылку, которая протухнет вместе с занятием.</para>
    /// </remarks>
    public sealed class ActivityHost : IDisposable, IActivityView
    {
        private readonly Session.SessionHost _sessions;
        // Показу нужен МОМЕНТ смены места, а не факт: арену являют и хоронят по разу.
        private readonly MessagePipe.IPublisher<ActivityChangedEvent> _changedPub;

        private LifetimeScope _activity;
        private ActivitySetup _setup;
        private ProvingGroundsSetupRequest? _pendingRoster;

        public ActivityHost(Session.SessionHost sessions,
                            MessagePipe.IPublisher<ActivityChangedEvent> changedPub)
        {
            _sessions   = sessions;
            _changedPub = changedPub;
        }

        /// <summary>Идёт ли мероприятие прямо сейчас.</summary>
        public bool IsOpen => _activity != null && _activity.Container != null;

        /// <summary>
        /// С чем открыто текущее мероприятие; вне мероприятия — <see cref="ActivityKind.None"/>. Это и
        /// есть ответ интерфейса на вопрос «где мы»: не вывод из наличия забега и состояния арены, а
        /// то, что назвали при входе.
        /// </summary>
        public ActivitySetup Current => IsOpen ? _setup : default;

        /// <summary>Раннер обхода акта текущего занятия; вне занятия — <c>null</c>.</summary>
        public ActRunner Runner => Resolve<ActRunner>();

        /// <summary>Рукопожатие боя текущего занятия; вне занятия — <c>null</c>.</summary>
        public Flow.IBattleSession Battles => Resolve<Flow.IBattleSession>();

        /// <summary>
        /// Владелец боевого скоупа текущего занятия; вне занятия — <c>null</c>. Нужен dev-инструментам:
        /// они живут дольше мероприятий, а работают с боем того, что идёт сейчас.
        /// </summary>
        public Flow.BattleHost Battle => Resolve<Flow.BattleHost>();

        /// <summary>
        /// Открыть Ристалище, если мероприятия нет, и вернуть владельца боя. Для dev-срезов: «поставь
        /// мне болванчиков» означает «мне нужна площадка» — своей дев-арены у нас нет и не будет
        /// (решение Макса 02.08.2026), тест-бои это прегены состава для Ристалища.
        /// </summary>
        public Flow.BattleHost EnsureBattleHost()
        {
            if (!IsOpen) Open(ActivitySetup.ProvingGrounds);
            return Battle;
        }

        /// <summary>
        /// Часы и фаза боя текущего занятия; вне занятия — <c>null</c>. Именно так UI и узнаёт, что
        /// показывать верхнюю панель нечему: не по фазе <c>None</c> у вечного объекта, а по отсутствию
        /// самого мероприятия.
        /// </summary>
        public IBattleClock Clock => Resolve<IBattleClock>();

        /// <summary>Гейт готовности и источник намерений — из них собирается контекст обхода акта.</summary>
        public Flow.IReadyGate ReadyGate => Resolve<Flow.IReadyGate>();

        public Flow.IPlayerIntentSource Intents => Resolve<Flow.IPlayerIntentSource>();

        /// <summary>Показ награды после узла; вне занятия наград не бывает.</summary>
        public Flow.IRewardPresenter Rewards => Resolve<Flow.IRewardPresenter>();

        /// <summary>Применение последствий текстовых ивентов к состоянию забега.</summary>
        public Flow.EventEffectApplier EventEffects => Resolve<Flow.EventEffectApplier>();

        /// <summary>
        /// Открыть мероприятие. Прошлое закрывается: двух занятий одновременно не бывает — они
        /// взаимоисключающи по построению (двор, забег, Ристалище — это одна и та же арена).
        /// </summary>
        /// <summary>
        /// Заказать состав для БЛИЖАЙШЕГО входа на площадку. Нужен тем, кто хочет площадку с готовым
        /// составом, но входом не владеет: дев-команда просит, а открывает площадку верхний цикл игры —
        /// ему ещё меню закрывать.
        /// </summary>
        /// <remarks>
        /// Заказ одноразовый и снимается тем входом, которому достался: следующая площадка встаёт своим
        /// раскладом, а не тем, что кто-то заказал полчаса назад.
        /// </remarks>
        public void OrderGroundsRoster(ProvingGroundsSetupRequest roster) => _pendingRoster = roster;

        public void Open(ActivitySetup setup)
        {
            Close();

            // Заказ, сделанный до входа, применяем здесь и тут же забываем.
            if (setup.Kind == ActivityKind.ProvingGrounds && setup.Roster == null && _pendingRoster != null)
                setup = ActivitySetup.GroundsWith(_pendingRoster.Value);
            _pendingRoster = null;

            _setup = setup;

            // Заготовку боевого скоупа не тащим сюда руками: она выбрана в мире, а мир — предок сессии,
            // значит внутри мероприятия она видна и так (её резолвит BattleHost).
            // Роль сеанса выбирает состав мероприятия: у гостя нет ведения акта — акт ведёт владелец.
            // Сеанса нет вовсе — CreateChild скажет об этом сам и вернёт null.
            Session.SessionRole role = _sessions.Context?.Role ?? Session.SessionRole.Owner;
            _activity = _sessions.CreateChild(new ActivityInstaller(setup, role), $"[Activity] {setup.Kind}");

            // Площадка открывается ВМЕСТЕ с ареной. Владелец расстановки живёт в боевом скоупе, и без
            // него на Ристалище некому ответить ни на заказ состава, ни на «включи серую зону» — интент
            // улетал бы в пустоту, а игрок видел бы пустой экран без панели (наход. Макса 02.08.2026).
            // Забег арену не поднимает: там её заказывает узел, и до первого узла её быть не должно.
            if (setup.Kind == ActivityKind.ProvingGrounds) Battle?.OpenEmpty();

            _changedPub?.Publish(new ActivityChangedEvent(setup));
        }

        /// <summary>
        /// Закрыть мероприятие: петля, узлы, награды и идущий бой уходят вместе с ним. Ничего
        /// «возвращать в исходное» перед этим не надо — в том и смысл границы.
        /// </summary>
        public void Close()
        {
            if (_activity == null) return;

            _activity.Dispose();
            _activity = null;
            _setup    = default; // мероприятия нет — и вида нет; «где мы» не переживает своё место

            // Место кончилось. Показ обязан узнать об этом, иначе он запомнит арену как «уже собранную»
            // и второй заход на ту же площадку пройдёт без единого перехода.
            _changedPub?.Publish(new ActivityChangedEvent(default));
        }

        public void Dispose() => Close();

        private T Resolve<T>() where T : class
        {
            if (!IsOpen) return null;
            return _activity.Container.TryResolve(out T value) ? value : null;
        }
    }

    /// <summary>
    /// Префаб боевого скоупа, выбранный в мире. Обёртка, потому что регистрировать «голый»
    /// <see cref="CombatLifetimeScope"/> нельзя: это тип самого скоупа, и такая регистрация читалась бы
    /// как «в контейнере лежит живой боевой скоуп», хотя лежит заготовка для рождения.
    /// </summary>
    public sealed class BattleScopePrefab
    {
        public readonly CombatLifetimeScope Value;

        public BattleScopePrefab(CombatLifetimeScope value) => Value = value;
    }
}
