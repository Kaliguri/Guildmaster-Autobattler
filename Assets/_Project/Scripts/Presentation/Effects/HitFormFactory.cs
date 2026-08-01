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
        /// Какой архетип формы несёт этот удар.
        /// </summary>
        /// <param name="type">Тип урона источника — он же говорит, ЧЕМ ударили.</param>
        /// <param name="ranged">Удар пришёл снарядом.</param>
        /// <param name="kind">Архетип формы.</param>
        /// <returns>
        /// <c>false</c> — формы у этого удара НЕТ, и это не пробел, а решение. У магии, света и тьмы нет
        /// клинка, которым машут: «след лезвия» был бы для них враньём. Свой знак им положен —
        /// кольцо, всплеск, луч, — но это отдельная строка словаря событий, и её ещё не написали.
        /// </returns>
        public static bool TryResolveKind(DamageType type, bool ranged, out HitFormKind kind)
        {
            // Дальний бой отвечает раньше типа: у выстрела нет взмаха, поэтому серп ему не положен даже
            // тогда, когда стрела режущая. Форма говорит, КАК доставили.
            if (ranged)
            {
                kind = HitFormKind.Bolt;
                return true;
            }

            switch (type)
            {
                case DamageType.Slash:
                    kind = HitFormKind.Slash;
                    return true;

                case DamageType.Pierce:
                    kind = HitFormKind.Pierce;
                    return true;

                case DamageType.Blunt:
                    kind = HitFormKind.Blunt;
                    return true;

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

            // Длина не нормируется только у линии-всполоха: её длина — вся дистанция выстрела, и она сама
            // по себе сообщение «прилетело оттуда».
            float length = a.LengthH > 0f
                ? a.LengthH * h * weight
                : Mathf.Max(0.01f, Vector3.Distance(from, to)) * (endsAtHit ? 1f : 2f);

            float arc = Mathf.Lerp(a.ArcH.x, a.ArcH.y, r1) * h * weight;
            // Знак прогиба тоже из сида: удары подряд выгибаются в разные стороны, и штампа не выходит.
            if (r3 > 0.5f) arc = -arc;

            float halfThickness = Mathf.Lerp(a.HalfThicknessH.x, a.HalfThicknessH.y, r2) * h * weight;
            float starRadius    = a.StarRadiusH * h * weight;
            int   starRays      = Mathf.RoundToInt(Mathf.Lerp(a.StarRays.x, a.StarRays.y, r3));

            return new HitFormParams(from, to, kind, endsAtHit,
                length, halfThickness, arc, a.Roughness, starRadius, starRays,
                seed & 0xFFFFu, core, rim,
                feel.HitFormLife, feel.HitFormGrowShare, feel.HitFormCoreWidth, freezeSeconds);
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
