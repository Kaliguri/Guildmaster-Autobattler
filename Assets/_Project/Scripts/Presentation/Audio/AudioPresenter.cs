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
    /// <see cref="IAudioService"/>. Точечные звуки мементо/эффектов (<c>relic.cryomancer.attack</c>,
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
        private readonly ILocalPlayer _localPlayer;

        // Звук идёт по ПОКАЗУ: сим ушёл вперёд на окно опережения, и подписка на него давала бы удары,
        // смерти и стингеры за десять секунд до того, как игрок их увидит.
        private readonly Combat.Tape.BattleTapeDispatcher _dispatcher;
        private readonly Combat.Tape.BattleTapePlayback   _playback;
        private readonly Combat.Tape.BattleUnitRegistry   _registry;

        public AudioPresenter(
            IAudioService audio,
            AudioCatalog catalog,
            ILocalPlayer localPlayer,
            Combat.Tape.BattleTapeDispatcher dispatcher,
            Combat.Tape.BattleTapePlayback playback,
            Combat.Tape.BattleUnitRegistry registry)
        {
            _audio = audio;
            _resolver = new AudioResolver(catalog);
            _localPlayer = localPlayer;
            _dispatcher = dispatcher;
            _playback   = playback;
            _registry   = registry;
        }

        public void Start()
        {
            _dispatcher.DamageDealt       += OnDamageDealt;
            _dispatcher.UnitDied          += OnUnitDied;
            _dispatcher.Healed            += OnHealed;
            _dispatcher.AttackEvaded      += OnAttackEvaded;
            _dispatcher.AttackStarted     += OnAttackStarted;
            _dispatcher.BattleEnded       += OnBattleEnded;
            _dispatcher.UnitSpawned       += OnUnitSpawned;
            _dispatcher.AttackInterrupted += OnAttackInterrupted;
            _dispatcher.AbilityCast       += OnAbilityCast;
            _dispatcher.EffectApplied     += OnEffectApplied;
            _dispatcher.EffectEnded       += OnEffectEnded;
        }

        public void Dispose()
        {
            _dispatcher.DamageDealt       -= OnDamageDealt;
            _dispatcher.UnitDied          -= OnUnitDied;
            _dispatcher.Healed            -= OnHealed;
            _dispatcher.AttackEvaded      -= OnAttackEvaded;
            _dispatcher.AttackStarted     -= OnAttackStarted;
            _dispatcher.BattleEnded       -= OnBattleEnded;
            _dispatcher.UnitSpawned       -= OnUnitSpawned;
            _dispatcher.AttackInterrupted -= OnAttackInterrupted;
            _dispatcher.AbilityCast       -= OnAbilityCast;
            _dispatcher.EffectApplied     -= OnEffectApplied;
            _dispatcher.EffectEnded       -= OnEffectEnded;
        }

        // Импакт по цели: щит-поглощение (если было) + удар. Килл-стингер — забота CombatFeelDirector.
        private void OnDamageDealt(int sourceId, int targetId, DamageResult result)
        {
            if (result.ShieldDamage > 0f) PlayFor(targetId, AudioAction.Shield);
            PlayFor(targetId, AudioAction.Hit);
        }

        private void OnUnitDied(int unitId) => PlayFor(unitId, AudioAction.Death);

        private void OnHealed(int sourceId, int targetId, float amount) => PlayFor(targetId, AudioAction.Heal);

        // Источник удара звуку не нужен: уклонение звучит с того, КТО уклонился, а не с того, кто бил.
        private void OnAttackEvaded(int attackerId, int targetId) => PlayFor(targetId, AudioAction.Evade);

        private void OnAttackStarted(int sourceId, int targetId) => PlayFor(sourceId, AudioAction.Attack);

        // Определение каста звуку пока не нужно: ключ строится от юнита. Приходит оно ради показа
        // (CastSource решает, что светится), и здесь просто игнорируется.
        private void OnAbilityCast(int casterId, Data.Definitions.AbilityData _) =>
            PlayFor(casterId, AudioAction.Cast);

        private void OnUnitSpawned(int unitId) => PlayKeyAt("combat.unit_spawn", AudioAction.Ui, unitId);

        // Замах сорван станом/смертью — короткий «сбой», иначе оборванная анимация выглядит багом.
        private void OnAttackInterrupted(int unitId) => PlayKeyAt("combat.attack_interrupted", AudioAction.Evade, unitId);

        // На перезапуск боя здесь глушить нечего: боевой звук — только one-shot'ы, их хвосты доигрывают
        // сами. Петли (музыка, амбиент) принадлежат RunAudioPresenter в root-скоупе, и прежний StopAll
        // сносил именно их — музыка после dev-R не возвращалась, потому что владелец считал её живой.
        // Статус лёг / спал: ключи effect.{id}.apply и effect.{id}.expire, с фолбэком на общий дефолт.
        private void OnEffectApplied(int targetId, EffectData def)
            => PlayKeyAt(def != null ? def.Id : null, AudioAction.Apply, targetId);

        private void OnEffectEnded(int targetId, EffectData def)
            => PlayKeyAt(def != null ? def.Id : null, AudioAction.Expire, targetId);

        // Конец боя → стингер победы/поражения ГЛАЗАМИ ЭТОГО клиента: победила моя команда или нет.
        // В PvP один и тот же исход даст одному победу, другому поражение. Ничья — поражение (никто не выиграл).
        private void OnBattleEnded(BattleOutcome outcome)
        {
            if (outcome.IsOngoing) return;
            PlayKey(outcome.IsWinFor(_localPlayer.Team) ? "battle.victory" : "battle.defeat", AudioAction.Stinger);
        }

        // Звук боевого события идёт ИЗ ТОЧКИ, где оно случилось: удар слева слышно слева. Точка берётся
        // из ПОКАЗАННОГО кадра — позиция живого юнита к этому моменту уехала на окно опережения вперёд,
        // и звук приходил бы оттуда, где юнита ещё не видно.
        private void PlayFor(int unitId, AudioAction action)
        {
            string key = _resolver.Resolve(_registry != null ? _registry.DefinitionOf(unitId)?.Id : null, action);
            if (key == null) return;
            PlayAtShown(key, unitId);
        }

        private void PlayKey(string contentId, AudioAction action)
        {
            string key = _resolver.Resolve(contentId, action);
            if (key != null) _audio.Play(key);
        }

        private void PlayKeyAt(string contentId, AudioAction action, int unitId)
        {
            string key = _resolver.Resolve(contentId, action);
            if (key == null) return;
            PlayAtShown(key, unitId);
        }

        /// <summary>Проиграть из точки, где юнит был в показанном кадре; не нашли — без позиции.</summary>
        private void PlayAtShown(string key, int unitId)
        {
            if (unitId >= 0 && _playback != null && _playback.TryGetFrame(out var frame))
            {
                for (int i = 0; i < frame.Count; i++)
                {
                    if (frame[i].Id != unitId) continue;
                    _audio.PlayAt(key, frame[i].Position);
                    return;
                }
            }
            _audio.Play(key);
        }
    }
}
