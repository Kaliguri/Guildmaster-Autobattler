using System;
using Guildmaster.Combat;
using Guildmaster.Core.Audio;
using Guildmaster.Core.Players;
using Guildmaster.Data.Definitions;
using VContainer.Unity;

namespace Guildmaster.Presentation.Audio
{
    /// <summary>
    /// Аудио-презентер (вики impl «09» §П4): POCO-entry-point, подписан НАПРЯМУЮ на C#-события боевой
    /// симуляции, системы способностей и системы эффектов (тот же приём, что <c>CombatFeelDirector</c>).
    /// Резолвит ключ <c>{contentId}.{action}</c> через <see cref="AudioResolver"/> и отдаёт в
    /// <see cref="IAudioService"/>. Точечные звуки реликвий/эффектов (<c>relic.cryomancer.attack</c>,
    /// <c>effect.frozen.apply</c>) подхватываются резолвером автоматически — достаточно выстрелить нужным
    /// действием с нужным id.
    ///
    /// Чего тут НЕТ намеренно: feel-слой (килл-стингер, тяжёлый удар, финишер) живёт в
    /// <c>CombatFeelDirector</c> — там уже посчитаны пороги и кулдауны, и звук обязан идти под теми же
    /// воротами, что slowmo/тряска. Экраны и карта — в <c>RunAudioPresenter</c> (root-скоуп, переживает бой).
    /// </summary>
    public sealed class AudioPresenter : IStartable, IDisposable
    {
        private readonly IAudioService _audio;
        private readonly AudioResolver _resolver;
        private readonly CombatSimulation _sim;
        private readonly AbilitySystem _abilities;
        private readonly EffectSystem _effects;
        private readonly ILocalPlayer _localPlayer;

        public AudioPresenter(
            IAudioService audio,
            AudioCatalog catalog,
            CombatSimulation sim,
            AbilitySystem abilities,
            EffectSystem effects,
            ILocalPlayer localPlayer)
        {
            _audio = audio;
            _resolver = new AudioResolver(catalog);
            _sim = sim;
            _abilities = abilities;
            _effects = effects;
            _localPlayer = localPlayer;
        }

        public void Start()
        {
            _sim.OnDamageDealt      += OnDamageDealt;
            _sim.OnUnitDied         += OnUnitDied;
            _sim.OnHealed           += OnHealed;
            _sim.OnAttackEvaded     += OnAttackEvaded;
            _sim.OnAttackStarted    += OnAttackStarted;
            _sim.OnProjectileSpawned += OnProjectileSpawned;
            _sim.OnBattleEnded      += OnBattleEnded;
            _sim.OnUnitSpawned      += OnUnitSpawned;
            _sim.OnAttackInterrupted += OnAttackInterrupted;
            _sim.OnBattleReset      += OnBattleReset;
            if (_abilities != null) _abilities.OnAbilityCast += OnAbilityCast;
            if (_effects != null)
            {
                _effects.OnEffectApplied += OnEffectApplied;
                _effects.OnEffectEnded   += OnEffectEnded;
            }
        }

        public void Dispose()
        {
            _sim.OnDamageDealt      -= OnDamageDealt;
            _sim.OnUnitDied         -= OnUnitDied;
            _sim.OnHealed           -= OnHealed;
            _sim.OnAttackEvaded     -= OnAttackEvaded;
            _sim.OnAttackStarted    -= OnAttackStarted;
            _sim.OnProjectileSpawned -= OnProjectileSpawned;
            _sim.OnBattleEnded      -= OnBattleEnded;
            _sim.OnUnitSpawned      -= OnUnitSpawned;
            _sim.OnAttackInterrupted -= OnAttackInterrupted;
            _sim.OnBattleReset      -= OnBattleReset;
            if (_abilities != null) _abilities.OnAbilityCast -= OnAbilityCast;
            if (_effects != null)
            {
                _effects.OnEffectApplied -= OnEffectApplied;
                _effects.OnEffectEnded   -= OnEffectEnded;
            }
        }

        // Импакт по цели: щит-поглощение (если было) + удар. Килл-стингер — забота CombatFeelDirector.
        private void OnDamageDealt(RuntimeUnit source, RuntimeUnit target, DamageResult result)
        {
            if (result.ShieldDamage > 0f) PlayFor(target, AudioAction.Shield);
            PlayFor(target, AudioAction.Hit);
        }

        private void OnUnitDied(RuntimeUnit unit) => PlayFor(unit, AudioAction.Death);

        private void OnHealed(RuntimeUnit source, RuntimeUnit target, float amount) => PlayFor(target, AudioAction.Heal);

        private void OnAttackEvaded(RuntimeUnit target) => PlayFor(target, AudioAction.Evade);

        private void OnAttackStarted(RuntimeUnit source, RuntimeUnit target) => PlayFor(source, AudioAction.Attack);

        private void OnProjectileSpawned(Projectile projectile) => PlayFor(projectile?.Source, AudioAction.Fire);

        private void OnAbilityCast(RuntimeUnit caster) => PlayFor(caster, AudioAction.Cast);

        private void OnUnitSpawned(RuntimeUnit unit) => PlayKeyAt("combat.unit_spawn", AudioAction.Ui, unit);

        // Замах сорван станом/смертью — короткий «сбой», иначе оборванная анимация выглядит багом.
        private void OnAttackInterrupted(RuntimeUnit unit) => PlayKeyAt("combat.attack_interrupted", AudioAction.Evade, unit);

        // Перезапуск боя (dev-R): глушим петли, иначе хвосты старого боя переезжают в новый.
        private void OnBattleReset() => _audio?.StopAll();

        // Статус лёг / спал: ключи effect.{id}.apply и effect.{id}.expire, с фолбэком на общий дефолт.
        private void OnEffectApplied(RuntimeUnit target, EffectData def, RuntimeUnit source)
            => PlayKeyAt(def != null ? def.Id : null, AudioAction.Apply, target);

        private void OnEffectEnded(RuntimeUnit target, EffectData def, RuntimeUnit source)
            => PlayKeyAt(def != null ? def.Id : null, AudioAction.Expire, target);

        // Конец боя → стингер победы/поражения ГЛАЗАМИ ЭТОГО клиента: победила моя команда или нет.
        // В PvP один и тот же исход даст одному победу, другому поражение. Ничья — поражение (никто не выиграл).
        private void OnBattleEnded(BattleOutcome outcome)
        {
            if (outcome.IsOngoing) return;
            PlayKey(outcome.IsWinFor(_localPlayer.Team) ? "battle.victory" : "battle.defeat", AudioAction.Stinger);
        }

        // Звук боевого события идёт ИЗ ТОЧКИ, где оно случилось: удар слева слышно слева.
        // Позицию берём из сима (она же ведёт вид), а не из вью — вью может отставать на кадр
        // интерполяции, и на быстрых смещениях звук уезжал бы за картинкой.
        private void PlayFor(RuntimeUnit unit, AudioAction action)
        {
            string key = _resolver.Resolve(unit?.Unit != null ? unit.Unit.Id : null, action);
            if (key == null) return;
            if (unit != null) _audio.PlayAt(key, unit.Position);
            else _audio.Play(key);
        }

        private void PlayKey(string contentId, AudioAction action)
        {
            string key = _resolver.Resolve(contentId, action);
            if (key != null) _audio.Play(key);
        }

        private void PlayKeyAt(string contentId, AudioAction action, RuntimeUnit at)
        {
            string key = _resolver.Resolve(contentId, action);
            if (key == null) return;
            if (at != null) _audio.PlayAt(key, at.Position);
            else _audio.Play(key);
        }
    }
}
