using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Пробуждение Сна (карточка [[the-lull]]): ЧУЖОЙ прямой удар снимает сон и получает за это
    /// <see cref="_wakeMultiplier"/>, а урон того, кто сон наложил, спящего НЕ будит. Сам запрет действий
    /// даёт <see cref="ControlComponent"/> на том же эффекте — этот компонент отвечает только за выход.
    /// <para><b>Числа:</b> <c>_wakeMultiplier</c> — во сколько раз больнее пробуждающий удар (2);
    /// <c>_sourceWakes</c> — будит ли носителя урон самого автора сна (выкл: иначе «Кошмар» обрывал бы
    /// собственное окно казни первым же тиком). Длительность сна — у эффекта, не здесь.</para>
    /// <para><b>Когда срабатывает:</b> в pre-damage на прямом ударе (авто-атака или способность). Тики
    /// DoT и ответки реактивов сон не рвут — иначе любое горение будило бы цель мгновенно.</para>
    /// </summary>
    /// <remarks>
    /// <b>Почему множитель, а не «удар проходит как обычно»:</b> команда обязана иметь цену за ошибку и
    /// награду за осознанный размен — «разбудил» становится решением, а не случайностью. Плюс это делает
    /// кита честно плохим против AoE-составов: любая площадь союзника будит цель рано, зато хотя бы
    /// получает свои ×2.
    /// <para><b>Снятие идёт диспелом по тегу <see cref="EffectTag.Sleep"/></b>, а не «удалить этот
    /// экземпляр»: сон на цели один, но повесить его могли поверх чужого, и адресное снятие оставило бы
    /// второй висеть. Тег сна отдельный именно для этого — по <see cref="EffectTag.Control"/> ушли бы и
    /// оглушения.</para>
    /// </remarks>
    [Serializable]
    public sealed class SleepComponent : IPreDamageComponent
    {
        [Tooltip("Во сколько раз сильнее удар, который будит спящего (2 = вдвое). 1 = без награды.")]
        [SerializeField] private float _wakeMultiplier = 2f;

        [Tooltip("Будит ли спящего урон ТОГО, кто наложил сон. Выкл (дефолт) — автор сна бьёт свою цель, не пробуждая.")]
        [SerializeField] private bool _sourceWakes;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        /// <summary>Пробуждение усиливает удар и рвёт сон, но самого удара не отменяет.</summary>

        public int Priority => ReactionPriority.Modify;


        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            if (result.Negated) return;
            if (!incoming.IsDirectHit) return;   // доты и ответки реактивов сон не рвут

            RuntimeUnit sleeper = ctx.Target;
            if (sleeper == null || sleeper.IsDead) return;

            bool fromAuthor = ReferenceEquals(incoming.Source, ctx.Source);
            if (fromAuthor && !_sourceWakes) return;

            if (_wakeMultiplier > 1f) result.AddMultiplier(_wakeMultiplier);

            // Диспел с запредельной силой: пробуждение — не «очистка по тиру», удар сильнее любого сна.
            ctx.Combat.Dispel(new DispelRequest(
                sleeper, DispelTargetPolarity.Any, EffectTag.Sleep, int.MaxValue, 0, incoming.Source));
        }
    }
}
