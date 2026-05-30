using System;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Глобальный шаблон стат-системы: дефолты статов + тюнинг-константы пайплайна
    /// (armor-константа, клампы скорости атаки). Единственный экземпляр на проект —
    /// база сборки любого юнита (вики «10» §4.2, «11» §3–§4).
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Combat/Stats Config", fileName = "StatsConfig")]
    public sealed class StatsConfig : ScriptableObject
    {
        [Header("Armor / mitigation")]
        [Tooltip("Armor-константа K из пайплайна урона: mult = K / (K + effArmor). Старт 100 (броня 100 → −50% урона).")]
        [SerializeField] private float _armorConstantK = 100f;

        [Header("Attack speed clamp (атак/сек)")]
        [SerializeField] private float _attackSpeedMin = 0.1f;
        [SerializeField] private float _attackSpeedMax = 2.5f;

        [Header("Stat defaults (override; пусто = натуральный дефолт)")]
        [Tooltip("Явные дефолты статов. Если стата нет в списке — берётся натуральный дефолт (1.0 для эффективностей и Size, иначе 0).")]
        [SerializeField] private StatDefault[] _defaults = Array.Empty<StatDefault>();

        public float ArmorConstantK => _armorConstantK;
        public float AttackSpeedMin => _attackSpeedMin;
        public float AttackSpeedMax => _attackSpeedMax;

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
