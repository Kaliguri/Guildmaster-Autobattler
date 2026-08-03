using System;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Глобальный шаблон стат-системы: дефолты статов + тюнинг-константы пайплайна
    /// (armor-константа, реген ресурса). Единственный экземпляр на проект —
    /// база сборки любого юнита (вики «10» §4.2, «11» §3–§4).
    /// <para>Клампа скорости атаки здесь НЕТ намеренно (решение 2026-07-28): симуляция его никогда
    /// не применяла, то есть поля в конфиге врали, а потолок темпа бил ровно по тому киту, чья
    /// фантазия — разгон (Огненный мечник).</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Combat/Stats Config", fileName = "StatsConfig")]
    public sealed class StatsConfig : ScriptableObject
    {
        [Header("Armor / mitigation")]
        [Tooltip("Armor-константа K из пайплайна урона: mult = K / (K + effArmor). Старт 100 (броня 100 → −50% урона).")]
        [SerializeField] private float _armorConstantK = 100f;

        [Header("Ресурс")]
        [Tooltip("Сколько ресурса капает в секунду — одинаково у всех (решение 2026-07-27). При запасе 100 " +
                 "полная шкала = 20 секунд, а темп способности читается как «стоимость ÷ это число».")]
        [SerializeField] private float _resourceRegenPerSecond = 5f;

        [Header("Stat defaults (override; пусто = натуральный дефолт)")]
        [Tooltip("Явные дефолты статов. Если стата нет в списке — берётся натуральный дефолт (1.0 для эффективностей и Size, иначе 0).\n\n" +
                 "MaxHP и MoveSpeed здесь ЗАПРЕЩЕНЫ: их базу задаёт боевой класс (ГДД «Боевая система»), " +
                 "и ClassBalanceConfig кладёт её первой Override-группой — то есть значение отсюда всё равно " +
                 "не доживало до юнита, но при чтении конфига выглядело правдой. Охраняется ContentValidationTests.")]
        [SerializeField] private StatDefault[] _defaults = Array.Empty<StatDefault>();

        public float ArmorConstantK => _armorConstantK;
        public float ResourceRegenPerSecond => _resourceRegenPerSecond;

        /// <summary>Базовое (доmodifier) значение стата: явный override из ассета или натуральный дефолт.</summary>
        public float GetDefault(StatType stat)
        {
            for (int i = 0; i < _defaults.Length; i++)
            {
                if (_defaults[i].Stat == stat)
                {
                    return _defaults[i].Value;
                }
            }

            return NaturalDefault(stat);
        }

        /// <summary>Натуральный старт стата по его смыслу: <c>1.0</c> для PercentMult-эффективностей и <see cref="StatType.Size"/>, иначе <c>0</c>.</summary>
        public static float NaturalDefault(StatType stat)
        {
            switch (stat)
            {
                case StatType.DamageTakenEff:
                case StatType.DamageDealtEff:
                case StatType.HealShieldTakenEff:
                case StatType.HealShieldDealtEff:
                case StatType.ApplyBuffEff:
                case StatType.ApplyDebuffEff:
                case StatType.ReceiveBuffEff:
                case StatType.ReceiveDebuffEff:
                case StatType.CooldownEff:
                case StatType.ResourceGainEff:
                case StatType.SummonHealthEff:
                case StatType.SummonDamageEff:
                case StatType.Size:
                    return 1f;
                default:
                    return 0f;
            }
        }

        [Serializable]
        private struct StatDefault
        {
            public StatType Stat;
            public float Value;
        }
    }
}
