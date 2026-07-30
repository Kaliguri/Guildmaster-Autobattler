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
    /// <c>_element</c> — что именно гасится (<c>None</c> в стихии = вся школа целиком).</para>
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

        [Tooltip("Школа урона, против которой работает сопротивление.")]
        [SerializeField] private DamageSchool _school = DamageSchool.Magical;

        [Tooltip("Стихия при магической школе. None = гасится вся школа, любой стихии.")]
        [SerializeField] private MagicElement _element = MagicElement.Fire;

        public void OnApply(in EffectContext ctx) { }

        public void OnExpire(in EffectContext ctx) { }

        public void OnPreDamage(in DamageRequest incoming, PreDamageResult result, in EffectContext ctx)
        {
            if (_resistPct <= 0f || result.Negated) return;
            if (incoming.School != _school) return;
            if (_element != MagicElement.None && incoming.Element != _element) return;

            // Домножаем, а не присваиваем: два источника стойкости обязаны сложиться множителями, как и
            // две уязвимости (см. PreDamageResult.AddMultiplier).
            result.AddMultiplier(1f - _resistPct);
        }
    }
}
