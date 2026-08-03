using System.Collections.Generic;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Разделение тел юнитов (вики «10» §5.1): круговые тела радиусом <c>Size × BodyRadiusPerSize</c>,
    /// мягкое позиционное расталкивание перекрытий. Это ЛОКАЛЬНОЕ избегание, а НЕ поиск пути —
    /// статические препятствия (геометрия арены / зоны способностей) будут отдельным навигационным
    /// слоем позже; персонажи друг для друга решаются здесь, расталкиванием, не перепрокладкой маршрута.
    /// <para>
    /// Детерминировано и ЗЕРКАЛЬНО: каждый юнит сам набирает все свои вклады, обходя соседей в
    /// каноническом порядке (<see cref="CompareCanonical"/>), поэтому отражённые команды считают одну и ту
    /// же сумму. Пара из-за этого считается дважды — по разу с каждой стороны; результат тот же, что у
    /// прежнего взаимного зачёта половин, но не зависит от порядка обхода. Без RNG; направление в
    /// вырожденном случае (позиции совпали) — по стороне команды и Id (<see cref="DegenerateDir"/>).
    /// Юниты в полёте (§9.9, <see cref="RuntimeUnit.DisplacedTicksRemaining"/>) ИСКЛЮЧЕНЫ из сепарации
    /// целиком — ими владеет <c>DisplacementSystem</c>, а удар летящего тела по цели/толпе делает логика
    /// броска (cannonball, урон + цепное отбрасывание), НЕ расталкивание (иначе оно спихивало бы цель с
    /// линии полёта до нанесения урона). Broad-phase через <see cref="SpatialHash"/>; итог клампится в
    /// границы арены. Место в тике: ПОСЛЕ движения/смещения, ДО ребилда хэша.
    /// </para>
    /// </summary>
    public sealed class SeparationSystem
    {
        // Тюнеры. Публичные, чтобы dev крутил вживую (gm_sep_*) без рекомпиляции. Стартовые значения
        // засевает CombatSimulation из снапшота SimTuning (SimTuningConfig); здесь — код-дефолты для
        // headless-конструирования без снапшота.
        public float BodyRadiusPerSize = SimTuning.Default.BodyRadiusPerSize;
        public float Strength          = SimTuning.Default.SeparationStrength;
        public int   Iterations        = SimTuning.Default.SeparationIterations;
        // Свои расталкиваются мягче (просачиваются к фронту), враги — на полную (линия держится).
        public float SameTeamScale     = SimTuning.Default.SeparationSameTeamScale;

        // Переиспользуемый буфер соседей — без аллокаций на горячем пути.
        private readonly List<RuntimeUnit> _neighbors = new List<RuntimeUnit>();

        // Накопленные за проход смещения (по индексу юнита в списке) — применяются ПОСЛЕ обхода всех пар.
        private Vector2[] _push = new Vector2[64];

        /// <summary>
        /// Упорядочить буфер соседей КАНОНИЧЕСКИ: сначала свои, потом чужие, внутри каждой группы по
        /// возрастанию <see cref="RuntimeUnit.Id"/>. Ключ намеренно ОТНОСИТЕЛЬНЫЙ («свой мне / чужой мне»),
        /// а не абсолютный: только такой порядок одинаков у отражённых сторон. Сортировка по одному Id этим
        /// свойством НЕ обладает — у левой команды свои Id младшие, у правой старшие, поэтому у левой
        /// выходит [свои, чужие], а у правой [чужие, свои]. Именно на это налетела первая попытка
        /// починить BAL-014.
        /// </summary>
        /// <remarks>
        /// Вставками, а НЕ <c>List.Sort</c>: тот заводит обёртку-компаратор поверх <c>Comparison</c>, то есть
        /// мусор на горячем пути (каждый юнит, каждая итерация, 30 Гц). Соседей здесь единицы — на таких
        /// длинах вставки быстрее любого introsort, не аллоцируют вовсе и устойчивы к порядку. Ключ полный
        /// (Id уникальны), поэтому итог однозначен.
        /// </remarks>
        private void SortCanonical(RuntimeUnit relativeTo)
        {
            for (int i = 1; i < _neighbors.Count; i++)
            {
                RuntimeUnit key = _neighbors[i];
                int j = i - 1;
                while (j >= 0 && Precedes(key, _neighbors[j], relativeTo))
                {
                    _neighbors[j + 1] = _neighbors[j];
                    j--;
                }
                _neighbors[j + 1] = key;
            }
        }

        /// <summary>Идёт ли <paramref name="x"/> раньше <paramref name="y"/> в каноническом порядке.</summary>
        private static bool Precedes(RuntimeUnit x, RuntimeUnit y, RuntimeUnit relativeTo)
        {
            bool xOwn = x.Team == relativeTo.Team, yOwn = y.Team == relativeTo.Team;
            if (xOwn != yOwn) return xOwn;
            return x.Id < y.Id;
        }

        /// <summary>Раздвинуть перекрывающиеся тела живых юнитов на один тик.</summary>
        /// <remarks>
        /// КАЖДАЯ ПАРА СЧИТАЕТСЯ ДВАЖДЫ, и это не расточительность, а условие зеркальности — не
        /// «оптимизировать» обратно в один проход по <c>b.Id &gt; a.Id</c>. При взаимном зачёте
        /// (<c>_push[a] += h; _push[b] -= h</c>) юнит получал часть вкладов из чужих итераций внешнего
        /// цикла, и порядок слагаемых зависел от того, старшие у соседей Id или младшие. У отражённых
        /// команд это ровно наоборот: у левого танка враги ложились в сумму ПОСЛЕ своих, у правого — ДО.
        /// Сложение float неассоциативно, поэтому суммы расходились в последнем бите (BAL-014, тик 68);
        /// никакой сортировкой соседей это не лечится, пока часть слагаемых приходит извне.
        /// Своя половина, посчитанная на своём шаге, — то же число, но в предсказуемом порядке.
        /// <para>
        /// ОБЕ половины правки необходимы, и это проверено по отдельности, а не выведено: убрать
        /// <see cref="SortCanonical"/> — расхождение возвращается на тик 68; вернуть взаимный зачёт,
        /// оставив сортировку, — возвращается туда же, бит в бит. Так что ни удвоение, ни канонический
        /// порядок не лишние.
        /// </para>
        /// </remarks>
        public void Tick(List<RuntimeUnit> units, SpatialHash hash, in ArenaBounds bounds)
        {
            if (units == null || units.Count < 2 || hash == null) return;

            // Максимальный радиус тела — чтобы broad-phase запрос гарантированно накрыл любого соседа,
            // даже более крупного (Size варьируется).
            float maxRadius = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit u = units[i];
                if (u.IsDead || u.DisplacedTicksRemaining > 0) continue;
                float r = BodyRadius(u);
                if (r > maxRadius) maxRadius = r;
            }
            if (maxRadius <= 0f) return;

            if (_push.Length < units.Count) _push = new Vector2[units.Count];

            for (int iter = 0; iter < Iterations; iter++)
            {
                for (int i = 0; i < units.Count; i++) _push[i] = Vector2.zero;

                for (int i = 0; i < units.Count; i++)
                {
                    RuntimeUnit a = units[i];
                    if (a.IsDead || a.DisplacedTicksRemaining > 0) continue; // в полёте — вне сепарации (владеет DisplacementSystem)

                    float ra = BodyRadius(a);
                    hash.QueryRadius(a.Position, ra + maxRadius, _neighbors);

                    // Порядок соседей из хэша идёт по ячейкам сетки, то есть растёт из КООРДИНАТ, а у
                    // отражённых сторон он поэтому обратный. Сложение float неассоциативно, так что от
                    // порядка зависят младшие биты суммы: ровно один такой бит разъезжался у зеркальных
                    // команд на 68-м тике и к 116-му дорастал до видимого расхождения (BAL-014).
                    SortCanonical(a);

                    for (int n = 0; n < _neighbors.Count; n++)
                    {
                        RuntimeUnit b = _neighbors[n];

                        // Себя, мёртвых и летящих пропускаем. Пара сознательно считается ДВАЖДЫ — по разу
                        // с каждой стороны: см. ремарку к Tick, взаимный зачёт половин ломал зеркальность.
                        if (ReferenceEquals(b, a) || b.IsDead || b.DisplacedTicksRemaining > 0) continue;

                        float minDist = ra + BodyRadius(b);
                        Vector2 delta = a.Position - b.Position;
                        float distSq = delta.sqrMagnitude;
                        if (distSq >= minDist * minDist) continue; // не пересекаются

                        float dist = Mathf.Sqrt(distSq);
                        Vector2 dir = dist > 1e-4f ? delta / dist : DegenerateDir(a, b);
                        // Свои — мягче (просачиваются сквозь свои ряды к фронту), враги — на полную (линия держится).
                        float pairStrength = a.Team == b.Team ? Strength * SameTeamScale : Strength;

                        // Половина перекрытия — своя доля этого юнита; вторую сосед возьмёт на своём шаге.
                        _push[i] += dir * ((minDist - dist) * pairStrength * 0.5f);
                    }
                }

                // Применяем ПОСЛЕ обхода: пока сдвиг ложился прямо в позицию, каждая следующая пара
                // считалась от уже подвинутого тела, и результат зависел от порядка соседей в хэше.
                // У зеркальных сторон этот порядок обратный, поэтому равные команды расходились
                // на первых же тиках и дальше разъезжались до разгромного счёта.
                for (int i = 0; i < units.Count; i++)
                {
                    if (_push[i] == Vector2.zero) continue;
                    RuntimeUnit u = units[i];
                    u.Position = bounds.Clamp(u.Position + _push[i]);
                }
            }
        }

        private float BodyRadius(RuntimeUnit u)
        {
            float size = u.Stats.Get(StatType.Size);
            return Mathf.Max(0.01f, size) * BodyRadiusPerSize;
        }

        /// <summary>
        /// Тела совпали точь-в-точь: геометрия направления не даёт, поэтому раздвигаем по X детерминированно
        /// И ЗЕРКАЛЬНО. Ключ — сторона команды (<see cref="FleeSteering.HomeDir"/>), а не только порядок Id:
        /// по одному Id обе отражённые пары уезжали в ОДНУ сторону, хотя зеркальность требует
        /// противоположных (остаток BAL-014).
        /// </summary>
        /// <remarks>
        /// Требований к направлению два, и оба выполняются. Антисимметрия — <c>dir(a,b) = -dir(b,a)</c>,
        /// иначе слипшиеся тела не разъедутся, а поедут вместе. Зеркальность — у отражённой пары X
        /// противоположен.
        /// <para>
        /// Враги расходятся каждый к своему тылу: антисимметрично само собой (тылы противоположны) и
        /// зеркально, потому что отражение меняет команды местами. Для этого случая поведение то же, что
        /// было. Свои разводятся по порядку Id, но со знаком СТОРОНЫ — младший к тылу, старший к фронту;
        /// именно здесь прежняя редакция и теряла зеркальность.
        /// </para>
        /// </remarks>
        private static Vector2 DegenerateDir(RuntimeUnit a, RuntimeUnit b)
        {
            Vector2 own = FleeSteering.HomeDir(a);
            if (a.Team != b.Team) return own;
            return a.Id < b.Id ? own : -own;
        }
    }
}
