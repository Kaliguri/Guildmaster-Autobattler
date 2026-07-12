using System.Collections.Generic;
using Guildmaster.Core.Arena;
using Guildmaster.Core.Simulation;
using UnityEngine;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Единая математика ПОБЕГА (Retreat и flee-фаза Kite): детерминированное steering-поле без RNG.
    /// Направление = взвешенная сумма трёх составляющих (вики «13» §9.7):
    /// <list type="bullet">
    ///   <item><b>Threat</b> — отталкивание от центроида ближних врагов (не одного «ближайшего»: центр масс
    ///   не даёт дёрганья «то от A, то от B»).</item>
    ///   <item><b>Home</b> — притяжение к своему тылу по <see cref="RuntimeUnit.Team"/> (0 → влево, 1 → вправо):
    ///   побег осмыслен (юнит уходит на свою сторону, под защиту), а не просто «прочь от ближайшего».</item>
    ///   <item><b>Wall</b> — превентивное отворачивание от стен арены в пределах отступа: гасит «прижимание
    ///   к стене» и «загон в угол» ДО контакта, а не клампом постфактум.</item>
    /// </list>
    /// Когда желаемое направление всё же упирается в стену (стена «переворачивала» бы курс или цель клампится) —
    /// гарантированное скольжение ВДОЛЬ стены к центру арены, чтобы юнит не замирал и не продавливался наружу.
    /// Веса и радиусы — из <see cref="SimTuning"/> (бейк, не хардкод).
    /// </summary>
    public static class FleeSteering
    {
        // Порог «желаемый шаг зажат стеной»: если кламп сдвинул цель дальше этого (кв. ед.) — упёрлись.
        private const float PinEpsilonSq = 1e-6f;

        // Ниже этого модуля вектор считаем вырожденным (нулевым) — избегаем деления и NaN.
        private const float MinDirSq = 1e-8f;

        /// <summary>
        /// Шаг ОТСТУПЛЕНИЯ (<see cref="PositioningIntent.Retreat"/>): threat + home + wall, скольжение у стены.
        /// Возвращает новую (уже клампнутую) позицию юнита.
        /// </summary>
        public static Vector2 Retreat(
            RuntimeUnit unit, IReadOnlyList<RuntimeUnit> units, float step,
            in ArenaBounds bounds, in SimTuning tuning)
        {
            Vector2 home   = HomeDir(unit);
            Vector2 threat = ThreatAwayDir(unit, units, home, in tuning);
            Vector2 away   = threat * tuning.FleeThreatWeight + home * tuning.FleeHomeWeight;
            away = Normalize(away, fallback: threat);
            return Advance(unit.Position, away, Vector2.zero, step, in bounds, in tuning);
        }

        /// <summary>
        /// Шаг ОТХОДА кайтера (flee-фаза <see cref="PositioningIntent.Kite"/>): радиальный уход от цели
        /// (<paramref name="awayDir"/>) + боковой уход (strafe, дуга вместо пятящегося отхода), скольжение у стены.
        /// Полосу дистанции держит вызывающий (капом <paramref name="step"/>); тыл здесь НЕ притягивает —
        /// кайтер удерживает дистанцию у цели, а не отступает на базу.
        /// </summary>
        public static Vector2 KiteFlee(
            Vector2 pos, Vector2 awayDir, Vector2 toTargetDir, float step,
            in ArenaBounds bounds, in SimTuning tuning)
        {
            // Боковой уход: касательная к линии на цель, ориентированная к центру арены (дрейф в открытое
            // место, прочь от стен). В свободном режиме добавляется к радиальному отходу → дуга.
            Vector2 strafe = Perpendicular(toTargetDir, towardCenterFrom: pos, bounds) * tuning.KiteStrafeWeight;
            return Advance(pos, awayDir, strafe, step, in bounds, in tuning);
        }

        // --- Ядро шага: свободный blend с избеганием стен; при заклинивании — скольжение вдоль стены ---

        private static Vector2 Advance(
            Vector2 pos, Vector2 awayDir, Vector2 strafe, float step,
            in ArenaBounds bounds, in SimTuning tuning)
        {
            if (step <= 0f) return pos;
            if (awayDir.sqrMagnitude < MinDirSq) return pos; // некуда бежать — стоим (безопасно)

            Vector2 wall = WallPush(pos, in bounds, in tuning) * tuning.FleeWallWeight;
            Vector2 free = Normalize(awayDir + strafe + wall, fallback: awayDir);

            // Свободный режим: стена лишь подкручивает курс (сохраняется положительная проекция на awayDir),
            // и шаг не клампится. Иначе (стена развернула бы курс ОТ желаемого, либо цель упёрлась) —
            // переходим к скольжению вдоль стены по ЧИСТОМУ awayDir (без strafe: у стены важна только
            // касательная вдоль неё, боковой уход тут дал бы паразитную нормальную составляющую).
            if (Vector2.Dot(free, awayDir) > 0f)
            {
                Vector2 desired = pos + free * step;
                Vector2 clamped = bounds.Clamp(desired);
                if ((clamped - desired).sqrMagnitude < PinEpsilonSq) return clamped;
            }

            Vector2 tangent = Perpendicular(awayDir, towardCenterFrom: pos, bounds);
            if (tangent.sqrMagnitude < MinDirSq) return bounds.Clamp(pos + awayDir * step); // вырождение — хоть куда
            return bounds.Clamp(pos + tangent * step);
        }

        // --- Составляющие направления ---

        /// <summary>Направление «к своему тылу» по команде: 0 (союзники, левая сторона) → влево, иначе вправо.</summary>
        private static Vector2 HomeDir(RuntimeUnit unit) => unit.Team == 0 ? Vector2.left : Vector2.right;

        /// <summary>
        /// Единичное направление «прочь от угрозы»: от центроида врагов в радиусе <see cref="SimTuning.FleeThreatRadius"/>;
        /// если в радиусе никого — от ближайшего врага; если врагов нет вовсе — по <paramref name="homeFallback"/>.
        /// Детерминизм: обход в порядке списка, тай-брейк ближайшего по Id.
        /// </summary>
        private static Vector2 ThreatAwayDir(
            RuntimeUnit unit, IReadOnlyList<RuntimeUnit> units, Vector2 homeFallback, in SimTuning tuning)
        {
            float r2 = tuning.FleeThreatRadius * tuning.FleeThreatRadius;
            Vector2 sum = Vector2.zero;
            int count = 0;
            RuntimeUnit nearest = null;
            float bestSq = float.MaxValue;

            for (int i = 0; i < units.Count; i++)
            {
                RuntimeUnit o = units[i];
                if (o.IsDead || o.Team == unit.Team) continue;

                float sq = (o.Position - unit.Position).sqrMagnitude;
                if (sq < bestSq || (sq == bestSq && (nearest == null || o.Id < nearest.Id)))
                {
                    bestSq = sq;
                    nearest = o;
                }
                if (sq <= r2) { sum += o.Position; count++; }
            }

            if (nearest == null) return homeFallback; // врагов нет — уходим к тылу

            Vector2 reference = count > 0 ? sum / count : nearest.Position;
            Vector2 away = unit.Position - reference;
            if (away.sqrMagnitude < MinDirSq) away = unit.Position - nearest.Position; // центроид на нас — от ближайшего
            return Normalize(away, fallback: homeFallback);
        }

        // --- Стены ---

        /// <summary>
        /// Суммарный вектор отталкивания от стен арены: для каждой из четырёх сторон в пределах
        /// <see cref="SimTuning.FleeWallMargin"/> добавляется нормаль ВНУТРЬ с линейным нарастанием (0 на границе
        /// отступа → 1 у стены). В углу два нормаля складываются в диагональ к центру. Unbounded (бесконечный
        /// размер) → расстояния до стен бесконечны → нулевой вектор (без NaN).
        /// </summary>
        private static Vector2 WallPush(Vector2 pos, in ArenaBounds bounds, in SimTuning tuning)
        {
            float m = tuning.FleeWallMargin;
            if (m <= 0f) return Vector2.zero;

            Rect2D rect = bounds.Rect;
            Vector2 c = rect.Center;
            Vector2 h = rect.HalfSize;
            Vector2 push = Vector2.zero;

            float dLeft   = pos.x - (c.x - h.x); if (dLeft   < m) push.x += (m - dLeft)   / m;
            float dRight  = (c.x + h.x) - pos.x; if (dRight  < m) push.x -= (m - dRight)  / m;
            float dBottom = pos.y - (c.y - h.y); if (dBottom < m) push.y += (m - dBottom) / m;
            float dTop    = (c.y + h.y) - pos.y; if (dTop    < m) push.y -= (m - dTop)    / m;

            return push;
        }

        // --- Векторные утилиты ---

        /// <summary>Единичная касательная к <paramref name="dir"/>, ориентированная в сторону центра арены.</summary>
        private static Vector2 Perpendicular(Vector2 dir, Vector2 towardCenterFrom, in ArenaBounds bounds)
        {
            Vector2 tangent = new Vector2(-dir.y, dir.x);
            if (Vector2.Dot(tangent, bounds.Center - towardCenterFrom) < 0f) tangent = -tangent;
            return tangent.sqrMagnitude < MinDirSq ? Vector2.zero : tangent.normalized;
        }

        private static Vector2 Normalize(Vector2 v, Vector2 fallback)
        {
            if (v.sqrMagnitude >= MinDirSq) return v.normalized;
            return fallback.sqrMagnitude >= MinDirSq ? fallback.normalized : Vector2.zero;
        }
    }
}
