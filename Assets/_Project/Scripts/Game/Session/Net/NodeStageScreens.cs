using System;
using Guildmaster.Core.Flow;
using Guildmaster.Guild;
using MessagePipe;
using VContainer.Unity;

namespace Guildmaster.Game.Session.Net
{
    /// <summary>
    /// Экраны узла — ОДИН код на обе роли: слушает объявленный шаг узла и показывает то, что на нём.
    /// </summary>
    /// <remarks>
    /// <b>Заведён 08.08.2026 под HARD-правило «хозяин и гость — равные игроки».</b> До него экраны узла
    /// публиковала петля акта, а она собирается только владельцу (<c>ActivityInstaller</c>,
    /// <c>_role == Owner</c>): из восьми экранов узла гость видел ровно один — витрину награды, да и ту
    /// вторым, отдельным путём. Разделение при этом было не решением, а несделанной работой, и игроку
    /// читалось как поломка: у клиента просто нет кнопок.
    /// <para><b>Роль осталась там, где ей и место</b> — в том, кто ОБЪЯВЛЯЕТ шаг: узел ведёт владелец,
    /// у него состояние забега и броски. Показ же одинаков, потому что смотрят оба.</para>
    /// <para><b>Передышка — навигация, а не решение</b> (вердикт Макса: «Каждый нажимает отдельно чисто
    /// для себя»). Поэтому кнопки не голосуют и ничего не ждут: они публикуют тот же запрос смены
    /// режима, что и табы верхней панели.</para>
    /// </remarks>
    public sealed class NodeStageScreens : IStartable, IDisposable
    {
        private readonly INodeStageView _stage;
        private readonly IPublisher<OpenContinueRequest> _continuePub;
        private readonly IPublisher<GoToModeRequest>     _modePub;

        public NodeStageScreens(INodeStageView stage,
                                IPublisher<OpenContinueRequest> continuePub,
                                IPublisher<GoToModeRequest> modePub)
        {
            _stage       = stage;
            _continuePub = continuePub;
            _modePub     = modePub;
        }

        public void Start()
        {
            if (_stage == null) return;

            _stage.Changed += OnStageChanged;

            // Шаг мог быть объявлен ДО нашего рождения: сеанс и его скоупы пересоздаются на каждой
            // смене режима, а узел при этом продолжается. Догоняем текущее состояние, а не ждём смены.
            OnStageChanged(_stage.Current);
        }

        public void Dispose()
        {
            if (_stage != null) _stage.Changed -= OnStageChanged;
        }

        private void OnStageChanged(NodeStageState state)
        {
            if (state.Kind != NodeStageKind.Interlude) return;

            _continuePub?.Publish(new OpenContinueRequest(
                labelKey:    null,
                onContinue:  () => _modePub?.Publish(new GoToModeRequest(RunMode.Map)),
                onFormation: () => _modePub?.Publish(new GoToModeRequest(RunMode.Battle))));
        }
    }
}
