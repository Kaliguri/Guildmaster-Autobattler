using UnityEngine;

namespace Guildmaster.Presentation.Effects
{
    /// <summary>Зона тела, куда пришёл удар. Порядок соответствует индексам в <see cref="ImpactZoneSolver"/>.</summary>
    public enum ImpactZoneKind
    {
        /// <summary>Голова, шея, щека — редкое и заметное попадание.</summary>
        Head = 0,
        /// <summary>Корпус — основная масса ударов.</summary>
        Body = 1,
        /// <summary>Верх ног, бёдра — низкие удары и всё, до чего дотягиваются снизу.</summary>
        Legs = 2,
    }

    /// <summary>Одна зона в запросе: где её центр, насколько она велика и какой у неё ЗАЯВЛЕННЫЙ вес.</summary>
    public readonly struct ImpactZoneSample
    {
        /// <summary>Мировой центр зоны — якорь на кости, поэтому следует за анимацией и масштабом.</summary>
        public readonly Vector2 Anchor;
        /// <summary>Радиус зоны в мировых единицах.</summary>
        public readonly float Radius;
        /// <summary>Заявленный вес до поправки на досягаемость (сумма по всем зонам произвольна).</summary>
        public readonly float Weight;

        public ImpactZoneSample(Vector2 anchor, float radius, float weight)
        {
            Anchor = anchor;
            Radius = Mathf.Max(0.001f, radius);
            Weight = Mathf.Max(0f, weight);
        }
    }

    /// <summary>Что решил солвер: куда пришёл удар, в какую зону и не пришлось ли выкручиваться.</summary>
    public readonly struct ImpactZoneResult
    {
        /// <summary>Мировая точка удара.</summary>
        public readonly Vector2 Point;
        /// <summary>Индекс выбранной зоны в переданном массиве.</summary>
        public readonly int ZoneIndex;
        /// <summary>
        /// Ни одна зона не оказалась достижимой, и точка выбрана «наименее плохой».
        /// Это НЕ штатный режим: показ и симуляция считают досягаемость одной формулой, поэтому
        /// засчитанный бою удар обязан находить зону. Вызывающий обязан на это громко ругаться.
        /// </summary>
        public readonly bool Degenerate;

        public ImpactZoneResult(Vector2 point, int zoneIndex, bool degenerate)
        {
            Point = point;
            ZoneIndex = zoneIndex;
            Degenerate = degenerate;
        }
    }

    /// <summary>
    /// Выбирает ТОЧКУ УДАРА на теле цели: сначала зону (заявленный вес, поправленный на досягаемость),
    /// затем место внутри неё. Чистая детерминированная математика — ни сцены, ни времени, ни RNG-сервиса.
    /// </summary>
    /// <remarks>
    /// Модель принята 2026-08-06 (ГД-журнал <c>2026-08-06/7</c>). Три вещи в ней неочевидны и держат всё
    /// остальное:
    /// <list type="number">
    /// <item><b>Досягаемость владеет распределением, а не украшает его.</b> Заявленные 80/15/5 — это база;
    /// вес зоны множится на долю её накрытия кругом атаки. Отсюда бесплатно берётся «мечник у великана
    /// бьёт по ногам»: до корпуса он не дотягивается, и корпус получает вес около нуля. Выключатель
    /// «доступна / нет» отвергнут — зона на границе мерцала бы от шага юнита.</item>
    /// <item><b>Круг атаки приходит СНАРУЖИ и считается формулой симуляции</b>
    /// (<c>AttackRange</c> как зазор между поверхностями плюс радиусы тел). Из совпадения формул следует
    /// гарантия: засчитанный бою удар всегда достаёт до ближайшей точки тела. Своя метрика дальности
    /// здесь завела бы второго владельца факта и разошлась бы с боем.</item>
    /// <item><b>Оси разведены.</b> Вертикаль принадлежит зонам (голова / корпус / ноги), горизонталь —
    /// стороне: удар приходит в ближнюю к атакующему половину фигуры. Деление на восемь сторон света
    /// рассматривалось и отвергнуто: у плоского спрайта нет глубины, а секторы «сверху/снизу» спорили бы
    /// с зонами за одну ось.</item>
    /// </list>
    /// <para>
    /// Позиции обязаны приходить <b>из ленты показа</b>, а не из трансформов живых видов: трансформы
    /// интерполируются под кадровую частоту, и в кооперативе у двух игроков разойдутся веса, а с ними и
    /// выбранная зона. Сид удара общий (<c>HitFormFactory.SeedOf</c>), поэтому при равных входах решение
    /// одинаково на всех машинах.
    /// </para>
    /// </remarks>
    public static class ImpactZoneSolver
    {
        /// <summary>
        /// Выбрать точку удара по телу цели.
        /// </summary>
        /// <param name="attacker">Позиция атакующего (из ленты).</param>
        /// <param name="targetCentre">Позиция цели (из ленты) — ось, относительно которой считается сторона.</param>
        /// <param name="reach">Радиус круга атаки от <paramref name="attacker"/>: зазор атаки плюс радиус его тела.</param>
        /// <param name="zones">Зоны цели; пустой массив — вернём центр цели и пометим вырожденным.</param>
        /// <param name="nearSideBias">Насколько сильно точка тянется к ближнему краю зоны при неполном
        /// накрытии: 0 — всегда центр зоны, 1 — вплотную к краю со стороны атакующего.</param>
        /// <param name="seed">Сид этого удара — тот же, что у формы: они обязаны совпасть местом.</param>
        public static ImpactZoneResult Solve(
            Vector2 attacker,
            Vector2 targetCentre,
            float reach,
            ImpactZoneSample[] zones,
            float nearSideBias,
            uint seed)
        {
            if (zones == null || zones.Length == 0)
                return new ImpactZoneResult(targetCentre, -1, degenerate: true);

            // Доля накрытия каждой зоны кругом атаки. Линейная аппроксимация пересечения двух кругов:
            // точное отношение площадей здесь не нужно — важна монотонность и то, что края дают ровно 0 и 1.
            var coverage = new float[zones.Length];
            float total = 0f;
            for (int i = 0; i < zones.Length; i++)
            {
                float d = Vector2.Distance(attacker, zones[i].Anchor);
                float r = zones[i].Radius;
                coverage[i] = Mathf.Clamp01((reach + r - d) / (2f * r));
                total += zones[i].Weight * coverage[i];
            }

            var stream = new SeedStream(seed);
            int picked;
            bool degenerate = false;

            if (total <= 1e-6f)
            {
                // Ни одна зона не достижима. Бьём в самую близкую к кругу атаки — но это признак поломки,
                // а не режим работы: вызывающий обязан заорать (см. ImpactZoneResult.Degenerate).
                degenerate = true;
                picked = 0;
                float best = float.NegativeInfinity;
                for (int i = 0; i < zones.Length; i++)
                {
                    float slack = reach + zones[i].Radius - Vector2.Distance(attacker, zones[i].Anchor);
                    if (slack > best) { best = slack; picked = i; }
                }
            }
            else
            {
                float roll = stream.NextFloat() * total;
                picked = zones.Length - 1;
                float running = 0f;
                for (int i = 0; i < zones.Length; i++)
                {
                    running += zones[i].Weight * coverage[i];
                    if (roll <= running) { picked = i; break; }
                }
            }

            Vector2 point = PointInZone(
                zones[picked], coverage[picked], attacker, targetCentre, reach, nearSideBias, ref stream);

            return new ImpactZoneResult(point, picked, degenerate);
        }

        /// <summary>
        /// Место внутри зоны: равномерно по кругу, со сдвигом к атакующему тем большим, чем хуже зона
        /// накрыта, затем отражение с дальней половины фигуры и жёсткое поджатие в круг атаки.
        /// </summary>
        private static Vector2 PointInZone(
            in ImpactZoneSample zone,
            float coverage,
            Vector2 attacker,
            Vector2 targetCentre,
            float reach,
            float nearSideBias,
            ref SeedStream stream)
        {
            // Равномерно ПО ПЛОЩАДИ: без корня точки сбились бы к центру и зона читалась бы как точка.
            float angle = stream.NextFloat() * Mathf.PI * 2f;
            float radius = zone.Radius * Mathf.Sqrt(stream.NextFloat());
            Vector2 point = zone.Anchor + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

            // Достаём зону наполовину — бьём в ту половину, что ближе к себе. Отдельной ручки «смещение
            // к атакующему» нет: она и есть недостача накрытия.
            Vector2 toAttacker = attacker - zone.Anchor;
            if (toAttacker.sqrMagnitude > 1e-8f)
            {
                point += toAttacker.normalized
                         * ((1f - coverage) * zone.Radius * Mathf.Clamp01(nearSideBias));
            }

            // Дальний край силуэта запрещён ВСЕМ, включая стрелков: стрела, вошедшая в спину при выстреле
            // в лицо, читается как баг. Отсекаем ТОЛЬКО по горизонтали — вертикаль принадлежит зонам, и
            // наклонная отсечка утащила бы голову вниз при ударе снизу.
            float side = attacker.x - targetCentre.x;
            if (Mathf.Abs(side) > 1e-4f)
            {
                float offset = (point.x - targetCentre.x) * Mathf.Sign(side);
                if (offset < 0f) point.x = targetCentre.x - (point.x - targetCentre.x);
            }

            // Последним — жёсткая гарантия «бьём только туда, куда достаём»: точку, выпавшую за круг
            // атаки, подтягиваем на его границу. Так достижимость не только статистика, но и закон.
            Vector2 fromAttacker = point - attacker;
            float dist = fromAttacker.magnitude;
            if (dist > reach && dist > 1e-6f)
                point = attacker + fromAttacker * (reach / dist);

            return point;
        }

        /// <summary>
        /// Поток дробей из сида удара. Xorshift, а не <c>System.Random</c>: нужна не статистика, а
        /// одинаковый ответ на всех машинах кооператива при одинаковом сиде.
        /// </summary>
        private struct SeedStream
        {
            private uint _state;

            public SeedStream(uint seed) => _state = seed != 0u ? seed : 0x9E3779B9u;

            /// <summary>Следующая дробь в [0, 1).</summary>
            public float NextFloat()
            {
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                return (_state >> 8) * (1f / 16777216f);   // 24 старших бита — ровно мантисса float
            }
        }
    }
}
