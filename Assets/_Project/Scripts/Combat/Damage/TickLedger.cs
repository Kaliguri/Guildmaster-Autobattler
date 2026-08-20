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
    /// <b>Реестр хранит СЫРУЮ заявку, а не посчитанный урон</b> (2026-07-31). Считать в момент заявки было
    /// нельзя: расчёт зовёт pre-damage цели, а тот не чист — тратит запас щита, копит
    /// <see cref="RuntimeUnit.AbsorbedByWard"/>, жжёт заряды. Значит первый заявленный удар менял мир
    /// посреди фазы решений, и тот, кто ходит в обходе позже, решал по другому состоянию: два Антимага,
    /// обменявшись «Перегрузкой» в один тик, били по-разному. Теперь весь счёт идёт на коммите, через
    /// <see cref="ITickLedgerSink.ResolveIncoming"/> — реестр по-прежнему владеет только применением, но
    /// момент счёта у него один на всех.
    /// </para>
    /// </remarks>
    public sealed class TickLedger
    {
        /// <summary>
        /// Заявка на урон — СЫРАЯ: во сколько она обернётся, решает коммит. Здесь лежит ровно то, что
        /// заявил источник, потому что счёт задевает состояние цели (см. <c>remarks</c> класса).
        /// </summary>
        private readonly struct DamageEntry
        {
            public readonly DamageRequest Req;

            public RuntimeUnit      Source     => Req.Source;
            public DamageSourceKind SourceKind => Req.SourceKind;
            public DamageType       Type       => Req.Type;

            public DamageEntry(in DamageRequest req) => Req = req;
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

        // Посчитанные заявки текущей цели, индекс к индексу с её списком заявок. Переиспользуется:
        // Resolve идёт по одной цели за раз, поэтому один буфер на реестр.
        private readonly List<DamageResolution> _resolved = new();

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

        /// <summary>
        /// Заявить урон по цели. Заявка сырая: броня, уязвимости и щиты цели считаются на коммите, поэтому
        /// порог значимости применяется тоже там — здесь ещё неизвестно, во что удар обернётся.
        /// </summary>
        public void AddDamage(RuntimeUnit target, in DamageRequest req)
        {
            if (target == null || target.IsDead || req.RawDamage <= 0f) return;

            if (!_damage.TryGetValue(target, out List<DamageEntry> list))
            {
                list = _damagePool.Count > 0 ? _damagePool.Pop() : new List<DamageEntry>();
                _damage[target] = list;
                Track(target);
            }

            list.Add(new DamageEntry(in req));
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

            // Счёт всех заявок цели — здесь и только здесь. Внутри одной цели заявки идут в порядке
            // добавления: он детерминирован и у зеркальных сторон эквивалентен (i-й враг бьёт i-го).
            // Порог значимости — тоже здесь: до счёта неизвестно, что от удара останется после брони
            // и щитов, а обрезать надо именно хвост отражения.
            _resolved.Clear();
            float totalDamage = 0f;
            if (hits != null)
            {
                for (int i = 0; i < hits.Count; i++)
                {
                    DamageEntry entry = hits[i];
                    DamageResolution r = sink.ResolveIncoming(target, in entry.Req);
                    if (r.Dealt < MinSignificantAmount) r = DamageResolution.None;

                    _resolved.Add(r);
                    totalDamage += r.Dealt;
                }
            }

            float totalHeal = 0f;
            if (heals != null) for (int i = 0; i < heals.Count; i++) totalHeal += heals[i].Amount;

            // 1. Щит поглощает весь урон тика разом, остаток уходит в HP.
            float shieldAbsorbed = Mathf.Min(target.CurrentShield, totalDamage);
            target.CurrentShield -= shieldAbsorbed;
            float hpDamage = totalDamage - shieldAbsorbed;

            // Поглощение уходит наружу отдельным сообщением: пул щита общий, а держат его конкретные
            // эффекты конкретных авторов, и разложить поглощённое обратно по держателям может только тот,
            // кто их видит. Без этого щит остаётся работой без исполнителя — «сколько он заблокировал»
            // не отвечалось вовсе, а у кита на щитах вся поддержка читалась нулём.
            if (shieldAbsorbed > 0f) sink.OnShieldAbsorbed(target, shieldAbsorbed);

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
                RuntimeUnit killer = killed ? Killer(hits, _resolved) : null;

                for (int i = 0; i < hits.Count; i++)
                {
                    DamageEntry e = hits[i];
                    DamageResolution r = _resolved[i];
                    if (r.Dealt <= 0f) continue;   // негейт или обрезанный хвост: события такой удар не поднимает

                    float share = r.Dealt / totalDamage;
                    var result = new DamageResult(
                        hpDamage * share, shieldAbsorbed * share,
                        killed && ReferenceEquals(e.Source, killer),
                        e.SourceKind, e.Type, r.Vulnerability,
                        r.Mitigated);   // срезанное принадлежит своему удару целиком, делить его не надо

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
        private static RuntimeUnit Killer(List<DamageEntry> hits, List<DamageResolution> resolved)
        {
            RuntimeUnit best = null;
            float bestAmount = -1f;

            for (int i = 0; i < hits.Count; i++)
            {
                RuntimeUnit source = hits[i].Source;
                if (source == null) continue;

                float amount = resolved[i].Dealt;
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
    /// Во что обернулась одна заявка после защит цели: сколько дошло, сколько срезано, каким множителем
    /// цель усилила урон по себе. <see cref="None"/> — удар не дошёл вовсе (негейт или ничтожный хвост).
    /// </summary>
    public readonly struct DamageResolution
    {
        /// <summary>Эффективный урон, который дойдёт до щита и HP.</summary>
        public readonly float Dealt;

        /// <summary>Сколько срезала защита — для разбора «чем боец не умер».</summary>
        public readonly float Mitigated;

        /// <summary>Уязвимость цели, применённая к этому удару (1 = без изменений). Идёт в отчёты.</summary>
        public readonly float Vulnerability;

        public DamageResolution(float dealt, float mitigated, float vulnerability)
        {
            Dealt         = dealt;
            Mitigated     = mitigated;
            Vulnerability = vulnerability;
        }

        /// <summary>Удар не дошёл: ни урона, ни событий. Уязвимость нейтральная — отчётам делить нечего.</summary>
        public static DamageResolution None => new DamageResolution(0f, 0f, 1f);
    }

    /// <summary>
    /// Куда реестр отдаёт разрешённые изменения: события наружу и внутренние события для реактивов.
    /// Реализует <c>CombatSimulation</c> — она и остаётся единственной точкой мутации мира.
    /// </summary>
    public interface ITickLedgerSink
    {
        /// <summary>
        /// Посчитать одну заявку по состоянию МОМЕНТА КОММИТА: pre-damage цели, уязвимости, овертайм,
        /// броня. Зовётся реестром, потому что счёт задевает состояние цели (запас щита, накопитель
        /// поглощённого, заряды) — делать это в момент заявки значило бы менять мир посреди фазы решений.
        /// </summary>
        DamageResolution ResolveIncoming(RuntimeUnit target, in DamageRequest req);

        /// <summary>Урон дошёл до цели: доля этого источника в общем ударе тика.</summary>
        void OnDamageResolved(RuntimeUnit source, RuntimeUnit target, in DamageResult result);

        /// <summary>
        /// Щит цели поглотил часть урона этого тика. Реестр знает только величину — кто этот щит держал,
        /// видит сим, и разложить поглощённое по авторам он обязан здесь же, пока состав щитов не изменился.
        /// </summary>
        void OnShieldAbsorbed(RuntimeUnit target, float absorbed);

        /// <summary>Лечение дошло до цели: доля этого источника в фактически вылеченном (overheal не входит).</summary>
        void OnHealResolved(RuntimeUnit source, RuntimeUnit target, float applied);
    }
}
