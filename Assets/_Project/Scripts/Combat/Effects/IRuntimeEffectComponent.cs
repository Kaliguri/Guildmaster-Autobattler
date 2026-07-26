using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.Combat.Effects
{
    /// <summary>
    /// Рантайм-контракт поведения эффекта. Производный от Data-маркера <see cref="IEffectComponent"/>:
    /// сериализационный якорь живёт в Data, а хуки, оперирующие боевым состоянием (<see cref="EffectContext"/>),
    /// — здесь, в Combat (кросс-сборочный шов, вики «10» §2.2, «12» §3.1).
    /// </summary>
    /// <remarks>
    /// Компоненты <b>stateless</b>: экземпляр шарится между всеми носителями эффекта. Per-unit
    /// состояние — в <see cref="RuntimeEffect"/>/<see cref="EffectContext"/>. По умолчанию рестак =
    /// <c>OnExpire→OnApply</c> (keyed-снятие обязано быть идемпотентным). Компонент с накопленным
    /// внешним состоянием (щит/заряды) реализует <see cref="IStackableComponent"/> и правит вклад дельтой.
    /// </remarks>
    public interface IRuntimeEffectComponent : IEffectComponent
    {
        void OnApply(in EffectContext ctx);
        void OnExpire(in EffectContext ctx);
    }

    /// <summary>
    /// Опциональный шов рестака (07 §3.8 B1–B3): компонент с накопленным ВНЕШНИМ состоянием
    /// (пул щита, заряды негейта) сам корректирует свой вклад при смене числа стаков. Иначе
    /// EffectSystem применяет дефолт — слепой <c>OnExpire→OnApply</c>, который для такого
    /// состояния неверен: щит пере-вычитается (клэмп съедает частично израсходованный пул),
    /// а заряды бесплатно перезаряжаются. Компонентам с keyed-снятием
    /// (<c>StatModifierComponent</c> снимает моды по ключу-эффекту) шов НЕ нужен — им хватает дефолта.
    /// </summary>
    public interface IStackableComponent : IRuntimeEffectComponent
    {
        /// <summary>
        /// Число стаков изменилось. <paramref name="previousStacks"/> — до изменения,
        /// <c>ctx.Stacks</c> — после. Компонент правит вклад на дельту (обычно <c>ctx.Stacks − previousStacks</c>).
        /// </summary>
        void OnStacksChanged(int previousStacks, in EffectContext ctx);
    }

    /// <summary>Периодический компонент: <c>OnTick</c> каждые <see cref="Interval"/> секунд (DoT/HoT/реген).</summary>
    public interface IPeriodicComponent : IRuntimeEffectComponent
    {
        float Interval { get; }
        void OnTick(in EffectContext ctx);
    }

    /// <summary>Реактивный компонент: реагирует на боевые события (вампиризм/шипы). Диспатч — внутренняя FIFO-очередь.</summary>
    public interface IReactiveComponent : IRuntimeEffectComponent
    {
        CombatEvent Events { get; }
        void OnEvent(in EffectContext ctx, in CombatEventData e);
    }

    /// <summary>
    /// Мутируемый исход pre-damage прохода (§9.3): компонент может полностью негейтить удар или
    /// изменить его величину.
    /// </summary>
    public sealed class PreDamageResult
    {
        /// <summary>Удар отменён (урон не наносится) — «Изворотливость» ассасина.</summary>
        public bool Negated;

        /// <summary>
        /// Множитель входящего урона, накопленный компонентами цели (1 = без изменений). Компоненты
        /// НЕ присваивают его, а домножают через <see cref="AddMultiplier"/> — иначе два уязвимости
        /// на одной цели затирали бы друг друга. Применяется в <see cref="DamagePipeline.Execute"/>
        /// ДО брони: это уязвимость самой цели, а не пробивание источника.
        /// Носители: «Угли» (+1% урона огнём за стак), будущее «+25% урона молнии по Мокрому».
        /// </summary>
        public float DamageMultiplier = 1f;

        /// <summary>Домножить множитель входящего урона (уязвимости копятся, а не перетирают друг друга).</summary>
        public void AddMultiplier(float factor)
        {
            if (factor > 0f) DamageMultiplier *= factor;
        }

        public void Reset()
        {
            Negated = false;
            DamageMultiplier = 1f;
        }
    }

    /// <summary>
    /// Синхронный перехват ДО вычета HP (§9.3): вызывается из <see cref="EffectSystem.RunPreDamage"/>
    /// перед <see cref="DamagePipeline.Execute"/>. В отличие от <see cref="IReactiveComponent"/>
    /// (пост-факт, после урона) — успевает поднять щит, поглощающий триггер-удар («Оплот»), ИЛИ
    /// полностью отменить удар через <see cref="PreDamageResult.Negated"/> («Изворотливость»).
    /// Порядок опроса — по индексу <see cref="RuntimeUnit.ActiveEffects"/> (детерминизм).
    /// </summary>
    public interface IPreDamageComponent : IRuntimeEffectComponent
    {
        void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx);
    }

    /// <summary>
    /// Часть удара, уходящая другой школой урона (карточка The Pyre: по горящей цели половина клинка
    /// бьёт Огнём). Доля берётся ОТ того же сырого урона, суммарная величина удара не меняется —
    /// меняется то, какой бронёй она гасится и какие реакции будит.
    /// </summary>
    public readonly struct AttackSplit
    {
        /// <summary>Доля урона [0..1], уходящая школой <see cref="School"/>.</summary>
        public readonly float Share;
        public readonly Data.Definitions.DamageSchool School;
        public readonly Data.Definitions.MagicElement Element;

        public AttackSplit(float share, Data.Definitions.DamageSchool school, Data.Definitions.MagicElement element)
        {
            Share   = share < 0f ? 0f : share > 1f ? 1f : share;
            School  = school;
            Element = element;
        }
    }

    /// <summary>
    /// Компонент на эффекте АТАКУЮЩЕГО, расщепляющий его авто-атаку по школам (условие смотрит на
    /// цель). Опрашивается <see cref="EffectSystem.TryResolveAttackSplit"/> в момент удара; первый
    /// сработавший выигрывает — порядок опроса по индексу активных эффектов, как и у pre-damage.
    /// </summary>
    public interface IAttackSplitComponent : IRuntimeEffectComponent
    {
        bool TrySplit(RuntimeUnit attacker, RuntimeUnit target, in EffectContext ctx, out AttackSplit split);
    }

    /// <summary>
    /// Опциональный шов: компонент объявляет масштабируемую потенцию. EffectSystem резолвит её
    /// из статов источника один раз при наложении и кладёт снимок в <see cref="RuntimeEffect.ScaledPotency"/>
    /// (per-second rate для DoT/HoT — НЕ запечённый total, вики «11» §5.1).
    /// </summary>
    public interface IScalablePotency
    {
        ScalableValue Potency { get; }
    }
}
