using System;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// <b>Блок — общий примитив защиты</b> (дизайн Макса 2026-07-30): юнита атакуют — он поднимает щит
    /// на короткое окно. Pre-damage реактив с зарядами: перед входящим уроном, если выполнен триггер
    /// блока (читается из <c>self.Unit.Ai.PassiveTrigger</c>) и есть готовый заряд, на носителя
    /// накладывается таймированный щит (<see cref="_shieldEffect"/>), который тут же поглощает
    /// триггер-удар. Состояние зарядов — per-effect в <see cref="RuntimeEffect.TryConsumeCharge"/>
    /// (сверка с текущим тиком, без декрементов), как у <see cref="DodgeComponent"/>.
    /// <para><b>Числа:</b> <c>_maxCharges</c> — сколько ударов подряд блок способен встретить;
    /// <c>_internalCooldownSeconds</c> — за сколько восстанавливается ОДИН заряд (заряды тикают
    /// независимо); <c>_shieldEffect</c> — сам щит, его величина и длительность живут в том эффекте.</para>
    /// <para><b>Когда срабатывает:</b> в pre-damage, ДО применения урона — иначе щит не успел бы
    /// поглотить тот самый удар, ради которого поднялся.</para>
    /// </summary>
    /// <remarks>
    /// <b>«Оплот» Защитника — это переопределение Блока числами, а не своя пассивка</b> (вердикт Макса
    /// 2026-07-30). Кит задаёт заряды, перезарядку и сам щит; механика одна на всех, потому что
    /// «поднять щит под удар» понадобится многим, а второй такой же компонент означал бы второго
    /// владельца правила. До 2026-07-31 класс назывался <c>BulwarkComponent</c> — имя кита в имени
    /// примитива и было причиной, по которой блок читался как персональная способность.
    /// <para>Щит намеренно короткий (0.4 с в ассете Оплота), а зарядов несколько: тогда блок гасит
    /// ровно те удары, ради которых поднялся, и его сила определяется величиной щита, а не тем, сколько
    /// ударов успело прилететь за время его жизни (замер 2026-07-26: при 2-секундном щите правка
    /// величины не меняла размен один-на-один вовсе).</para>
    /// <para><b>Блок не будят периодический урон и ответка шипов</b> — только прямые попадания
    /// (автоатака или способность). Иначе горение съедало бы заряды тиками по капле, мимо того удара,
    /// ради которого блок существует.</para>
    /// <para><b>Требует дееспособности</b> (<see cref="IRequiresAgencyComponent"/>): щит носитель
    /// ПОДНИМАЕТ, и оглушённый сделать этого не может (решение Макса 2026-07-29). До этой правки блок
    /// ловил удары сквозь стан и сон, а телеграф показывал поднимающийся щит у юнита, который не владеет
    /// собой.</para>
    /// </remarks>
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceClassName: "BulwarkComponent")]
    public sealed class BlockComponent : IPreDamageComponent, IStackableComponent, IRequiresAgencyComponent
    {
        [Tooltip("Число зарядов щита. Защитник = 2 (заряды восстанавливаются независимо).")]
        [SerializeField] private int _maxCharges = 1;

        [Tooltip("Независимая перезарядка ОДНОГО заряда, сек (стартует ПОСЛЕ срабатывания). Защитник = 5.")]
        [SerializeField] private float _internalCooldownSeconds = 4f;

        [Tooltip("Таймированный щит-эффект, накладываемый на носителя при срабатывании (величина — в его MissingHpShieldComponent).")]
        [SerializeField] private EffectData _shieldEffect;

        public void OnApply(in EffectContext ctx)
        {
            ctx.Effect.ArmCharges(_maxCharges);
        }

        public void OnExpire(in EffectContext ctx) { }

        public void OnStacksChanged(int previousStacks, in EffectContext ctx)
        {
            // Рестак НЕ трогает заряды: их число фиксировано, а per-charge таймеры уже живут в
            // в самом эффекте. Дефолтный OnExpire→OnApply дал бы бесплатный рефилл
            // всех зарядов на каждый стак (та же гоча, что у «Изворотливости»).
        }

        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead || _shieldEffect == null) return;

            // Щит встаёт под ПРЯМОЙ удар — автоатаку или способность. Тики DoT и ответка шипов его
            // не будят: иначе горение съедало бы все заряды тиками по капле, мимо того удара, ради
            // которого «Оплот» существует.
            if (!incoming.IsDirectHit) return;

            if (!TriggerMet(self, in incoming)) return;

            int rechargeTicks = Mathf.Max(1, Mathf.RoundToInt(_internalCooldownSeconds * SimConstants.TickRate));

            // Нет готовых зарядов — удар проходит как есть.
            if (!ctx.Effect.TryConsumeCharge(ctx.Combat.CurrentTick, rechargeTicks)) return;

            ctx.Combat.ApplyEffect(self, _shieldEffect, self);
        }

        /// <summary>
        /// Триггер блока F: None — никогда; AnyHit/Always — на любой удар; OnHitAbovePctMaxHp —
        /// на удар выше порога (по сырому урону) ИЛИ смертельный (сырой ≥ текущего HP — «всегда при смертельном»).
        /// </summary>
        private static bool TriggerMet(RuntimeUnit self, in DamageRequest req)
        {
            AIProfile ai = self.Unit != null ? self.Unit.Ai : null;
            PassiveTrigger trigger = ai != null ? ai.PassiveTrigger : PassiveTrigger.AnyHit;

            switch (trigger)
            {
                case PassiveTrigger.None:
                    return false;

                case PassiveTrigger.AnyHit:
                case PassiveTrigger.Always:
                    return true;

                case PassiveTrigger.OnHitAbovePctMaxHp:
                    float threshold = (ai != null ? ai.PassiveThresholdPct : AIProfile.DefaultPassiveThresholdPct) * self.Stats.Get(StatType.MaxHP);
                    return req.RawDamage > threshold || req.RawDamage >= self.CurrentHP;

                default:
                    return false;
            }
        }
    }
}
