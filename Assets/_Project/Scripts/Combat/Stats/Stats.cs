using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Рантайм стат-объект юнита. Хранит группы модификаторов по источнику,
    /// вычисляет итог по формуле <c>(base + ΣFlat) × (1 + ΣPercentAdd) × Π(1 + PercentMult)</c>
    /// и кэширует результат с инвалидацией по dirty-флагу (вики «10» §5.2, «11» §1).
    /// </summary>
    public sealed class Stats : IStatReader
    {
        private static readonly int StatCount = Enum.GetValues(typeof(StatType)).Length;

        private readonly StatsConfig _config;
        private readonly List<ModifierGroup> _groups = new List<ModifierGroup>();
        private readonly float[] _cache;
        private bool _dirty = true;

        /// <param name="config">Источник базовых значений статов; null — натуральные дефолты.</param>
        public Stats(StatsConfig config)
        {
            _config = config;
            _cache = new float[StatCount];
        }

        /// <summary>Итоговое значение стата после всех модификаторов и клампов.</summary>
        public float Get(StatType stat)
        {
            if (_dirty) RebuildCache();
            return _cache[(int)stat];
        }

        /// <summary>
        /// Добавить группу модификаторов от <paramref name="source"/>.
        /// Все модификаторы снимаются разом через <see cref="RemoveModifiersFrom"/>.
        /// </summary>
        public void AddModifiersFrom(object source, StatModifier[] modifiers)
        {
            if (modifiers == null || modifiers.Length == 0) return;
            _groups.Add(new ModifierGroup(source, modifiers));
            _dirty = true;
        }

        /// <summary>Удалить все модификаторы, добавленные от <paramref name="source"/>.</summary>
        public void RemoveModifiersFrom(object source)
        {
            for (int i = _groups.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_groups[i].Source, source))
                {
                    _groups.RemoveAt(i);
                    _dirty = true;
                }
            }
        }

        private void RebuildCache()
        {
            float[] flat       = new float[StatCount];
            float[] percentAdd = new float[StatCount];
            float[] multAccum  = new float[StatCount];
            for (int i = 0; i < StatCount; i++) multAccum[i] = 1f;

            for (int g = 0; g < _groups.Count; g++)
            {
                StatModifier[] mods = _groups[g].Modifiers;
                for (int m = 0; m < mods.Length; m++)
                {
                    int idx = (int)mods[m].Stat;
                    switch (mods[m].Op)
                    {
                        case ModifierOp.Flat:        flat[idx]       += mods[m].Value;               break;
                        case ModifierOp.PercentAdd:  percentAdd[idx] += mods[m].Value;               break;
                        case ModifierOp.PercentMult: multAccum[idx]  *= (1f + mods[m].Value);        break;
                    }
                }
            }

            for (int i = 0; i < StatCount; i++)
            {
                float baseVal = _config != null
                    ? _config.GetDefault((StatType)i)
                    : StatsConfig.NaturalDefault((StatType)i);
                _cache[i] = (baseVal + flat[i]) * (1f + percentAdd[i]) * multAccum[i];
            }

            _dirty = false;
        }

        private readonly struct ModifierGroup
        {
            public readonly object Source;
            public readonly StatModifier[] Modifiers;

            public ModifierGroup(object source, StatModifier[] modifiers)
            {
                Source    = source;
                Modifiers = modifiers;
            }
        }
    }
}
