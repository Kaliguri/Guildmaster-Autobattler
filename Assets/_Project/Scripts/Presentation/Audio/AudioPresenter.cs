using System;
using Guildmaster.Combat;
using Guildmaster.Core.Audio;
using VContainer.Unity;

namespace Guildmaster.Presentation.Audio
{
    /// <summary>
    /// Аудио-презентер (вики impl «09» §П4): POCO-entry-point, подписан НАПРЯМУЮ на C#-события боевой
    /// симуляции и системы способностей (тот же приём, что <c>CombatFeelDirector</c> — он тоже держит
    /// <see cref="CombatSimulation"/>). Резолвит ключ <c>{contentId}.{action}</c> через
    /// <see cref="AudioResolver"/> и отдаёт в <see cref="IAudioService"/>. Точечные звуки реликвий/эффектов
    /// (<c>relic.cryomancer.attack</c>, <c>relic.*.cast</c>) подхватываются резолвером автоматически —
    /// достаточно выстрелить нужным действием на нужном юните.
    /// Пока НЕ озвучены (нужны хуки/id — отдельный заход): эффекты apply/expire с конкретным id, DoT/HoT-тик,
    /// UI (пауза/скорость/расстановка), стингер старта боя, feel-слои (heavy_hit/death_shatter).
    /// </summary>
    public sealed class AudioPresenter : IStartable, IDisposable
    {
        private readonly IAudioService _audio;
        private readonly AudioResolver _resolver;
        private readonly CombatSimulation _sim;
        private readonly AbilitySystem _abilities;

        public AudioPresenter(
            IAudioService audio,
            AudioCatalog catalog,
            CombatSimulation sim,
            AbilitySystem abilities)
        {
            _audio = audio;
            _resolver = new AudioResolver(catalog);
            _sim = sim;
            _abilities = abilities;
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
            if (_abilities != null) _abilities.OnAbilityCast += OnAbilityCast;
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
            if (_abilities != null) _abilities.OnAbilityCast -= OnAbilityCast;
        }

        // Импакт по цели: щит-поглощение (если было) + удар, добивание → килл-стингер поверх.
        private void OnDamageDealt(RuntimeUnit source, RuntimeUnit target, DamageResult result)
        {
            if (result.ShieldDamage > 0f) PlayFor(target, AudioAction.Shield);
            PlayFor(target, AudioAction.Hit);
            if (result.KilledTarget) PlayKey("feel.kill", AudioAction.Stinger);
        }

        private void OnUnitDied(RuntimeUnit unit) => PlayFor(unit, AudioAction.Death);

        private void OnHealed(RuntimeUnit source, RuntimeUnit target, float amount) => PlayFor(target, AudioAction.Heal);

        private void OnAttackEvaded(RuntimeUnit target) => PlayFor(target, AudioAction.Evade);

        private void OnAttackStarted(RuntimeUnit source, RuntimeUnit target) => PlayFor(source, AudioAction.Attack);

        private void OnProjectileSpawned(Projectile projectile) => PlayFor(projectile?.Source, AudioAction.Fire);

        private void OnAbilityCast(RuntimeUnit caster) => PlayFor(caster, AudioAction.Cast);

        // Конец боя → стингер победы/поражения. Допущение: TeamA — сторона игрока (перепроверить, если
        // окажется наоборот, — поменять местами одну строку). Draw/Ongoing — без стингера.
        private void OnBattleEnded(BattleOutcome outcome)
        {
            string key = outcome switch
            {
                BattleOutcome.TeamAWins => "battle.victory",
                BattleOutcome.TeamBWins => "battle.defeat",
                _ => null,
            };
            if (key != null) PlayKey(key, AudioAction.Stinger);
        }

        private void PlayFor(RuntimeUnit unit, AudioAction action)
            => PlayKey(unit?.Unit != null ? unit.Unit.Id : null, action);

        private void PlayKey(string contentId, AudioAction action)
        {
            string key = _resolver.Resolve(contentId, action);
            if (key != null) _audio.Play(key);
        }
    }
}
