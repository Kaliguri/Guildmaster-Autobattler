using System;
using Guildmaster.Combat;
using Guildmaster.Core.Audio;
using MessagePipe;
using UnityEngine;
using VContainer;

namespace Guildmaster.Presentation.Audio
{
    /// <summary>
    /// Аудио-презентер (вики «13» §7): подписывается на боевые MessagePipe-события (по образцу
    /// <see cref="CombatPresenter"/>), резолвит ключ через <see cref="AudioResolver"/> и отдаёт его в
    /// <see cref="IAudioService"/>. Сейчас <c>IAudioService</c> — заглушка (Debug.Log), звука нет; FMOD
    /// подключится в Фазе 7/9 без правок этого класса. Attack/cast появятся, когда их события пробросят
    /// в MessagePipe — сейчас доступны hit (урон) и death.
    /// </summary>
    public sealed class AudioPresenter : MonoBehaviour
    {
        private IAudioService _audio;
        private AudioResolver _resolver;
        private ISubscriber<DamageDealtEvent> _damageSub;
        private ISubscriber<UnitDiedEvent> _diedSub;
        private IDisposable _subscriptions;

        [Inject]
        public void Construct(
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

        private void OnEnable()
        {
            if (_damageSub == null || _diedSub == null) return;
            var bag = DisposableBag.CreateBuilder();
            _damageSub.Subscribe(OnDamageDealt).AddTo(bag);
            _diedSub.Subscribe(OnUnitDied).AddTo(bag);
            _subscriptions = bag.Build();
        }

        private void OnDisable() => _subscriptions?.Dispose();

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
