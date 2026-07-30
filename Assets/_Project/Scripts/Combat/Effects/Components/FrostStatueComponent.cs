using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Верхняя ступень холодной линии — статуя (карточка [[frost]] §Обратился в лёд). Оглушение даёт
    /// <see cref="ControlComponent"/> на том же эффекте, а этот компонент отвечает за три вещи, которые
    /// принадлежат именно окну: <b>хрупкость</b> (+к дробящему и к льду), <b>Раскол</b> (физический удар
    /// разбивает статую) и <b>обнуление «Изморози»</b> по выходу из окна.
    /// <para><b>Числа:</b> <c>_bluntVuln</c> — насколько больнее дробящий по статуе (0.2 = +20%);
    /// <c>_iceVuln</c> — то же для льда, пока цель оглушена; <c>_shatterPctMaxHp</c> — чистый урон Раскола
    /// долей от максимума HP цели (0.15); <c>_shatterStun</c> — секундное оглушение поверх Раскола.</para>
    /// <para><b>Когда срабатывает:</b> в pre-damage (хрупкость и Раскол) и на истечении (обнуление).</para>
    /// </summary>
    /// <remarks>
    /// <b>Раскол — правило линии, а не свойство кита.</b> Он живёт здесь, на самой статуе, поэтому статую
    /// разбивает ЛЮБОЙ физический дилер, и ни одному киту не нужно про Раскол знать. Условие «только пока
    /// цель оглушена» выполняется само собой: нет статуи — нет и компонента.
    /// <para><b>Секундное оглушение Раскола — предохранитель развилки</b> (Макс, 2026-07-29). Без него
    /// разбить статую в первую десятую долю секунды означало бы выкинуть почти весь заработанный контроль,
    /// и правильным ответом всегда было бы ждать. С ним обмен честен: до двух секунд контроля меняются на
    /// одну гарантированную плюс чистый бурст.</para>
    /// <para><b>Известное отступление от карточки:</b> хвост уязвимости к льду («+20% ещё две секунды после
    /// окна») не реализован — пока цель статуя, действует полная прибавка, после обнуления не остаётся
    /// ничего. Отдельный эффект-хвост завести дешевле, чем кажется, но он не нужен ни одному текущему киту.</para>
    /// </remarks>
    [Serializable]
    public sealed class FrostStatueComponent : IPreDamageComponent
    {
        [Tooltip("Прибавка к получаемому ДРОБЯЩЕМУ урону, пока цель — статуя (0.2 = +20%). Лёд делает цель хрупкой.")]
        [SerializeField] private float _bluntVuln = 0.2f;

        [Tooltip("Прибавка к получаемому урону ЛЬДОМ, пока цель оглушена (0.4 = +40%).")]
        [SerializeField] private float _iceVuln = 0.4f;

        [Tooltip("Раскол: чистый урон долей от МАКСИМУМА HP цели (0.15 = 15%). 0 = Раскол выключен.")]
        [SerializeField] private float _shatterPctMaxHp = 0.15f;

        [Tooltip("Оглушение, которое Раскол выдаёт вместо прерванного окна (обычный стан, тир 2).")]
        [SerializeField] private EffectData _shatterStun;

        public void OnApply(in EffectContext ctx) { }

        /// <summary>
        /// Окно закрылось: «Изморозь» обнуляется. Так велит карточка — без обнуления две крио-реликвии
        /// держали бы цель в бесконечном цикле «докинули до капа → стан → докинули», а diminishing returns
        /// у нас нет. Обнуление и есть встроенное окно уязвимости атакующего.
        /// </summary>
        public void OnExpire(in EffectContext ctx)
        {
            RuntimeUnit target = ctx.Target;
            if (target == null) return;

            ctx.Combat.Dispel(new DispelRequest(
                target, DispelTargetPolarity.Any, EffectTag.Frostbite, int.MaxValue, 0, ctx.Source));
        }

        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            if (result.Negated) return;

            RuntimeUnit target = ctx.Target;
            if (target == null || target.IsDead) return;

            // Хрупкость: множители к самому удару, который сейчас прилетит.
            if (_bluntVuln > 0f && incoming.School == DamageSchool.Physical
                && incoming.Subtype == PhysicalSubtype.Blunt)
                result.AddMultiplier(1f + _bluntVuln);

            if (_iceVuln > 0f && incoming.School == DamageSchool.Magical
                && incoming.Element == MagicElement.Ice)
                result.AddMultiplier(1f + _iceVuln);

            // Раскол — только физический ПРЯМОЙ удар: доты и магия статую не колют.
            if (_shatterPctMaxHp <= 0f) return;
            if (!incoming.IsDirectHit || incoming.School != DamageSchool.Physical) return;

            RuntimeUnit breaker = incoming.Source;
            if (breaker == null) return;

            // Порядок важен: сначала бурст (пока статуя ещё цела и множители честно посчитаны), потом
            // подмена контроля. Обратный порядок дал бы Раскол по уже разбитой цели.
            float pure = target.Stats.Get(StatType.MaxHP) * _shatterPctMaxHp;
            if (pure > 0f)
                ctx.Combat.DealDamage(new DamageRequest(
                    breaker, target, pure, DamageSchool.True, ctx.Combat.ArmorK,
                    sourceKind: DamageSourceKind.Ability));

            // Статую гасим её же сроком, а не диспелом: диспел по тегу контроля унёс бы заодно чужие
            // оглушения на этой цели, а адресного снятия у шва нет. Обнуление «Изморози» произойдёт в
            // OnExpire — то есть путь выхода из окна ОДИН, что через таймер, что через Раскол.
            ctx.Effect.EndDuration();

            if (_shatterStun != null) ctx.Combat.ApplyEffect(target, _shatterStun, breaker);
        }
    }
}
