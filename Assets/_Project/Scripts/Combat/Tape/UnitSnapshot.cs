using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat.Tape
{
    /// <summary>
    /// Состояние одного юнита на конец одного тика — то, из чего показ рисует кадр, не заглядывая в
    /// живой <see cref="RuntimeUnit"/>. Сим уходит вперёд на окно опережения, поэтому живой юнит уже
    /// «в будущем»: читать из него — не задержка, а рассинхрон.
    /// <para><b>Состав не произвольный:</b> ровно те поля, которые сегодня читает презентация
    /// (<c>UnitView</c>, полоски HP/маны, силуэт). Что читается один раз при привязке вида —
    /// <c>UnitData</c>, палитра, префаб — здесь не дублируется: это не состояние, а определение.</para>
    /// </summary>
    public readonly struct UnitSnapshot
    {
        /// <summary>Id юнита в симуляции — ключ, по которому показ находит свой вид.</summary>
        public readonly int Id;

        /// <summary>Команда: нужна цвету полосок и фильтрам показа.</summary>
        public readonly int Team;

        public readonly Vector2 Position;

        /// <summary>Позиция на конец ПРЕДЫДУЩЕГО тика — из неё показ интерполирует движение.</summary>
        public readonly Vector2 PreviousPosition;

        public readonly float CurrentHP;
        public readonly float MaxHP;
        public readonly float Shield;
        public readonly float CurrentResource;
        public readonly float MaxResource;

        /// <summary>Размер тела: масштаб вида и радиус попадания в презентации.</summary>
        public readonly float Size;

        /// <summary>Фаза атаки на конец тика — по ней показ выбирает анимацию.</summary>
        public readonly AttackPhase Phase;

        /// <summary>Полная длительность замаха в тиках (0 = замаха нет) — знаменатель прогресса.</summary>
        public readonly int WindupTicks;

        /// <summary>Сколько тиков замаха осталось — вместе с <see cref="WindupTicks"/> даёт прогресс.</summary>
        public readonly int WindupRemaining;

        /// <summary>Тики до следующей авто-атаки: показ тянет по ним хвост восстановления.</summary>
        public readonly int AttackCooldownTicks;

        /// <summary>Id текущей цели или <c>-1</c>: показ разворачивает юнита к ней. Ссылки на объект
        /// здесь нет намеренно — иначе через цель протёк бы живой сим.</summary>
        public readonly int TargetId;

        /// <summary>Маска тегов эффектов: по ней показ включает стелс-силуэт и прочие метки.</summary>
        public readonly EffectTag Tags;

        public readonly bool IsDead;

        public UnitSnapshot(
            int id, int team, Vector2 position, Vector2 previousPosition,
            float currentHp, float maxHp, float shield, float currentResource, float maxResource,
            float size, AttackPhase phase, int windupTicks, int windupRemaining,
            int attackCooldownTicks, int targetId, EffectTag tags, bool isDead)
        {
            Id                  = id;
            Team                = team;
            Position            = position;
            PreviousPosition    = previousPosition;
            CurrentHP           = currentHp;
            MaxHP               = maxHp;
            Shield              = shield;
            CurrentResource     = currentResource;
            MaxResource         = maxResource;
            Size                = size;
            Phase               = phase;
            WindupTicks         = windupTicks;
            WindupRemaining     = windupRemaining;
            AttackCooldownTicks = attackCooldownTicks;
            TargetId            = targetId;
            Tags                = tags;
            IsDead              = isDead;
        }

        /// <summary>Снять состояние с живого юнита. Единственное место, где сим встречается с лентой.</summary>
        public static UnitSnapshot From(RuntimeUnit unit)
        {
            RuntimeUnit target = unit.CurrentTarget;
            return new UnitSnapshot(
                unit.Id,
                unit.Team,
                unit.Position,
                unit.PreviousPosition,
                unit.CurrentHP,
                unit.Stats.Get(StatType.MaxHP),
                unit.CurrentShield,
                unit.CurrentResource,
                unit.Stats.Get(StatType.MaxResource),
                unit.Stats.Get(StatType.Size),
                unit.Phase,
                unit.WindupTicks,
                unit.WindupRemaining,
                unit.AttackCooldownTicks,
                target != null ? target.Id : -1,
                unit.EffectTagMask,
                unit.IsDead);
        }
    }
}
