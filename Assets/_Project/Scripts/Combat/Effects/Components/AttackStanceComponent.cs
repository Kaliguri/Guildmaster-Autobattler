using System;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Боевые СТОЙКИ по дистанции: носитель переключает форму своей авто-атаки, когда цель входит в
    /// ближнюю зону и когда выходит из неё. Стойка целиком задаёт форму — доставку, тип урона, on-hit
    /// эффекты, канал и статы; переключение идёт с гистерезисом. Носитель — Десятина: кровавый поток на
    /// дистанции, колющие выпады в упор.
    /// <para><b>Числа:</b> <c>_enterCloseRange</c> — дистанция, ближе которой уходим в ближнюю стойку;
    /// <c>_exitCloseRange</c> — дальше которой возвращаемся в дальнюю (обязан быть больше первой, иначе
    /// на самом пороге юнит дёргается между формами каждый тик). <c>_checkInterval</c> — как часто он
    /// вообще думает о форме.</para>
    /// <para><b>Когда срабатывает:</b> при наложении (выставляет форму сразу) и далее периодически.</para>
    /// </summary>
    /// <remarks>
    /// <b>Стойка УСТАНАВЛИВАЕТ снимок, а не накладывает и снимает эффект-форму</b> — и это главное
    /// свойство модели. Запись профиля целиком идемпотентна: у неё нет обратной операции, порядка
    /// наложений и вопроса «что если форму сорвали диспелом посреди боя». Пара «наложить/снять» на
    /// каждую форму дала бы всё это разом, причём молча.
    /// <para><b>Форму нельзя менять посреди удара.</b> Замах, канал и хвост уже сняли свои цифры с
    /// прежней формы (тип урона едет с ударом до прилёта намеренно), поэтому смена в полёте означала бы
    /// удар, начатый одной формой и завершённый другой. Ждём <c>AttackPhase.Idle</c>.</para>
    /// <para><b>Фокус авто-атаки — тоже часть формы</b> (собрано 2026-07-30). Стойка кладёт его в
    /// <c>RuntimeUnit.StanceTargeting</c>, а мозг читает оттуда, предпочитая профилю кита: профиль он
    /// берёт один раз при сборке, и «фокус по форме» через него невыразим.</para>
    /// </remarks>
    [Serializable]
    public sealed class AttackStanceComponent : IPeriodicComponent, IDeclaresDamageTypes
    {
        [Tooltip("Дистанция до цели, ближе которой юнит уходит в ближнюю стойку (мировые единицы).")]
        [SerializeField] private float _enterCloseRange = 3f;

        [Tooltip("Дистанция, дальше которой возвращается в дальнюю стойку. Обязана быть больше входной — " +
                 "этот зазор и есть гистерезис, без него юнит на пороге меняет форму каждый тик.")]
        [SerializeField] private float _exitCloseRange = 4.5f;

        [Tooltip("Как часто пересматривать форму, сек. Чаще тика мысли AI смысла не имеет.")]
        [SerializeField] private float _checkInterval = 0.1f;

        [Tooltip("Дальняя стойка — форма по умолчанию (её же он принимает, пока цели нет вовсе).")]
        [SerializeField] private AttackStance _farStance;

        [Tooltip("Ближняя стойка.")]
        [SerializeField] private AttackStance _closeStance;

        /// <summary>Индекс дальней стойки. Публичный: по нему гейтятся навыки, живущие в одной форме.</summary>
        public const int FarStanceIndex = 0;

        /// <summary>Индекс ближней стойки. См. <see cref="FarStanceIndex"/>.</summary>
        public const int CloseStanceIndex = 1;

        public float Interval => _checkInterval > 0f ? _checkInterval : 0.1f;

        public void OnApply(in EffectContext ctx)
        {
            RuntimeUnit self = ctx.Target;
            if (self == null) return;

            // На входе в бой форма выставляется без гейта по фазе: удара ещё нет, а выйти на арену без
            // формы вовсе нельзя — снимок авто-атаки остался бы китовым, то есть чужим для обеих стоек.
            Apply(self, DecideStance(self), in ctx);
        }

        public void OnExpire(in EffectContext ctx)
        {
            ctx.Target?.Stats?.RemoveModifiersFrom(ctx.Effect, deferred: true);

            // Форма ушла — вместе с ней уходит и её фокус, иначе боец без стойки продолжил бы выбирать
            // цель по правилу формы, которой на нём больше нет.
            if (ctx.Target != null) ctx.Target.StanceTargeting = null;
        }

        public void OnTick(in EffectContext ctx)
        {
            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead) return;

            int wanted = DecideStance(self);
            if (wanted == self.AttackStance) return;

            // Удар уже начат прежней формой — доигрываем его и переключимся в следующее окно покоя.
            // Спрашиваем именно «идёт ли удар», а не «фаза равна Idle»: с появлением боевого ожидания
            // (CombatIdle) юнит между ударами почти всегда стоит рядом с целью, и сравнение с Idle
            // означало бы, что форма не меняется, пока враг не потерян — то есть никогда.
            if (self.IsSwinging) return;

            Apply(self, wanted, in ctx);
        }

        /// <summary>
        /// Обе формы объявляют свои типы урона наружу — иначе чипы на карточке показывали бы одну,
        /// записанную в ките, и кит со стойками врал бы игроку про половину своего оружия.
        /// </summary>
        public void CollectDamageTypes(System.Collections.Generic.ICollection<DamageType> into)
        {
            if (_farStance != null && _farStance.DamageType != DamageType.Undefined)
                into.Add(_farStance.DamageType);

            if (_closeStance != null && _closeStance.DamageType != DamageType.Undefined)
                into.Add(_closeStance.DamageType);
        }

        /// <summary>Какая форма нужна сейчас: гистерезис считает от ТЕКУЩЕЙ, поэтому порог зависит от неё.</summary>
        private int DecideStance(RuntimeUnit self)
        {
            RuntimeUnit target = self.CurrentTarget;
            // Без цели форму не меняем: дальник, потерявший цель, не должен «возвращаться в дальнюю»
            // ровно в тот тик, когда следующий враг уже шагнул ему в лицо. Первый выбор — дальняя.
            if (target == null || target.IsDead)
                return self.AttackStance >= 0 ? self.AttackStance : FarStanceIndex;

            float distSq = (target.Position - self.Position).sqrMagnitude;
            bool wasClose = self.AttackStance == CloseStanceIndex;
            float threshold = wasClose ? _exitCloseRange : _enterCloseRange;

            return distSq <= threshold * threshold ? CloseStanceIndex : FarStanceIndex;
        }

        private void Apply(RuntimeUnit self, int index, in EffectContext ctx)
        {
            AttackStance stance = index == CloseStanceIndex ? _closeStance : _farStance;
            if (stance == null) return;

            self.AttackStance         = index;
            self.AttackType           = stance.Delivery;
            self.AutoAttackDamageType = stance.DamageType;
            self.AutoAttackOnHit      = stance.OnHitEffects;
            self.AttackChannel        = stance.Channel;
            self.StanceTargeting      = stance.Targeting;

            // Статы формы висят на ключе эффекта-стойки: снятие и наложение одной группой означает, что
            // сменить форму нельзя «наполовину». Отложенно — по закону видимости, как любой баф.
            if (self.Stats == null) return;
            self.Stats.RemoveModifiersFrom(ctx.Effect, deferred: true);
            if (stance.Stats != null && stance.Stats.Length > 0)
                self.Stats.AddModifiersFrom(ctx.Effect, stance.Stats, deferred: true);
        }

        /// <summary>
        /// Одна боевая форма: полный профиль авто-атаки. Полный намеренно — форма, задающая лишь ЧАСТЬ
        /// профиля, оставила бы остальное от кита, и кит стал бы третьей, невидимой стойкой.
        /// </summary>
        [Serializable]
        public sealed class AttackStance
        {
            [Tooltip("Доставка: ближний бой или снаряд. Канальная форма бьёт мгновенно при любом значении.")]
            public AttackType Delivery = AttackType.Melee;

            [Tooltip("Тип урона авто-атаки в этой форме (у Десятины: Кровотечение вдали, Колющий в упор).")]
            public DamageType DamageType = DamageType.Undefined;

            [Tooltip("Канал этой формы. Duration = 0 — форма бьёт обычными одномоментными ударами.")]
            public AttackChannel Channel;

            [Tooltip("On-hit эффекты этой формы.")]
            public EffectData[] OnHitEffects;

            [Tooltip("Фокус авто-атаки в этой форме. Часть профиля формы, как и всё остальное: Десятина " +
                     "вдали бьёт самого живучего (дольше кровоточит), в упор — самого бронированного.")]
            public TargetingMode Targeting = TargetingMode.Nearest;

            [Tooltip("Статы формы: дальность, скорость атаки и прочее. Override задаёт значение, а не дельту.")]
            public StatModifier[] Stats;
        }
    }
}
