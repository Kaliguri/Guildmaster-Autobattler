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

    /// <summary>
    /// Опциональный шов подкрепления: компонент, выдающий ОДНОРАЗОВЫЙ заряд, взводится заново, когда
    /// уже висящий эффект накладывают повторно (<see cref="StackRule.Refresh"/> /
    /// <see cref="StackRule.StackAndRefresh"/>). Обычный Refresh продлевает длительность и компонентов
    /// не трогает — и правильно: разбудить <c>StatModifierComponent</c> значило бы добавить его моды
    /// второй раз. Но заряд, который уже потрачен, продлевать нечего: «Скрытность», подкреплённая
    /// вторым уходом в тень, обязана снова дать усиленный удар, иначе повторный уход не даёт ничего.
    /// <para>Требование к реализации: <c>OnApply</c> должен быть идемпотентен (присваивать, а не
    /// накапливать) — его позовут поверх живого состояния.</para>
    /// </summary>
    public interface IRearmOnRefreshComponent : IRuntimeEffectComponent
    {
    }

    /// <summary>
    /// Маркер: этому компоненту нужна ДЕЕСПОСОБНОСТЬ носителя — он не срабатывает, пока юнит выведен
    /// контролем (<c>CanAct == false</c>: оглушение, сон). Проверку делает <see cref="EffectSystem"/> один
    /// раз на диспатче, а не каждый компонент у себя.
    /// <para><b>Почему маркер, а не список эффектов</b> (решение Макса 2026-07-29). Список «эффектов,
    /// отключающих реактивы» был бы вторым владельцем факта, который уже есть: дееспособность считает
    /// <see cref="EffectSystem"/> из активных компонентов контроля в <c>CanAct</c>. Такой список расходится
    /// на первом же новом контроле — добавили сон, забыли дописать.</para>
    /// <para><b>Почему маркер, а не проверка везде.</b> Требование дееспособности — свойство самого
    /// поведения, и оно РАЗНОЕ: щит «Оплота» юнит поднимает (нужна), кувырок «Отхода» делает
    /// (нужна), вихревой заход монаха — это рывок (нужна). А шипы колют бронёй сами, вампиризм и горение
    /// идут своим ходом — им дееспособность не нужна, и оглушённый носитель обязан продолжать колоть.</para>
    /// </summary>
    public interface IRequiresAgencyComponent : IRuntimeEffectComponent
    {
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
        /// <summary>Удар отменён (урон не наносится) — «Отход» ассасина.</summary>
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
    /// полностью отменить удар через <see cref="PreDamageResult.Negated"/> («Отход»).
    /// <para><b>Порядок опроса — по <see cref="Priority"/> вниз</b>, при равных числах раньше идёт своя
    /// реакция носителя, а не наложенная союзником (<see cref="ReactionOrigin"/>); совсем равные
    /// разводятся индексом в <see cref="RuntimeUnit.ActiveEffects"/>, чтобы бой остался детерминированным.
    /// Проход обрывается на первой реакции, отменившей удар: за отменённый удар больше никто не платит.</para>
    /// </summary>
    public interface IPreDamageComponent : IRuntimeEffectComponent
    {
        /// <summary>
        /// Насколько рано спросить эту реакцию: больше — раньше. Ступени и их смысл —
        /// <see cref="ReactionPriority"/>.
        /// <para><b>Значение живёт в коде компонента, а не в ассете, намеренно:</b> порядок реакций —
        /// это правило боя, и дизайнер не должен уметь переставить его из инспектора. Дефолта у свойства
        /// нет по той же причине — новая реакция обязана выбрать себе место осознанно.</para>
        /// </summary>
        int Priority { get; }

        void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx);
    }

    /// <summary>
    /// Часть удара, уходящая другой школой урона (карточка The Pyre: по горящей цели половина клинка
    /// бьёт Огнём). Доля берётся ОТ того же сырого урона, суммарная величина удара не меняется —
    /// меняется то, какой бронёй она гасится и какие реакции будит.
    /// <para>Исключение — <see cref="OwnDamage"/>: отщеплённая часть считается СВОЕЙ величиной (у
    /// Мечника — процентом от макс. HP цели), и тогда суммарный урон удара уже не сохраняется. Это
    /// намеренно: процентная половина и есть то, чем он выкашивает толстых.</para>
    /// </summary>
    public readonly struct AttackSplit
    {
        /// <summary>Доля урона [0..1], уходящая типом <see cref="DamageType"/>. При <see cref="HasOwnDamage"/> задаёт только, сколько СНИМАЕТСЯ с исходного типа.</summary>
        public readonly float Share;

        /// <summary>
        /// Своя величина отщеплённой части. &gt; 0 — она бьёт этим числом вместо <c>RawDamage × Share</c>;
        /// исходная школа при этом всё равно теряет свою долю, то есть клинок остаётся ополовиненным.
        /// </summary>
        public readonly float OwnDamage;

        /// <summary>
        /// Тип урона отщеплённой части — обязателен: расщепление и существует ради того, чтобы часть
        /// удара пошла ДРУГИМ типом («Водяной щит» Монаха: половина Дробящим, половина Льдом).
        /// </summary>
        public readonly Data.Definitions.DamageType DamageType;

        public bool HasOwnDamage => OwnDamage > 0f;

        public AttackSplit(float share, Data.Definitions.DamageType damageType, float ownDamage = 0f)
        {
            Share      = share < 0f ? 0f : share > 1f ? 1f : share;
            DamageType = damageType;
            OwnDamage  = ownDamage < 0f ? 0f : ownDamage;
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
    /// Опциональный шов: компонент носителя усиливает ЕГО удары по цели, отвечающей условию
    /// (Криомант больнее бьёт замороженных). Это свойство ИСТОЧНИКА — в отличие от уязвимости, которая
    /// живёт на цели («Угли» усиливают огонь по подожжённому) и считается в pre-damage.
    /// <para>Прибавка возвращается долей (0.25 = +25%) и складывается между компонентами, как статы:
    /// два разных источника усиления обязаны оба сработать. Выбор «что сильнее» внутри одного набора
    /// правил — забота компонента, а не системы.</para>
    /// </summary>
    public interface IOutgoingDamageBonusComponent : IRuntimeEffectComponent
    {
        float BonusAgainst(RuntimeUnit attacker, RuntimeUnit target, bool isAutoAttack, in EffectContext ctx);
    }

    /// <summary>
    /// Опциональный шов: эффект на НОСИТЕЛЕ способен отправить его собственную атаку в молоко (слепота).
    /// Спрашивается один раз на снятии цифр удара; <c>true</c> — удар уходит мимо.
    /// </summary>
    /// <remarks>
    /// Зеркало pre-damage-негейта («Отход» гасит удар со стороны ЦЕЛИ) — только со стороны
    /// атакующего, и потому отдельный шов: у цели решение тратит её заряды и щиты, а здесь ничего не
    /// расходуется, кроме самой атаки. Ответ обязан быть детерминированным (правило нулевого выходного
    /// рандома): счёт идёт по <see cref="RuntimeUnit.HitsMade"/>, а не по броску.
    /// </remarks>
    public interface IAttackMissComponent : IRuntimeEffectComponent
    {
        bool MissesAttack(RuntimeUnit attacker, in EffectContext ctx);
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
