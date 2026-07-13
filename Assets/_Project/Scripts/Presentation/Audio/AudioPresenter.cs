using System;
using Guildmaster.Combat;
using Guildmaster.Core.Audio;
using MessagePipe;
using VContainer.Unity;

namespace Guildmaster.Presentation.Audio
{
    /// <summary>
    /// Аудио-презентер (вики impl «09» §П3): POCO-entry-point (как <c>CombatFeelDirector</c>), подписывается на
    /// боевые MessagePipe-события, резолвит ключ через <see cref="AudioResolver"/> и отдаёт в
    /// <see cref="IAudioService"/>. Развязан от симуляции; не MonoBehaviour — не нужен объект в сцене,
    /// регистрируется в <c>CombatLifetimeScope</c>. Attack/cast и прочие появятся, когда их события
    /// пробросят в MessagePipe (П4) — пока доступны hit (урон) и death.
    /// </summary>
    public sealed class AudioPresenter : IStartable, IDisposable
    {
        private readonly IAudioService _audio;
        private readonly AudioResolver _resolver;
        private readonly ISubscriber<DamageDealtEvent> _damageSub;
        private readonly ISubscriber<UnitDiedEvent> _diedSub;
        private IDisposable _subscriptions;

        public AudioPresenter(
            IAudioService audio,
            AudioCatalog catalog,
            ISubscriber<DamageDealtEvent> damageSub,
            ISubscriber<UnitDiedEvent> diedSub)
        {
            _audio = audio;
            _resolver = new AudioResolver(catalog);
            _damageSub = damageSub;
            _diedSub = diedSub;
        }

        public void Start()
        {
            var bag = DisposableBag.CreateBuilder();
            _damageSub.Subscribe(OnDamageDealt).AddTo(bag);
            _diedSub.Subscribe(OnUnitDied).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose() => _subscriptions?.Dispose();

        private void OnDamageDealt(DamageDealtEvent e) => PlayFor(e.Target, AudioAction.Hit);

        private void OnUnitDied(UnitDiedEvent e) => PlayFor(e.Unit, AudioAction.Death);

        private void PlayFor(RuntimeUnit unit, AudioAction action)
        {
            string contentId = unit?.Unit != null ? unit.Unit.Id : null;
            string key = _resolver.Resolve(contentId, action);
            if (key != null) _audio.Play(key);
        }
    }
}
