using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// <b>«Шипастое древо»</b> — пассивка Древня (карточка [[the-bloom]]).
    /// <para><b>Что делает:</b> получив прямой удар, носитель огрызается шипами по ВСЕМ врагам вокруг
    /// себя. Урон не выбирается им — он случается в ответ, поэтому кит тем сильнее, чем гуще на нём
    /// висят: один враг почти не наказывается, свалка из четверых разбивается об него.</para>
    /// <para><b>Числа:</b>
    /// <list type="bullet">
    /// <item><c>_flatDamage</c> — плоский урон залпа, ОСНОВНАЯ величина (задаёт автор).</item>
    /// <item><c>_armorRatio</c> — добавка от физической брони носителя (0.1 = +10% статы брони).</item>
    /// <item><c>_radius</c> — радиус ответки, мировые единицы; растёт от «Разрастания».</item>
    /// <item><c>_radiusPerGrowthStack</c> — прибавка радиуса за стак «Разрастания» (0.2 = +20% базы).</item>
    /// <item><c>_cooldownSeconds</c> — микро-КД между залпами: ритм задаёт Древень, а не темп врагов.</item>
    /// <item><c>_damageType</c> — тип урона шипов (у Древня — Колющий).</item>
    /// </list>
    /// Итог залпа: <c>(_flatDamage + броня × _armorRatio) × стаки</c>. При броне 90 и базе 25 это 34.</para>
    /// <para><b>Когда срабатывает:</b> реактив на <see cref="CombatEvent.DamageTaken"/>, только от
    /// прямого удара и не чаще микро-КД.</para>
    /// </summary>
    /// <remarks>
    /// База введена 2026-07-27: пока урон был чистой бронёй (доля 1.0), каждая купленная единица защиты
    /// становилась единицей урона, и танк, вкладывающийся в живучесть, выходил в главные дамагеры игры —
    /// 554 урона в секунду по свалке против 226 у самого бьющего кита.
    /// <para>
    /// Два гейта, оба принципиальные:
    /// <list type="bullet">
    /// <item>Только прямой удар (<see cref="CombatEventData.IsDirectHit"/>) — авто-атака или атакующая
    /// способность. Тики DoT крону не будят (иначе яд превращал бы Древня в вечный дамаг-пульс), и свои же
    /// шипы тоже: их урон помечен <see cref="DamageSourceKind.Reactive"/>, поэтому пинг-понга нет.</item>
    /// <item>Микро-КД между залпами: без него четверо быстрых мили превращают ответку в пулемёт с частотой
    /// ЧУЖОЙ скорости атаки. Кулдаун держим эффектом-маркером на носителе — компонент живёт в SO и общий для
    /// всех юнитов, своё состояние в нём хранить нельзя.</item>
    /// </list>
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class ArmorThornsComponent : IReactiveComponent
    {
        private const string CooldownMarkerId = "sys.thorns_cooldown";

        [Tooltip("Плоский урон залпа — основная величина. Задаётся автором и не растёт от прокачки статов.")]
        [SerializeField] private float _flatDamage;

        [Tooltip("Доля брони носителя, добавляемая к плоской базе (0.1 = 10% статы брони).")]
        [SerializeField] private float _armorRatio = 1f;

        [Tooltip("Радиус ответного удара вокруг носителя (мировые единицы).")]
        [SerializeField] private float _radius = 3f;

        [Tooltip("Школа урона шипов.")]
        [SerializeField] private DamageType _damageType = DamageType.Undefined;

        /// <summary>Тип урона шипов (прямые поля источника) — для агрегации тегов «быстрого чтения».</summary>
        public DamageType DamageType => _damageType;

        [Tooltip("Эффект «Разрастание»: каждый его стак раздувает радиус шипов (карточка ГДД). Пусто = радиус фиксирован.")]
        [SerializeField] private EffectData _growthEffect;

        [Tooltip("Прибавка к радиусу за стак «Разрастания» (0.2 = +20% от базового радиуса за стак).")]
        [SerializeField] private float _radiusPerGrowthStack = 0.2f;

        [Tooltip("Микро-КД между залпами шипов, сек. Ритм ответки задаёт Древень, а не скорость атаки врагов. 0 = без КД.")]
        [SerializeField] private float _cooldownSeconds = 0.5f;

        // Буфер запроса — компонент stateless по игровому состоянию, буфер переиспользуется как в системах.
        [NonSerialized] private readonly List<RuntimeUnit> _hits = new List<RuntimeUnit>();

        // Маркер КД строится в коде (как sys.airborne в симуляции) — системный эффект, не контент-ассет.
        [NonSerialized] private EffectData _cooldownMarker;

        public CombatEvent Events => CombatEvent.DamageTaken;

        public void OnApply(in EffectContext ctx) { }
        public void OnExpire(in EffectContext ctx) { }

        public void OnEvent(in EffectContext ctx, in CombatEventData e)
        {
            if (!e.IsDirectHit) return;

            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead) return;

            if (_cooldownSeconds > 0f && HasEffect(self, CooldownMarker())) return;

            // База + слабая доля брони (решение 2026-07-26): пока урон был чистой бронёй, каждая купленная
            // единица защиты становилась единицей урона, и танк, вкладывающийся в живучесть, автоматически
            // выходил в главные дамагеры игры — 554 урона в секунду по свалке против 226 у самого бьющего кита.
            float damage = (_flatDamage + self.Stats.Get(StatType.PhysArmor) * _armorRatio) * ctx.Stacks;
            if (damage <= 0f) return;

            float radius = EffectiveRadius(self);

            ctx.Combat.ReportAreaHit(AreaHit.Circle(self.Position, radius, self.Team));
            ctx.Combat.QueryUnitsInRadius(self.Position, radius, _hits, TargetFilter.Enemies, self.Team);

            for (int i = 0; i < _hits.Count; i++)
            {
                RuntimeUnit victim = _hits[i];
                if (victim.IsDead) continue;

                // Reactive: ответка шипов сама реактивы не будит — иначе два Древня зациклят друг друга.
                ctx.Combat.DealDamage(new DamageRequest(self, victim, damage, _damageType, ctx.Combat.ArmorK,
                    sourceKind: DamageSourceKind.Reactive));
            }

            if (_cooldownSeconds > 0f) ctx.Combat.ApplyEffect(self, CooldownMarker(), self);
        }

        /// <summary>
        /// Радиус шипов с учётом «Разрастания»: активка Древня раздувает не только HP и броню, но и крону
        /// (карточка ГДД). Радиус — не стат, поэтому стаки читаем прямо с носителя.
        /// </summary>
        private float EffectiveRadius(RuntimeUnit self)
        {
            if (_growthEffect == null || _radiusPerGrowthStack <= 0f) return _radius;

            int stacks = StacksOf(self, _growthEffect);
            return stacks > 0 ? _radius * (1f + _radiusPerGrowthStack * stacks) : _radius;
        }

        /// <summary>Маркер микро-КД: системный эффект без компонентов, его наличие и есть «шипы на перезарядке».</summary>
        private EffectData CooldownMarker()
        {
            return _cooldownMarker ??= EffectData.CreateRuntime(
                CooldownMarkerId,
                EffectPolarity.Neutral,     // Neutral: длительность КД не крутится статами эффективности эффектов
                EffectTag.None,
                baseDuration: _cooldownSeconds,
                unremovable: true);         // диспелом КД не сбросить
        }

        private static bool HasEffect(RuntimeUnit unit, EffectData def)
        {
            for (int i = 0; i < unit.ActiveEffects.Count; i++)
                if (unit.ActiveEffects[i].Def == def) return true;
            return false;
        }

        private static int StacksOf(RuntimeUnit unit, EffectData def)
        {
            for (int i = 0; i < unit.ActiveEffects.Count; i++)
                if (unit.ActiveEffects[i].Def == def) return unit.ActiveEffects[i].VisibleStacks;
            return 0;
        }
    }
}
