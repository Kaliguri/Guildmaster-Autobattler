using System;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// <b>Удар в тыл больнее</b> (гоблин-убийца): носитель наносит больше урона цели, в тыловой конус
    /// которой он стоит. Позиционная награда за обход строя — та же ось, что у маскировки: подойти
    /// со спины стоит времени, и цена должна отбиваться уроном.
    /// <para><b>Числа:</b> <c>_bonus</c> — прибавка долей (0.5 = +50%); <c>_rearConeCos</c> — косинус
    /// тылового конуса (1 = строго со спины, 0.5 = ±60°, 0 = вся задняя полуплоскость);
    /// <c>_autoAttackOnly</c> — считать только для авто-атак.</para>
    /// <para><b>Когда срабатывает:</b> в момент удара, до расщепления по школам — прибавка достаётся
    /// удару целиком, а не одной его половине.</para>
    /// </summary>
    /// <remarks>
    /// Отдельный компонент, а не поле в <see cref="RearStrikeEffectComponent"/>: тот добавляет цели
    /// ЭФФЕКТ и силы намеренно не несёт (его сила живёт в бонусном эффекте). Множитель урона внутри него
    /// завёл бы второго владельца силы у одного компонента — ровно то, от чего его докстринг
    /// предостерегает. Здесь наоборот: сила есть, эффекта нет.
    /// <para><b>Что такое «тыл»</b> — <see cref="CombatPositioning.IsRearAttack"/>: конвенция сторон, а не
    /// разворот юнита (своего «лица» в симуляции нет). Ограничение прокси описано там же.</para>
    /// </remarks>
    [Serializable]
    public sealed class RearDamageBonusComponent : IOutgoingDamageBonusComponent
    {
        [Tooltip("Прибавка к урону за удар в тыл долей: 0.5 = +50%.")]
        [SerializeField] private float _bonus = 0.5f;

        [Tooltip("Косинус тылового конуса: 1 = строго со спины, 0.5 = ±60°, 0 = вся задняя полуплоскость.")]
        [Range(0f, 1f)]
        [SerializeField] private float _rearConeCos = 0.5f;

        [Tooltip("Только авто-атаки. Выкл = любой урон носителя, включая способности.")]
        [SerializeField] private bool _autoAttackOnly = true;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public float BonusAgainst(RuntimeUnit attacker, RuntimeUnit target, bool isAutoAttack, in EffectContext ctx)
        {
            if (_bonus == 0f) return 0f;
            if (_autoAttackOnly && !isAutoAttack) return 0f;
            if (attacker == null || target == null) return 0f;
            return CombatPositioning.IsRearAttack(attacker, target, _rearConeCos) ? _bonus : 0f;
        }
    }
}
