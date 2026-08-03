using System;
using System.Collections.Generic;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// <b>Парирование — общий примитив ответа</b> (дизайн Макса 2026-07-30, триггер уточнён 2026-07-31):
    /// носителя бьют в ближнем бою — он отбивает удар, открывает короткое окно защиты, фиксирует
    /// микро-станом всех, кто целился в него спереди, и отвечает своим уникальным ударом вне очереди.
    /// <para><b>Числа:</b> <c>_maxCharges</c> — сколько парирований подряд; <c>_cooldownSeconds</c> —
    /// перезарядка ОДНОГО заряда (обычно 6); <c>_parryWindow</c> — эффект окна, его длительность живёт
    /// там; <c>_microStun</c> — общий микро-стан игры; <c>_stunRangeFactor</c> — во сколько своих
    /// дальностей ловит стан; <c>_frontalDegrees</c> — ширина фронтального сектора; <c>_riposteCharge</c> —
    /// заряд ответного удара (множитель и эффекты живут в нём); <c>_outrangedHaste</c> — разгон, если
    /// парировал того, кто длиннее.</para>
    /// </summary>
    /// <remarks>
    /// <b>Триггер — прилетевший удар, как у Блока</b> (вердикт Макса 2026-07-31). Альтернатива —
    /// намерение атакующего («кто выбрал парирующего целью») — позволяла бы отбивать до попадания, но
    /// требовала бы нового шва «кто в кого целится» в pre-damage; выбран прилетевший удар, и тогда
    /// парирование целиком живёт на уже написанном pre-damage-шве вместе с Блоком и «Изворотливостью».
    /// <para><b>Ответ — это рекаст, а не свой путь урона.</b> Уникальный удар выходит той же дорогой,
    /// что «Решительный удар»: заряд <see cref="_riposteCharge"/> плюс
    /// <see cref="RuntimeUnit.RecastAttack"/>. Поэтому ответ можно сбить контролем, от него можно
    /// уклониться, и он уходит в промах по ушедшей цели — контригра работает та же, что у всех ударов.</para>
    /// <para><b>Требует дееспособности</b> (<see cref="IRequiresAgencyComponent"/>): парирование — это
    /// действие, оглушённый его не совершает. Уже открытое окно станом не гасится — см.
    /// <see cref="ParryWindowComponent"/>.</para>
    /// <para><b>Пока окно висит, второй заряд не тратится:</b> иначе серия из трёх Ударов сожгла бы все
    /// заряды разом, хотя первое же парирование их и так отбило.</para>
    /// </remarks>
    [Serializable]
    public sealed class ParryComponent : IPreDamageComponent, IStackableComponent, IRequiresAgencyComponent
    {
        [Tooltip("Число парирований подряд (заряды восстанавливаются независимо).")]
        [SerializeField] private int _maxCharges = 1;

        [Tooltip("Независимая перезарядка ОДНОГО заряда, сек. Обычно 6.")]
        [SerializeField] private float _cooldownSeconds = 6f;

        [Tooltip("Эффект окна парирования (ParryWindowComponent). Длительность окна — его _baseDuration, 0.3 с.")]
        [SerializeField] private EffectData _parryWindow;

        [Tooltip("Общий микро-стан игры, ложащийся на всех, кто целился в парирующего спереди. Пусто = стана нет.")]
        [SerializeField] private EffectData _microStun;

        [Tooltip("Во сколько СВОИХ дальностей атаки ловит микро-стан (2 = вдвое дальше, чем бьёт сам). " +
                 "Иначе парирование доставало бы лучника с другого конца арены.")]
        [SerializeField] private float _stunRangeFactor = 2f;

        [Tooltip("Полная ширина фронтального сектора в градусах: 180 = ±90° от взгляда. " +
                 "0 или 360 = сектора нет, ловит вокруг себя.")]
        [SerializeField] private float _frontalDegrees = 180f;

        [Tooltip("Заряд ответного удара: множитель и его эффекты живут в нём (как у «Решительного удара»). " +
                 "Пусто = ответ обычной атакой вне очереди.")]
        [SerializeField] private EffectData _riposteCharge;

        [Tooltip("Разгон, если парировал того, у кого дальность больше своей: догнать копейщика. Пусто = нет.")]
        [SerializeField] private EffectData _outrangedHaste;

        [Tooltip("Награда носителю за само парирование, без условий: щит разбойника-дуэлянта (200 на 3 сек). " +
                 "Пусто = награды нет. В отличие от разгона, не спрашивает, кого именно парировал.")]
        [SerializeField] private EffectData _onParryReward;

        [NonSerialized] private readonly List<RuntimeUnit> _aimers = new List<RuntimeUnit>();

        public void OnApply(in EffectContext ctx)
        {
            ctx.Effect.ArmCharges(_maxCharges);
        }

        public void OnExpire(in EffectContext ctx) { }

        public void OnStacksChanged(int previousStacks, in EffectContext ctx)
        {
            // Рестак НЕ трогает заряды — та же готча, что у Блока и «Изворотливости»: дефолтный
            // OnExpire→OnApply дал бы бесплатный рефилл всех зарядов на каждый стак.
        }

        /// <summary>
        /// Удар, который парирование вообще способно встретить: прямое попадание (авто-атака или
        /// способность) от источника, бьющего в ближнем бою.
        /// </summary>
        /// <remarks>
        /// Владелец правила один на два компонента — иначе окно ловило бы не то же, что открыло его.
        /// Стрелок парированием не встречается: «атакуют в ближнем бою» — условие самого дизайна, и без
        /// него парирующий отбивал бы стрелы, ради чего механики никто не заводил.
        /// </remarks>
        public static bool IsParryable(in DamageRequest incoming)
        {
            if (!incoming.IsDirectHit) return false;

            RuntimeUnit attacker = incoming.Source;
            return attacker != null && attacker.AttackType == AttackType.Melee;
        }

        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            if (result.Negated) return;          // удар уже погашен окном или уклонением
            if (!IsParryable(in incoming)) return;

            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead || _parryWindow == null) return;

            // Окно уже открыто — значит парирование состоялось; следующий Удар гасит оно, а не второй заряд.
            if (HasWindow(self)) return;

            int rechargeTicks = Mathf.Max(1, Mathf.RoundToInt(_cooldownSeconds * SimConstants.TickRate));
            if (!ctx.Effect.TryConsumeCharge(ctx.Combat.CurrentTick, rechargeTicks)) return;

            // Удар-триггер гасится тут же — как щит Блока встаёт под тот же удар, ради которого поднялся.
            result.Negated = true;
            ctx.Combat.ApplyEffect(self, _parryWindow, self);

            // Награда за парирование — сразу с окном, а не по факту ответного удара: отбил он уже здесь,
            // а ответ ему могут сорвать станом, и тогда щит пропадал бы вместе с чужим контролем.
            if (_onParryReward != null) ctx.Combat.ApplyEffect(self, _onParryReward, self);

            StunAimers(self, in ctx);
            Riposte(self, in incoming, in ctx);
        }

        /// <summary>Висит ли окно парирования прямо сейчас — сравнением с тем же ассетом, без служебных тегов.</summary>
        private bool HasWindow(RuntimeUnit self)
        {
            for (int i = 0; i < self.ActiveEffects.Count; i++)
                if (ReferenceEquals(self.ActiveEffects[i].Def, _parryWindow)) return true;

            return false;
        }

        /// <summary>
        /// Микро-стан всем, кто выбрал парирующего целью и стоит перед ним. Не «всем вокруг»: парирование
        /// сбивает тех, кто на него замахнулся, а не случайных прохожих (вердикт Макса 2026-07-30/12).
        /// </summary>
        private void StunAimers(RuntimeUnit self, in EffectContext ctx)
        {
            if (_microStun == null) return;

            float radius = self.Stats.Get(StatType.AttackRange) * (_stunRangeFactor > 0f ? _stunRangeFactor : 1f);
            if (radius <= 0f) return;

            ctx.Combat.QueryUnitsInRadius(self.Position, radius, _aimers, TargetFilter.Enemies, self.Team);

            Vector2 facing = Facing(self);
            for (int i = 0; i < _aimers.Count; i++)
            {
                RuntimeUnit aimer = _aimers[i];
                if (aimer.IsDead || !ReferenceEquals(aimer.CurrentTarget, self)) continue;
                if (!InFront(self, aimer, facing)) continue;

                ctx.Combat.ApplyEffect(aimer, _microStun, self);
            }
        }

        /// <summary>
        /// Ответ: заряд уникального удара плюс рекаст. Порядок важен — заряд ставится ДО рекаста, иначе
        /// удар вне очереди вышел бы обычным, а усиление досталось бы следующему за ним.
        /// </summary>
        private void Riposte(RuntimeUnit self, in DamageRequest incoming, in EffectContext ctx)
        {
            if (_riposteCharge != null) ctx.Combat.ApplyEffect(self, _riposteCharge, self);
            else self.RecastAttack(ctx.Combat.Tuning);

            // Разгон достаётся только тому, кого достают, а он не достаёт: парировал копейщика — получил
            // возможность дойти. Против равного по дальности бонуса нет, иначе он был бы просто прибавкой
            // ко всякому парированию.
            if (_outrangedHaste == null) return;

            RuntimeUnit attacker = incoming.Source;
            if (attacker == null) return;

            float mine  = self.Stats.Get(StatType.AttackRange);
            float their = attacker.Stats.Get(StatType.AttackRange);
            if (their > mine + 1e-3f) ctx.Combat.ApplyEffect(self, _outrangedHaste, self);
        }

        /// <summary>
        /// Куда смотрит парирующий. Направления взгляда в симуляции нет вовсе — его выводит показ по
        /// цели и движению, и здесь берётся то же правило, чтобы «спереди» в бою совпадало со «спереди»
        /// на экране. Нет ни цели, ни движения — сектор не применяется (см. <see cref="InFront"/>).
        /// </summary>
        private static Vector2 Facing(RuntimeUnit self)
        {
            RuntimeUnit target = self.CurrentTarget;
            if (target != null && !target.IsDead)
            {
                Vector2 toTarget = target.Position - self.Position;
                if (toTarget.sqrMagnitude > 1e-6f) return toTarget;
            }

            return self.Position - self.PreviousPosition;
        }

        private bool InFront(RuntimeUnit self, RuntimeUnit other, Vector2 facing)
        {
            if (_frontalDegrees <= 0f || _frontalDegrees >= 360f) return true;
            if (facing.sqrMagnitude <= 1e-6f) return true;   // неизвестно, куда смотрит — не сужаем

            Vector2 toOther = other.Position - self.Position;
            if (toOther.sqrMagnitude <= 1e-6f) return true;  // вплотную: угла нет

            float cos = Vector2.Dot(facing.normalized, toOther.normalized);
            return cos >= Mathf.Cos(_frontalDegrees * 0.5f * Mathf.Deg2Rad);
        }
    }
}
