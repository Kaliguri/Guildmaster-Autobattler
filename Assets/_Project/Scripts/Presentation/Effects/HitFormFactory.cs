using Guildmaster.Data.Definitions;
using Guildmaster.Presentation.Design;
using UnityEngine;

namespace Guildmaster.Presentation.Effects
{
    /// <summary>
    /// Сборка формы удара из канона: тип урона выбирает архетип, вес удара задаёт размер, сид разводит
    /// два одинаковых удара. Чистая функция — ничего не спавнит и ни на что не подписана, поэтому её
    /// правила можно проверить тестом, а не глазами в бою.
    /// </summary>
    public static class HitFormFactory
    {
        /// <summary>
        /// Какой архетип формы несёт этот удар. Форма есть у КАЖДОЙ автоатаки — вопрос только в том, какая.
        /// </summary>
        /// <param name="type">Тип урона источника.</param>
        /// <param name="ranged">Бьёт ли юнит снарядом (<c>AttackType.Ranged</c> в его SO).</param>
        /// <param name="kind">Архетип формы.</param>
        /// <returns>
        /// <c>false</c> — архетип НЕВЫРАЗИМ этими данными, и это дефект контента, а не «эффекта нет».
        /// Ловится <c>HitFormCoverageTests</c> ещё до боя; в рантайме вызывающий обязан шуметь в консоль.
        /// </returns>
        /// <remarks>
        /// <b>Архетип приходит из ДАННЫХ, а не из дефолта.</b> Два источника, оба в SO юнита:
        /// <list type="bullet">
        /// <item><b>Дальний бой</b> — из <c>AttackType.Ranged</c>: у выстрела форма одна на всех, линия-всполох
        /// (канон <c>gdd/70-gamefeel/vfx-language</c> §Дальний бой). Тип урона тут не спрашивают вовсе:
        /// у стрелы нет взмаха, и серп ей не положен, даже когда она режущая.</item>
        /// <item><b>Ближний бой</b> — из <see cref="DamageType"/>, но только потому, что три физических типа
        /// ПРЯМО НАЗЫВАЮТ способ доставки: рубанули, укололи, ударили тяжёлым. Это не «школа решает форму»,
        /// а единственный случай, когда школа и способ совпадают.</item>
        /// </list>
        /// Всё остальное в ближнем бою — магия, свет, тьма, яд, кровь — способ доставки НЕ называет:
        /// «ледяной удар» не говорит, посохом бьют или когтем. Подставлять такому режущего по умолчанию
        /// нельзя (прямой запрет Макса 01.08.2026): дефолт молча назначает язык вместо автора. Сегодня
        /// таких юнитов в контенте нет — все двадцать семь ближников физические, — а появится, и его
        /// архетип придётся объявить: полем в <c>UnitData</c> либо типом урона, который назовёт способ.
        /// <para>Безоружный удар — <b>дробящий</b> (Макс, 01.08.2026): кулак это тяжёлое, а не острое.</para>
        /// </remarks>
        public static bool ResolveKind(DamageType type, bool ranged, out HitFormKind kind)
        {
            if (ranged)
            {
                kind = HitFormKind.Bolt;
                return true;
            }

            switch (type)
            {
                case DamageType.Slash:  kind = HitFormKind.Slash;  return true;
                case DamageType.Pierce: kind = HitFormKind.Pierce; return true;
                case DamageType.Blunt:  kind = HitFormKind.Blunt;  return true;

                default:
                    kind = HitFormKind.Slash;
                    return false;
            }
        }

        /// <summary>
        /// Собрать параметры формы. Все размеры считаются от роста юнита-человека и множителя веса удара;
        /// прогиб, толщина и лучи выбираются внутри коридоров архетипа по <paramref name="seed"/>.
        /// </summary>
        /// <param name="feel">Feel-конфиг: числа архетипов, жизнь формы, коридор размера.</param>
        /// <param name="kind">Архетип.</param>
        /// <param name="from">Точка A — откуда пришёл удар.</param>
        /// <param name="to">Точка B — точка попадания.</param>
        /// <param name="hpDamageFrac">Доля максимального HP цели, снятая ударом, — вес удара.</param>
        /// <param name="core">Цвет пересвета ядра.</param>
        /// <param name="rim">Цвет каймы — палитра бьющего.</param>
        /// <param name="seed">
        /// Сид вариации. Обязан быть выведен из данных боевого события, а не из <c>Random</c> и не из
        /// <c>IRngService</c>: первый разошёлся бы между клиентами кооператива, а второй — сам поток
        /// случайности симуляции, и вычерпывать его показом значит менять ход боя от того, что кто-то
        /// смотрит на экран.
        /// </param>
        /// <param name="endsAtHit">Форма кончается в цели: дробящий либо удар, принятый щитом.</param>
        /// <param name="freezeSeconds">Окно hitstop той же пары — столько форма стоит замороженной.</param>
        public static HitFormParams Build(CombatFeelConfig feel, HitFormKind kind,
            Vector3 from, Vector3 to, float hpDamageFrac, Color core, Color rim,
            uint seed, bool endsAtHit, float freezeSeconds)
        {
            HitFormArchetypeConfig a = feel.HitFormArchetype(kind);
            float h = Mathf.Max(0.01f, feel.HitFormUnitHeight);
            float weight = feel.EvaluateHitFormSize(hpDamageFrac);

            // Три независимых потока из одного сида: иначе толстая форма всегда была бы и самой выгнутой.
            float r1 = Unit01(seed, 0x9E3779B9u);
            float r2 = Unit01(seed, 0x85EBCA6Bu);
            float r3 = Unit01(seed, 0xC2B2AE35u);

            // Длина архетипа в долях роста — у ВСЕХ четырёх, включая линию-всполох (04.08.2026). Прежде
            // всполох брал длину из дистанции выстрела, и с четырёх единиц полёта росчерк выходил в восемь:
            // знак попадания превращался в линию через полэкрана. «Откуда прилетело» говорит направление
            // A→B, а не размер. Нулевая длина в архетипе оставлена рабочей: она возвращает прежнее правило
            // для того, кому оно понадобится, — но по умолчанию его больше нет ни у кого.
            float length = a.LengthH > 0f
                ? a.LengthH * h * weight
                : Mathf.Max(0.01f, Vector3.Distance(from, to)) * (endsAtHit ? 1f : 2f);

            // КОСА ОБЯЗАНА ПРОЙТИ ПУТЬ КЛИНКА (решение Макса 05.08.2026): от точки начала замаха до
            // точки хита, минимум. Архетипная длина остаётся полом на случай короткого замаха, но
            // перестаёт быть потолком: замах длиннее — растёт и серп, иначе он обрывается на полпути
            // и удар читается как «дотянулся», хотя клинок прошёл всю дугу.
            //
            // Длина равна пути РОВНО, без удвоения (05.08.2026, «понизь длину слеша в 2 раза»): коса
            // теперь лежит НА отрезке A→B, а не центрируется в точке хита. Прежняя двойка была честной
            // геометрией для центра в B — до точки A дотягивалась половина, — но вторая половина при
            // этом улетала за спину цели, куда клинок не приходил. Замер на живом префабе: |AB| = 2.48
            // при росте юнита 1.6, то есть серп выходил почти в три роста, из них полтора за целью.
            // Центрирование по пути живёт в HitFormVfx.Apply и держится тем же условием — Slash навылет.
            //
            // Только режущий. У колющего форма — прокол в точке хита, у дробящего — короткий след перед
            // ней; тянуть их от плеча значит соврать о способе доставки. Всполох выстрела не трогаем
            // тем более: его длина уже была отвязана от дистанции полёта 04.08.2026, иначе росчерк
            // растягивается через полэкрана.
            if (kind == HitFormKind.Slash)
                length = Mathf.Max(length, Vector3.Distance(from, to));

            float arc = Mathf.Lerp(a.ArcH.x, a.ArcH.y, r1) * h * weight;
            // Знак прогиба тоже из сида: удары подряд выгибаются в разные стороны, и штампа не выходит.
            if (r3 > 0.5f) arc = -arc;

            float halfThickness = Mathf.Lerp(a.HalfThicknessH.x, a.HalfThicknessH.y, r2) * h * weight;
            float starRadius    = a.StarRadiusH * h * weight;
            int   starRays      = Mathf.RoundToInt(Mathf.Lerp(a.StarRays.x, a.StarRays.y, r3));

            return new HitFormParams(from, to, kind, endsAtHit,
                length, halfThickness, arc, a.Roughness, starRadius, starRays,
                seed & 0xFFFFu, core, rim,
                feel.HitFormLife, feel.HitFormGrowShare, feel.HitFormTailLag, feel.HitFormCoreWidth,
                freezeSeconds);
        }

        /// <summary>
        /// Сид одного удара: одинаковый на всех клиентах, разный у соседних ударов. Собирается из того,
        /// что у показа и так есть и что у всех совпадает — участники, место и величина удара.
        /// </summary>
        public static uint SeedOf(int sourceId, int targetId, Vector2 at, float damage)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)sourceId) * 16777619u;
                h = (h ^ (uint)targetId) * 16777619u;
                // Позиция округляется до сантиметра: у клиентов она совпадает до бита (сим детерминирован),
                // но лишние разряды сида ничего не добавляют, а округление делает его читаемым в логе.
                h = (h ^ (uint)Mathf.RoundToInt(at.x * 100f)) * 16777619u;
                h = (h ^ (uint)Mathf.RoundToInt(at.y * 100f)) * 16777619u;
                h = (h ^ (uint)Mathf.RoundToInt(damage * 10f)) * 16777619u;
                return h;
            }
        }

        /// <summary>Одно число 0..1 из сида и соли — свой поток на каждый параметр.</summary>
        private static float Unit01(uint seed, uint salt)
        {
            unchecked
            {
                uint x = seed ^ salt;
                x ^= x >> 16;
                x *= 0x7FEB352Du;
                x ^= x >> 15;
                x *= 0x846CA68Bu;
                x ^= x >> 16;
                return (x & 0xFFFFFFu) / (float)0x1000000u;
            }
        }
    }
}
