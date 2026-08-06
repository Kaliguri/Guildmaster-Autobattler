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

    /// <summary>
    /// Одна зона в запросе. Центров у неё ДВА, и это главное решение модели: веса считаются по
    /// расчётному центру, а бьём вокруг живого.
    /// </summary>
    /// <remarks>
    /// Разделение появилось из-за кооператива (06.08.2026). Выбор зоны дискретен: миллиметровая разница
    /// в позе на границе весов перебрасывает удар из корпуса в ногу, и два игрока видят РАЗНЫЕ части
    /// тела. Место внутри зоны непрерывно — там та же разница даёт сдвиг на миллиметры, которого не
    /// видно. Поэтому дискретное решение считается по геометрии, одинаковой у всех (доли роста от
    /// позиции из ленты), а непрерывное — по живому якорю на кости, чтобы вспышка попадала туда, где
    /// часть тела НАРИСОВАНА.
    /// </remarks>
    public readonly struct ImpactZoneSample
    {
        /// <summary>
        /// Расчётный центр — доля роста от позиции юнита в ленте. Одинаков у всех клиентов при любой
        /// фазе анимации, поэтому от него и только от него считается вес зоны.
        /// </summary>
        public readonly Vector2 WeighAt;
        /// <summary>Живой центр — якорь на кости: следует за анимацией, поворотом и масштабом.</summary>
        public readonly Vector2 StrikeAt;
        /// <summary>Радиус зоны в мировых единицах.</summary>
        public readonly float Radius;
        /// <summary>Заявленный вес до поправки на досягаемость (сумма по всем зонам произвольна).</summary>
        public readonly float Weight;

        public ImpactZoneSample(Vector2 weighAt, Vector2 strikeAt, float radius, float weight)
        {
            WeighAt = weighAt;
            StrikeAt = strikeAt;
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
    /// <b>Выбор зоны детерминирован полностью</b> — он считается по <see cref="ImpactZoneSample.WeighAt"/>,
    /// то есть по долям роста от позиции из ленты, и не зависит ни от кадровой частоты, ни от фазы
    /// анимации. Сид удара общий (<c>HitFormFactory.SeedOf</c>), поэтому в кооперативе оба игрока
    /// получают ОДНУ зону. Место внутри зоны берётся от живого якоря и может разойтись на миллиметры —
    /// цена того, что вспышка попадает в нарисованную часть тела, а не в расчётную точку рядом с ней.
    /// </para>
    /// <para>
    /// Считать всё это в симуляции — отдельный, сознательно НЕ выбранный путь (06.08.2026): симу
    /// пришлось бы узнать геометрию фигуры, которая живёт в арте, а розыгрыш зоны из боевого потока
    /// случайности сдвинул бы все последующие броски. Переезжать туда зона обязана в тот день, когда
    /// понадобится механике — крит по голове, броня по частям, прицельные удары.
    /// </para>
    /// </remarks>
    public static class ImpactZoneSolver
    {
        /// <summary>
        /// Выбрать точку удара по телу цели.
        /// </summary>
        /// <param name="attackerWeighAt">Расчётный центр атаки — доля роста от позиции атакующего в ленте.
        /// От него считаются веса, поэтому он обязан быть одинаков у всех клиентов.</param>
        /// <param name="attackerStrikeAt">Живой центр атаки — якорь корпуса на кости; вокруг него
        /// проверяется, что точка не вышла за круг.</param>
        /// <param name="targetCentre">Позиция цели (из ленты) — ось, относительно которой считается сторона.</param>
        /// <param name="reach">Радиус круга атаки: зазор атаки плюс радиусы обоих тел (формула симуляции).</param>
        /// <param name="zones">Зоны цели; пустой массив — вернём центр цели и пометим вырожденным.</param>
        /// <param name="reachSharpness">Насколько резко недостача накрытия давит вес зоны. 1 — линейно
        /// (базовый вес почти всегда перевешивает), 2 — квадрат, 3 и выше — достижимость решает почти всё.</param>
        /// <param name="nearSideBias">Насколько сильно точка тянется к ближнему краю зоны при неполном
        /// накрытии: 0 — всегда центр зоны, 1 — вплотную к краю со стороны атакующего.</param>
        /// <param name="seed">Сид этого удара — тот же, что у формы: они обязаны совпасть местом.</param>
        public static ImpactZoneResult Solve(
            Vector2 attackerWeighAt,
            Vector2 attackerStrikeAt,
            Vector2 targetCentre,
            float reach,
            ImpactZoneSample[] zones,
            float reachSharpness,
            float nearSideBias,
            uint seed)
        {
            if (zones == null || zones.Length == 0)
                return new ImpactZoneResult(targetCentre, -1, degenerate: true);

            float sharpness = Mathf.Max(1f, reachSharpness);

            // Доля накрытия каждой зоны кругом атаки. Линейная аппроксимация пересечения двух кругов:
            // точное отношение площадей здесь не нужно — важна монотонность и то, что края дают ровно 0 и 1.
            //
            // Возведение в степень — решение Макса от 06.08.2026. Линейного множителя мало: базовый вес
            // корпуса больше веса ног в шестнадцать раз, и корпус, доступный на четверть, всё равно
            // выигрывал у ног, доступных на три четверти. Квадрат эту разницу переворачивает.
            var coverage = new float[zones.Length];
            float total = 0f;
            for (int i = 0; i < zones.Length; i++)
            {
                float d = Vector2.Distance(attackerWeighAt, zones[i].WeighAt);
                float r = zones[i].Radius;
                coverage[i] = Mathf.Clamp01((reach + r - d) / (2f * r));
                total += zones[i].Weight * Mathf.Pow(coverage[i], sharpness);
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
                    float slack = reach + zones[i].Radius - Vector2.Distance(attackerWeighAt, zones[i].WeighAt);
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
                    running += zones[i].Weight * Mathf.Pow(coverage[i], sharpness);
                    if (roll <= running) { picked = i; break; }
                }
            }

            Vector2 point = PointInZone(
                zones[picked], coverage[picked], attackerStrikeAt, targetCentre, reach, nearSideBias, ref stream);

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
            // Центр — ЖИВОЙ якорь: зону уже выбрали, теперь бьём туда, где часть тела нарисована.
            float angle = stream.NextFloat() * Mathf.PI * 2f;
            float radius = zone.Radius * Mathf.Sqrt(stream.NextFloat());
            Vector2 point = zone.StrikeAt + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

            // Достаём зону наполовину — бьём в ту половину, что ближе к себе. Отдельной ручки «смещение
            // к атакующему» нет: она и есть недостача накрытия.
            Vector2 toAttacker = attacker - zone.StrikeAt;
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
