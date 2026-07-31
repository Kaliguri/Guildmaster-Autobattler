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

        [Tooltip("Эффекты, которые усиленный удар накладывает на цель СВЕРХ обычных on-hit: Драугр вгоняет " +
                 "лишние стаки «Изморози», «Решительный удар» — оглушение и ослабление. Пусто = ничего. " +
                 "Ложатся ПРИ ПОПАДАНИИ: сорванный контролем или ушедший в промах удар не накладывает ничего.")]
        [SerializeField] private EffectData[] _bonusOnHitEffects;

        [Tooltip("Сколько раз наложить КАЖДЫЙ бонус-эффект (для стакающихся это и есть число лишних стаков).")]
        [Min(0)]
        [SerializeField] private int _bonusOnHitCount = 1;

        [Tooltip("Выпустить усиленный удар ВНЕ ОЧЕРЕДИ (рекаст): хвост текущей атаки обрезается, ожидание " +
                 "интервала снимается. Так живут «Решительный удар», удар Монаха воды и выход из тени у " +
                 "Убийцы. Выключено = заряд ждёт своей обычной атаки (Драугр: каждая третья).")]
        [SerializeField] private bool _recastImmediately;

        [Tooltip("Множитель замаха удара, вышедшего по рекасту: 1 = обычный, 0.5 = вдвое короче. " +
                 "Работает только при включённом рекасте.")]
        [Range(0f, 1f)]
        [SerializeField] private float _recastWindupMult = 1f;

        [Tooltip("Доля усиленного удара, уходящая ДРУГИМ типом урона («Восходящий удар» Монаха воды: " +
                 "половина Дробящим, половина Льдом). 0 = удар идёт целиком типом автоатаки.")]
        [Range(0f, 1f)]
        [SerializeField] private float _splitShare;

        [Tooltip("Тип отщеплённой половины усиленного удара. Undefined при доле > 0 — дефект контента.")]
        [SerializeField] private DamageType _splitType = DamageType.Undefined;

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
            self.EmpowerConsumeTag   = _consumeTag;
            self.EmpowerBonusEffects = _bonusOnHitEffects;
            self.EmpowerBonusCount   = _bonusOnHitCount;
            self.EmpowerSplitShare   = _splitType != DamageType.Undefined ? _splitShare : 0f;
            self.EmpowerSplitType    = _splitType;
            if (_blinkBehind) self.BlinkBehindOnNextAttack = true;

            // Рекаст — часть ОДНОГО намерения «следующий удар особый и выходит сейчас», поэтому живёт
            // здесь, а не выводится из чужих полей. До 2026-07-31 его получала любая активка с уроном
            // без канала, и он молча доставался залпу Арканиста и вихрю Копейщика, которым не нужен.
            if (_recastImmediately) self.RecastAttack(recoveryMult: 0f, nextWindupMult: _recastWindupMult);
        }

        public void OnExpire(in EffectContext ctx)
        {
            RuntimeUnit self = ctx.Target;
            if (self == null) return;

            self.EmpowerDamageMult = 0f;
            self.EmpowerFlatPen    = 0f;
            self.EmpowerKnockback  = 0f;
            self.EmpowerBonusEffects = null;
            self.EmpowerBonusCount   = 0;
            self.EmpowerSplitShare  = 0f;
            self.EmpowerSplitType   = DamageType.Undefined;
            if (_blinkBehind) self.BlinkBehindOnNextAttack = false;
        }
    }
}
