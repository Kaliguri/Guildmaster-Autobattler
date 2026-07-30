using System;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Combat.Effects.Components
{
    /// <summary>
    /// Сопротивление урону ОДНОЙ школы (и, если задан, одной стихии): пока эффект висит, подходящий
    /// входящий урон умножается на <c>1 − _resistPct</c>. «Раздуть жар» Хранителя углей даёт союзнику
    /// сопротивление огню — потому что сам только что навесил на него угли.
    /// <para><b>Числа:</b> <c>_resistPct</c> — сколько урона снимается (0.25 = −25%); <c>_school</c> и
    /// <c>_wholeSchool</c> — что именно гасится (флаг = вся школа этого типа целиком).</para>
    /// <para><b>Когда срабатывает:</b> в pre-damage, до брони — это уязвимость/стойкость самой цели, а не
    /// пробивание источника. Стаки НЕ учитываются: сопротивление задаётся эффектом, а не их числом.</para>
    /// </summary>
    /// <remarks>
    /// Статом это выразить нечем: броня у нас одна на всю магию (ГДД «Статы» §Школа vs сродство), а
    /// <c>DamageTakenEff</c> общий — он снизил бы и физику, то есть соврал бы про природу защиты. Поэтому
    /// сопротивление школе живёт компонентом на эффекте: там, где уже считаются уязвимости («Угли»
    /// усиливают огонь по подожжённому), и ровно тем же множителем.
    /// <para>Дееспособность не требуется: это свойство тела, а не действие — оглушённый союзник не
    /// перестаёт быть закалённым.</para>
    /// </remarks>
    [Serializable]
    public sealed class SchoolDamageResistComponent : IPreDamageComponent
    {
        [Tooltip("Доля снимаемого урона (0.25 = −25%).")]
        [Range(0f, 1f)]
        [SerializeField] private float _resistPct = 0.25f;

        [Tooltip("Тип урона, против которого работает сопротивление.")]
        [SerializeField] private DamageType _damageType = DamageType.Undefined;

        [Tooltip("Гасить всю школу этого типа, а не только сам тип.")]
        [SerializeField] private bool _wholeSchool;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            if (_resistPct <= 0f || result.Negated) return;
            if (!DamageTypes.Matches(_damageType, _wholeSchool, incoming.Type)) return;

            // Домножаем, а не присваиваем: два источника стойкости обязаны сложиться множителями, как и
            // две уязвимости (см. PreDamageResult.AddMultiplier).
            result.AddMultiplier(1f - _resistPct);
        }
    }
}
