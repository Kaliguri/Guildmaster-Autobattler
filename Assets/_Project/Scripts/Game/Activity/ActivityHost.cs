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
    public sealed class ActivityHost : IDisposable
    {
        private readonly Session.SessionHost _sessions;

        private LifetimeScope _activity;

        public ActivityHost(Session.SessionHost sessions) => _sessions = sessions;

        /// <summary>Идёт ли мероприятие прямо сейчас.</summary>
        public bool IsOpen => _activity != null && _activity.Container != null;

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
        /// Открыть занятие, если его нет, и вернуть владельца боя. Для dev-срезов: «поставь мне
        /// болванчиков» означает «мне нужна арена», а арена — это мероприятие, пусть и пустое.
        /// </summary>
        public Flow.BattleHost EnsureBattleHost()
        {
            if (!IsOpen) Open();
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
        public void Open()
        {
            Close();

            // Заготовку боевого скоупа не тащим сюда руками: она выбрана в мире, а мир — предок сессии,
            // значит внутри мероприятия она видна и так (её резолвит BattleHost).
            _activity = _sessions.CreateChild(new ActivityInstaller(), "[Activity]");
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
