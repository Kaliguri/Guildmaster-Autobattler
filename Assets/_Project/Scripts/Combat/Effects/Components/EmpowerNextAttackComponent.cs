using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// «Усиленный следующий удар»: пока эффект висит, следующая авто-атака носителя бьёт сильнее,
    /// игнорирует часть брони и (опционально) блинкует носителя в спину цели.
    /// <para>Компонент живёт на самом СОСТОЯНИИ (у Убийцы — на бафе «Скрытность»), а не на том, кто
    /// это состояние выдал. Поэтому любой источник скрытности — пассивка «Скрытность», активка
    /// «Уйти в тень», будущий предмет — даёт ровно одно усиление с одними и теми же числами, и
    /// править их приходится в одном месте.</para>
    /// <para><b>Числа:</b> <c>_damageMult</c> — во сколько раз сильнее удар (2 = вдвое);
    /// <c>_flatPen</c> — сколько ед. брони игнорирует этот удар (20 у Убийцы; 0 = бьёт по обычной
    /// броне); <c>_blinkBehind</c> — телепортировать ли носителя за спину цели перед ударом.</para>
    /// <para><b>Когда тратится:</b> первой же авто-атакой — <see cref="Systems.AutoAttackSystem"/>
    /// применяет множитель, забирает пробивание и снимает состояние по тегу. Если состояние снято
    /// раньше (скрытность развеяли), усиление уходит вместе с ним: заряженный удар после
    /// сработавшей контры был бы наградой за проигранный обмен.</para>
    /// <para><b>Почему <see cref="IRearmOnRefreshComponent"/>:</b> заряд одноразовый, поэтому
    /// повторный уход в тень поверх ещё висящего бафа обязан взвести его заново — иначе второе
    /// убийство (или каст активки) не даёт вообще ничего. <c>OnApply</c> присваивает, а не
    /// накапливает, и потому безопасен поверх живого состояния.</para>
    /// </summary>
    [Serializable]
    public sealed class EmpowerNextAttackComponent : IRearmOnRefreshComponent
    {
        [Tooltip("Множитель урона усиленной авто-атаки (2 = вдвое).")]
        [SerializeField] private float _damageMult = 2f;

        [Tooltip("Сколько ед. брони игнорирует усиленный удар (20 у Убийцы). 0 = бьёт по обычной броне.")]
        [SerializeField] private float _flatPen;

        [Tooltip("Телепортировать носителя за спину цели перед усиленным ударом.")]
        [SerializeField] private bool _blinkBehind;

        [Tooltip("На сколько усиленный удар отбрасывает цель, мировых единиц (Монах воды = 2). 0 = не толкает.")]
        [SerializeField] private float _knockbackDistance;

        [Tooltip("Тег, по которому удар снимает взведший эффект. Stealth = удар выводит из тени (Убийца); " +
                 "Empowered = обычный заряд, стелса не касается.")]
        [SerializeField] private EffectTag _consumeTag = EffectTag.Stealth;

        [Tooltip("Конвертации статов в множитель усиления (M4). Убийца: прямая форма от AttackSpeed — " +
                 "«+0.5 к множителю за каждую 1.0 сверх базовой».")]
        [SerializeField] private Data.Stats.StatConversion[] _damageMultScalings;

        public void OnApply(in EffectContext ctx)
        {
            RuntimeUnit self = ctx.Target;
            if (self == null || self.IsDead) return;

            // Множитель — через конвертации (M4): удар из скрытности растёт со скоростью атаки.
            self.EmpowerDamageMult  = Data.Stats.StatConversion.ApplyAll(_damageMultScalings, _damageMult, self.Stats);
            self.EmpowerFlatPen     = _flatPen;
            self.EmpowerKnockback   = _knockbackDistance;
            self.EmpowerConsumeTag  = _consumeTag;
            if (_blinkBehind) self.BlinkBehindOnNextAttack = true;
        }

        public void OnExpire(in EffectContext ctx)
        {
            RuntimeUnit self = ctx.Target;
            if (self == null) return;

            self.EmpowerDamageMult = 0f;
            self.EmpowerFlatPen    = 0f;
            self.EmpowerKnockback  = 0f;
            if (_blinkBehind) self.BlinkBehindOnNextAttack = false;
        }
    }
}
