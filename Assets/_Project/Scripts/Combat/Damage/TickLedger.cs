using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Реестр тика: копит урон и лечение, посчитанные за раунд, и применяет их ОДНИМ коммитом.
    /// Пока удары ложились в HP по одному, исход зависел от того, чей ход в обходе списка раньше —
    /// а у зеркальных сторон этот порядок обратный (см. <c>tick-resolution</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>Правила разрешения</b> (приняты 2026-07-27):</para>
    /// <list type="bullet">
    ///   <item><b>Дельты складываются:</b> <c>HP += лечение − урон</c>, затем кламп по <see cref="StatType.MaxHP"/>.
    ///   Хилер, успевший в тот же тик, спасает от смертельного удара.</item>
    ///   <item><b>Щит суммирует:</b> весь урон тика вычитается из щита разом, остаток идёт в HP. Для чистого
    ///   поглощения это то же, что последовательно, но результат не зависит от порядка ударов.</item>
    ///   <item><b>Доли пропорциональны:</b> фактически нанесённое и фактически вылеченное делятся между
    ///   источниками по их вкладу. Никто не наказан за то, что его удар обработали вторым, а вампиризм и
    ///   on-heal реактивы считают ровно то, что дошло.</item>
    ///   <item><b>Убийство — у наибольшей доли</b> (тай-брейк по <see cref="RuntimeUnit.Id"/>): у смерти один
    ///   владелец, награды за неё не множатся.</item>
    /// </list>
    /// <para>
    /// Расчёт урона (эффективности, броня, уязвимость) делает <see cref="DamagePipeline.Resolve"/> в момент
    /// заявки — он чист и от порядка не зависит, потому что статы заморожены на тик законом видимости.
    /// Реестр владеет только применением.
    /// </para>
    /// </remarks>
    public sealed class TickLedger
    {
        /// <summary>Заявка на урон: сколько дойдёт до цели (после брони) и с какими метаданными.</summary>
        private readonly struct DamageEntry
        {
            public readonly RuntimeUnit Source;
            public readonly float Amount;
            public readonly float Mitigated;
            public readonly DamageSourceKind SourceKind;
            public readonly DamageSchool School;
            public readonly DamageAffinity Affinity;
            public readonly MagicElement Element;
            public readonly float Vulnerability;

            public DamageEntry(RuntimeUnit source, float amount, float mitigated, in DamageRequest req)
            {
                Source        = source;
                Amount        = amount;
                Mitigated     = mitigated;
                SourceKind    = req.SourceKind;
                School        = req.School;
                Affinity      = req.Affinity;
                Element       = req.Element;
                Vulnerability = req.Vulnerability;
            }
        }

        /// <summary>Заявка на лечение: сырое значение уже с учётом обеих HealShield-эффективностей.</summary>
        private readonly struct HealEntry
        {
            public readonly RuntimeUnit Source;
            public readonly float Amount;

            public HealEntry(RuntimeUnit source, float amount)
            {
                Source = source;
                Amount = amount;
            }
        }

        // Заявки по целям. Словари живут между тиками (переиспользуются), очищаются на коммите.
        private readonly Dictionary<RuntimeUnit, List<DamageEntry>> _damage = new();
        private readonly Dictionary<RuntimeUnit, List<HealEntry>>   _heal   = new();

        // Стабильный порядок обхода целей на коммите: словарь порядка не гарантирует, а он входит
        // в последовательность событий, которую видят презентация и реактивы.
        private readonly List<RuntimeUnit> _order = new();

        // Пулы списков — чтобы не аллоцировать на каждом тике боя.
        private readonly Stack<List<DamageEntry>> _damagePool = new();
        private readonly Stack<List<HealEntry>>   _healPool   = new();

        /// <summary>
        /// Порог значимости заявки, единиц HP. Ниже него урон и лечение отбрасываются.
        /// </summary>
        /// <remarks>
        /// Не оптимизация, а глушитель бесконечных цепочек. Взаимные шипы отражают долю урона друг в
        /// друга и затухают геометрически, но нуля не достигают никогда: 25% отражения дают ряд
        /// 100 → 25 → 6.25 → … и упираются в предел числа, а не в конец боя. Сотая доля HP не читается
        /// ни игроком, ни балансом, поэтому хвост обрезается здесь — до того, как упрётся в кап раундов.
        /// </remarks>
        public const float MinSignificantAmount = 0.05f;

        /// <summary>Есть ли что применять.</summary>
        public bool HasPending => _order.Count > 0;

        /// <summary>Заявить урон по цели: <paramref name="amount"/> — уже посчитанный эффективный урон,
        /// <paramref name="mitigated"/> — сколько срезала защита (для разбора «чем боец не умер»).</summary>
        public void AddDamage(RuntimeUnit target, float amount, float mitigated, in DamageRequest req)
        {
            if (target == null || target.IsDead || amount < MinSignificantAmount) return;

            if (!_damage.TryGetValue(target, out List<DamageEntry> list))
            {
                list = _damagePool.Count > 0 ? _damagePool.Pop() : new List<DamageEntry>();
                _damage[target] = list;
                Track(target);
            }

            list.Add(new DamageEntry(req.Source, amount, mitigated, in req));
        }

        /// <summary>Заявить лечение цели: <paramref name="amount"/> — уже с учётом HealShield-эффективностей.</summary>
        public void AddHeal(RuntimeUnit target, float amount, RuntimeUnit source)
        {
            if (target == null || target.IsDead || amount < MinSignificantAmount) return;

            if (!_heal.TryGetValue(target, out List<HealEntry> list))
            {
                list = _healPool.Count > 0 ? _healPool.Pop() : new List<HealEntry>();
                _heal[target] = list;
                Track(target);
            }

            list.Add(new HealEntry(source, amount));
        }

        /// <summary>Запомнить цель в стабильном порядке (целей в бою десятки — линейной проверки хватает).</summary>
        private void Track(RuntimeUnit target)
        {
            if (!_order.Contains(target)) _order.Add(target);
        }

        /// <summary>
        /// Применить всё накопленное. <paramref name="sink"/> получает исход по каждой цели — он поднимает
        /// события наружу и кормит реактивы. После вызова реестр пуст.
        /// </summary>
        public void Commit(ITickLedgerSink sink)
        {
            // Копия порядка: sink по ходу дела может заявить новый урон (реактивы), и он уедет
            // в СЛЕДУЮЩИЙ раунд, а не подмешается в этот обход.
            int count = _order.Count;
            for (int i = 0; i < count; i++) Resolve(_order[i], sink);

            for (int i = 0; i < count; i++)
            {
                RuntimeUnit target = _order[i];
                if (_damage.TryGetValue(target, out List<DamageEntry> d)) { d.Clear(); _damagePool.Push(d); _damage.Remove(target); }
                if (_heal.TryGetValue(target, out List<HealEntry> h))     { h.Clear(); _healPool.Push(h);   _heal.Remove(target); }
            }

            _order.RemoveRange(0, count);
        }

        /// <summary>Свести все заявки по одной цели в одно изменение HP и щита.</summary>
        private void Resolve(RuntimeUnit target, ITickLedgerSink sink)
        {
            _damage.TryGetValue(target, out List<DamageEntry> hits);
            _heal.TryGetValue(target, out List<HealEntry> heals);

            float totalDamage = 0f;
            if (hits != null) for (int i = 0; i < hits.Count; i++) totalDamage += hits[i].Amount;

            float totalHeal = 0f;
            if (heals != null) for (int i = 0; i < heals.Count; i++) totalHeal += heals[i].Amount;

            // 1. Щит поглощает весь урон тика разом, остаток уходит в HP.
            float shieldAbsorbed = Mathf.Min(target.CurrentShield, totalDamage);
            target.CurrentShield -= shieldAbsorbed;
            float hpDamage = totalDamage - shieldAbsorbed;

            // 2. Дельты складываются, и только потом кламп: успевший хилер спасает от смертельного удара.
            float maxHp    = target.Stats.Get(StatType.MaxHP);
            float before   = target.CurrentHP;
            float afterRaw = before - hpDamage + totalHeal;
            target.CurrentHP = Mathf.Min(afterRaw, maxHp);

            // Фактически вылеченное — то, что уцелело после клампа. Оно и делится между хилерами:
            // на потолок HP наткнулись все вместе, а не тот, кого обработали последним.
            float healApplied = Mathf.Max(0f, target.CurrentHP - (before - hpDamage));
            bool  killed      = target.CurrentHP <= 0f;

            // 3. Урон по источникам — пропорционально вкладу.
            if (hits != null && totalDamage > 0f)
            {
                RuntimeUnit killer = killed ? Killer(hits) : null;

                for (int i = 0; i < hits.Count; i++)
                {
                    DamageEntry e = hits[i];
                    float share = e.Amount / totalDamage;
                    var result = new DamageResult(
                        hpDamage * share, shieldAbsorbed * share,
                        killed && ReferenceEquals(e.Source, killer),
                        e.SourceKind, e.School, e.Affinity, e.Element, e.Vulnerability,
                        e.Mitigated);   // срезанное принадлежит своему удару целиком, делить его не надо

                    sink.OnDamageResolved(e.Source, target, in result);
                }
            }

            // 4. Лечение по источникам — тоже пропорционально; overheal никому не засчитывается.
            if (heals != null && healApplied > 0f && totalHeal > 0f)
            {
                for (int i = 0; i < heals.Count; i++)
                {
                    HealEntry e = heals[i];
                    sink.OnHealResolved(e.Source, target, healApplied * (e.Amount / totalHeal));
                }
            }
        }

        /// <summary>
        /// Кому засчитать убийство: наибольшая доля урона, при равенстве — меньший
        /// <see cref="RuntimeUnit.Id"/>. Сравниваются только те, кто бил ОДНУ цель, поэтому зеркальные
        /// стороны получают зеркальный же ответ.
        /// </summary>
        private static RuntimeUnit Killer(List<DamageEntry> hits)
        {
            RuntimeUnit best = null;
            float bestAmount = -1f;

            for (int i = 0; i < hits.Count; i++)
            {
                RuntimeUnit source = hits[i].Source;
                if (source == null) continue;

                float amount = hits[i].Amount;
                bool better = best == null
                              || amount > bestAmount
                              || (amount == bestAmount && source.Id < best.Id);

                if (better) { best = source; bestAmount = amount; }
            }

            return best;
        }

        /// <summary>Сбросить все незакрытые заявки (перезапуск боя на месте).</summary>
        public void Clear()
        {
            _damage.Clear();
            _heal.Clear();
            _order.Clear();
            _damagePool.Clear();
            _healPool.Clear();
        }
    }

    /// <summary>
    /// Куда реестр отдаёт разрешённые изменения: события наружу и внутренние события для реактивов.
    /// Реализует <c>CombatSimulation</c> — она и остаётся единственной точкой мутации мира.
    /// </summary>
    public interface ITickLedgerSink
    {
        /// <summary>Урон дошёл до цели: доля этого источника в общем ударе тика.</summary>
        void OnDamageResolved(RuntimeUnit source, RuntimeUnit target, in DamageResult result);

        /// <summary>Лечение дошло до цели: доля этого источника в фактически вылеченном (overheal не входит).</summary>
        void OnHealResolved(RuntimeUnit source, RuntimeUnit target, float applied);
    }
}
