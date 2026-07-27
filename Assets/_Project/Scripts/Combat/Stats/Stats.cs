using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Рантайм стат-объект юнита. Хранит группы модификаторов по источнику,
    /// вычисляет итог по формуле <c>(baseTerm + ΣFlat) × (1 + ΣPercentAdd) × Π(1 + PercentMult)</c>,
    /// где <c>baseTerm = Override (если задан) ИНАЧЕ дефолт StatsConfig</c>,
    /// и кэширует результат с инвалидацией по dirty-флагу (вики «10» §5.2, «11» §1).
    /// </summary>
    public sealed class Stats : IStatExplainer
    {
        private static readonly int StatCount = Enum.GetValues(typeof(StatType)).Length;

        private readonly StatsConfig _config;
        private readonly List<ModifierGroup> _groups = new List<ModifierGroup>();
        private readonly float[] _cache;
        private bool _dirty = true;

        // Отложенные изменения от эффектов — ждут конца тика (см. Commit). ОДНА очередь, а не два списка:
        // порядок операций внутри тика значим (стак снимает свою же группу и вешает новую).
        private List<PendingOp> _pending;

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
        /// <param name="deferred">
        /// true — изменение ждёт <see cref="Commit"/> (конец боевого тика). Так вешают эффекты: наложенный
        /// баф или ослабление не должны менять стат посреди тика, иначе исход зависит от того, чей ход в
        /// обходе списка раньше. false (умолчание) — стат меняется сразу; так собирают юнита фабрика,
        /// инвентарь и тесты, где никакого тика нет.
        /// </param>
        public void AddModifiersFrom(object source, StatModifier[] modifiers, bool deferred = false)
        {
            if (modifiers == null || modifiers.Length == 0) return;

            if (deferred)
            {
                _pending ??= new List<PendingOp>();
                _pending.Add(new PendingOp(source, modifiers));
                return;
            }

            _groups.Add(new ModifierGroup(source, modifiers));
            _dirty = true;
        }

        /// <summary>
        /// Разложить стат на базу и вклады источников — для показа игроку (план UI-реворка
        /// §II.10.1). <see cref="StatValue.Final"/> по построению равен <see cref="Get"/>:
        /// обе дороги идут через <see cref="Compose"/>.
        /// </summary>
        /// <remarks>
        /// Инспекция, а НЕ горячий путь: аллоцирует и пересчитывает стат по разу на каждый
        /// модификатор. Зовётся тултипом и панелью юнита (порядка раза в полсекунды на
        /// открытый элемент), никогда — тиком симуляции.
        /// </remarks>
        public StatValue Explain(StatType stat)
        {
            // Override формирует БАЗУ, а не вклад: это способ авторинга базовых статов
            // реликвии. Побеждает последний — тот же порядок, что в RebuildCache.
            float baseVal = DefaultOf(stat);
            int count = 0;

            for (int g = 0; g < _groups.Count; g++)
            {
                StatModifier[] mods = _groups[g].Modifiers;
                for (int m = 0; m < mods.Length; m++)
                {
                    if (mods[m].Stat != stat) continue;
                    if (mods[m].Op == ModifierOp.Override) baseVal = mods[m].Value;
                    else count++;
                }
            }

            if (count == 0)
                return new StatValue(stat, baseVal, Get(stat), null, StatKinds.KindOf(stat));

            var sources = new object[count];
            var contributing = new StatModifier[count];
            int n = 0;
            for (int g = 0; g < _groups.Count; g++)
            {
                StatModifier[] mods = _groups[g].Modifiers;
                for (int m = 0; m < mods.Length; m++)
                {
                    if (mods[m].Stat != stat || mods[m].Op == ModifierOp.Override) continue;
                    sources[n] = _groups[g].Source;
                    contributing[n] = mods[m];
                    n++;
                }
            }

            float final = ComposeSubset(baseVal, contributing, -1);

            var terms = new StatTerm[count];
            for (int i = 0; i < count; i++)
            {
                // Вклад = насколько итог просядет без этого модификатора. Единственная честная
                // мера при смешанных Flat/PercentAdd/PercentMult — сырое значение мода зависит
                // от базы и соседей и игроку ничего не сообщает.
                float without = ComposeSubset(baseVal, contributing, i);
                terms[i] = new StatTerm(
                    (sources[i] as IModifierSource)?.ModifierSourceLocKey,
                    contributing[i].Op,
                    contributing[i].Value,
                    final - without);
            }

            return new StatValue(stat, baseVal, final, terms, StatKinds.KindOf(stat));
        }

        /// <summary>Собрать стат из подмножества модификаторов, пропустив <paramref name="skipIndex"/> (-1 = не пропускать).</summary>
        private static float ComposeSubset(float baseVal, StatModifier[] mods, int skipIndex)
        {
            float flat = 0f, percentAdd = 0f, multAccum = 1f;
            for (int i = 0; i < mods.Length; i++)
            {
                if (i == skipIndex) continue;
                switch (mods[i].Op)
                {
                    case ModifierOp.Flat:        flat       += mods[i].Value;        break;
                    case ModifierOp.PercentAdd:  percentAdd += mods[i].Value;        break;
                    case ModifierOp.PercentMult: multAccum  *= (1f + mods[i].Value); break;
                }
            }
            return Compose(baseVal, flat, percentAdd, multAccum);
        }

        /// <summary>Удалить все модификаторы, добавленные от <paramref name="source"/>.</summary>
        /// <param name="deferred">См. <see cref="AddModifiersFrom"/>: снятие эффекта живёт по тому же
        /// закону, что и наложение — иначе истечение баффа посреди тика снова решало бы бой порядком обхода.</param>
        public void RemoveModifiersFrom(object source, bool deferred = false)
        {
            if (deferred)
            {
                _pending ??= new List<PendingOp>();
                _pending.Add(new PendingOp(source, modifiers: null));
                return;
            }

            for (int i = _groups.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_groups[i].Source, source))
                {
                    _groups.RemoveAt(i);
                    _dirty = true;
                }
            }
        }

        /// <summary>
        /// Проявить отложенные изменения: снятия, затем наложения. Зовётся раз в конце боевого тика
        /// (<c>CombatSimulation.Tick</c>) — до этого <see cref="Get"/> отдаёт значения, с которыми тик начался.
        /// </summary>
        /// <remarks>
        /// Операции проигрываются В ПОРЯДКЕ ПОСТУПЛЕНИЯ, и это не косметика. Стакающийся эффект при каждом
        /// новом наложении снимает свою прежнюю группу и вешает пересчитанную под новое число стаков; если
        /// разложить очередь на «сначала все снятия, потом все наложения», снятия уйдут вхолостую (снимать
        /// ещё нечего), а сложатся ВСЕ группы разом — три стака по 5 дают не 15, а 30. Ровно это и поймал
        /// StatModifier_ScalesByStacks.
        /// </remarks>
        public void Commit()
        {
            if (_pending == null || _pending.Count == 0) return;

            for (int i = 0; i < _pending.Count; i++)
            {
                PendingOp op = _pending[i];
                if (op.Modifiers == null) RemoveModifiersFrom(op.Source);
                else                      AddModifiersFrom(op.Source, op.Modifiers);
            }

            _pending.Clear();
        }

        /// <summary>Отложенная операция: <see cref="Modifiers"/> = null означает снятие группы источника.</summary>
        private readonly struct PendingOp
        {
            public readonly object Source;
            public readonly StatModifier[] Modifiers;

            public PendingOp(object source, StatModifier[] modifiers)
            {
                Source    = source;
                Modifiers = modifiers;
            }
        }

        private void RebuildCache()
        {
            float[] flat        = new float[StatCount];
            float[] percentAdd  = new float[StatCount];
            float[] multAccum   = new float[StatCount];
            float[] overrideVal = new float[StatCount];
            bool[]  hasOverride = new bool[StatCount];
            for (int i = 0; i < StatCount; i++) multAccum[i] = 1f;

            for (int g = 0; g < _groups.Count; g++)
            {
                StatModifier[] mods = _groups[g].Modifiers;
                for (int m = 0; m < mods.Length; m++)
                {
                    int idx = (int)mods[m].Stat;
                    switch (mods[m].Op)
                    {
                        case ModifierOp.Flat:        flat[idx]       += mods[m].Value;        break;
                        case ModifierOp.PercentAdd:  percentAdd[idx] += mods[m].Value;        break;
                        case ModifierOp.PercentMult: multAccum[idx]  *= (1f + mods[m].Value); break;
                        // Абсолютный ввод: заменяет базовый терм (последний Override побеждает).
                        case ModifierOp.Override:    overrideVal[idx] = mods[m].Value; hasOverride[idx] = true; break;
                    }
                }
            }

            for (int i = 0; i < StatCount; i++)
            {
                // baseTerm = Override (если задан) ИНАЧЕ дефолт конфига/натуральный.
                float baseVal = hasOverride[i]
                    ? overrideVal[i]
                    : DefaultOf((StatType)i);
                _cache[i] = Compose(baseVal, flat[i], percentAdd[i], multAccum[i]);
            }

            _dirty = false;
        }

        /// <summary>
        /// Формула сборки стата — ЕДИНСТВЕННОЕ место, где она записана. И горячий путь
        /// (<see cref="RebuildCache"/>), и разбор для показа (<see cref="Explain"/>) обязаны
        /// звать её, иначе тултипы разойдутся с симуляцией на первом же изменении правил.
        /// </summary>
        private static float Compose(float baseVal, float flat, float percentAdd, float multAccum)
            => (baseVal + flat) * (1f + percentAdd) * multAccum;

        /// <summary>Базовое значение стата до модификаторов: из конфига или натуральный дефолт.</summary>
        private float DefaultOf(StatType stat)
            => _config != null ? _config.GetDefault(stat) : StatsConfig.NaturalDefault(stat);

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
