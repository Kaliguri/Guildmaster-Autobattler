using System.Collections.Generic;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using UnityEngine;

namespace Guildmaster.Balance.Editor
{
    /// <summary>
    /// Слот боевого строя: какую роль он закрывает и где стоит (координаты стороны 0; сторона 1 зеркалится
    /// по X). Испытуемые киты занимают слоты СВОИХ ролей, остальные закрываются эталонными манекенами.
    /// </summary>
    internal readonly struct Slot
    {
        public readonly UnitClass Role;
        public readonly Vector2 Pos;

        public Slot(UnitClass role, float x, float y)
        {
            Role = role;
            Pos = new Vector2(x, y);
        }
    }

    /// <summary>
    /// Строи команд для командных бенчей и общая раздача бойцов по слотам. Живёт отдельно от бенчей,
    /// потому что «как выглядит команда» — общий язык для всех форматов: подмени строй, и тот же прогон
    /// отвечает на другой вопрос.
    /// </summary>
    internal static class Lineups
    {
        // Глубина строя: фронт стоит вплотную к противнику, тыл — за спинами.
        public const float FrontX = 2.2f;
        public const float BackX = 4.4f;

        /// <summary>Дуэль: один боец, роль номинальная.</summary>
        public static readonly Slot[] Solo =
        {
            new Slot(UnitClass.Bruiser, FrontX, 0f),
        };

        /// <summary>Пара: кто-то держит фронт, кто-то бьёт из-за спины.</summary>
        public static readonly Slot[] Pair =
        {
            new Slot(UnitClass.Bruiser, FrontX, 0f),
            new Slot(UnitClass.Ranged, BackX, 0f),
        };

        /// <summary>Тройка: фронт, тыл и третий по своей линии.</summary>
        public static readonly Slot[] Trio =
        {
            new Slot(UnitClass.Bruiser, FrontX, -0.6f),
            new Slot(UnitClass.Bruiser, FrontX, 0.6f),
            new Slot(UnitClass.Ranged, BackX, 0f),
        };

        /// <summary>
        /// ОТРЯД ИГРЫ — четыре бойца. Это тот размер, который игрок реально выставляет на арену, поэтому
        /// именно он главный формат оценки; всё остальное — вспомогательные линзы.
        /// </summary>
        public static readonly Slot[] Squad =
        {
            new Slot(UnitClass.Tank, FrontX, -0.6f),
            new Slot(UnitClass.Bruiser, FrontX, 0.6f),
            new Slot(UnitClass.Ranged, BackX, -0.6f),
            new Slot(UnitClass.Support, BackX, 0.6f),
        };

        /// <summary>
        /// КРИВОЙ СОСТАВ: четыре фронтовые роли, ни дальника, ни поддержки. Моделирует испытание-ограничение
        /// вроде «только ближники» (<c>meta-progression</c> §Испытания) — игрок ломает пачку ради денег.
        /// </summary>
        /// <remarks>
        /// Существует ради вопроса «сколько стоит квест»: сравнивается со <see cref="Squad"/> на том же
        /// маршруте, и разница между ними и есть цена. Отдельный строй, а не флаг в бенче, потому что
        /// «как выглядит команда» — язык этого файла: заведи такой же массив внутри бенча, и у понятия
        /// строя станет два владельца.
        /// </remarks>
        public static readonly Slot[] MeleeOnly =
        {
            new Slot(UnitClass.Tank, FrontX, -0.6f),
            new Slot(UnitClass.Bruiser, FrontX, 0.6f),
            new Slot(UnitClass.Bruiser, BackX, -0.6f),
            new Slot(UnitClass.Assassin, BackX, 0.6f),
        };

        /// <summary>
        /// КРИВАЯ РАССТАНОВКА: состав штатный, но вывернут наизнанку — танк отправлен в тыл, поддержка
        /// выставлена на острие. Роли те же, что у <see cref="Squad"/>, разница ТОЛЬКО в позициях.
        /// </summary>
        /// <remarks>
        /// Отвечает на требование «рядовой бой обязан оставаться смертельным при неправильной расстановке»
        /// (решение Макса 2026-08-21, <c>gdd/30-run-meta/injuries-mettle</c>). Без этого строя стенд знал
        /// одну раскладку на бой и на вопрос про расстановку ответить не мог — слепое пятно, записанное
        /// в самом <see cref="EncounterBench"/>.
        /// </remarks>
        public static readonly Slot[] SquadInverted =
        {
            new Slot(UnitClass.Support, FrontX, -0.6f),
            new Slot(UnitClass.Ranged, FrontX, 0.6f),
            new Slot(UnitClass.Bruiser, BackX, -0.6f),
            new Slot(UnitClass.Tank, BackX, 0.6f),
        };

        /// <summary>Шестёрка: отряд, растянутый до боя крупнее штатного (проверка, как масштабируются киты).</summary>
        public static readonly Slot[] Large =
        {
            new Slot(UnitClass.Tank, FrontX, -1.2f),
            new Slot(UnitClass.Bruiser, FrontX, 0f),
            new Slot(UnitClass.Assassin, FrontX, 1.2f),
            new Slot(UnitClass.Ranged, BackX, -1.2f),
            new Slot(UnitClass.Ranged, BackX, 0f),
            new Slot(UnitClass.Support, BackX, 1.2f),
        };

        /// <summary>Фронтовые роли держат линию, остальные работают из-за спин.</summary>
        public static bool IsFrontline(UnitClass unitClass)
            => unitClass is UnitClass.Tank or UnitClass.Bruiser or UnitClass.Assassin;

        /// <summary>
        /// Роль, чей слот занимает кит. Призыватель и целитель делят слот с поддержкой: все трое стоят
        /// в тылу и работают не автоатакой, а тем, что вокруг них происходит.
        /// </summary>
        /// <remarks>
        /// Целитель добавлен сюда 2026-08-21, и это была не мелочь: класс завели 28.07, а сопоставления
        /// со слотом ему не дали — ни один строй слота <see cref="UnitClass.Healer"/> не объявляет,
        /// поэтому Светлый пастырь и Травница не выходили на арену НИ В ОДНОМ замере. Отчёт при этом
        /// выглядел целым: слот поддержки закрывал кит роли Support, и отсутствие хила в отряде читалось
        /// как свойство ростера, а не как дыра в стенде.
        /// </remarks>
        public static UnitClass SlotRole(UnitClass unitClass)
            => unitClass is UnitClass.Summoner or UnitClass.Healer ? UnitClass.Support : unitClass;

        /// <summary>
        /// Развернуть команду: каждый кит из <paramref name="heroes"/> встаёт в свободный слот своей роли,
        /// оставшиеся слоты закрывают эталонные манекены. Возвращает индексы китов в
        /// <paramref name="tracked"/> — они же их Id, потому что SimBench раздаёт Id по порядку списка.
        /// </summary>
        /// <remarks>
        /// Роли кита в строю может не оказаться (строй короче списка классов) или слот уже мог занять
        /// напарник — тогда кит вытесняет ближайшего по линии боя: фронтовой меняет Брузера, тыловой —
        /// дальника, а если и это занято, берёт первый свободный слот. Строй всегда заполняется целиком:
        /// команды обеих сторон обязаны быть одного размера, иначе сравнение бессмысленно.
        /// </remarks>
        public static int[] SpawnTeam(SimEnvironment env, ClassBalanceConfig classes, List<TrackedUnit> tracked,
            IReadOnlyList<RelicData> heroes, int team, Slot[] lineup)
        {
            float side = team == 0 ? -1f : 1f;
            var takenBy = new int[lineup.Length];
            for (int s = 0; s < takenBy.Length; s++) takenBy[s] = -1;

            for (int h = 0; h < heroes.Count; h++)
            {
                int slot = FindSlot(lineup, takenBy, SlotRole(heroes[h].CombatClass));
                if (slot >= 0) { takenBy[slot] = h; continue; }

                // Героев больше, чем слотов: этот на арену НЕ выйдет. Молчать здесь нельзя — его
                // heroIds останется нулём, то есть будет указывать на ЧУЖОГО юнита, и бенч напечатает
                // испытуемому строку, посчитанную по чужим числам. Такой замер неотличим от честного.
                Debug.LogError($"[Lineups] «{heroes[h].name}» не получил слот в строю из {lineup.Length} "
                               + $"(героев {heroes.Count}) — на арену не выйдет, и замер по нему будет ложным.");
            }

            var heroIds = new int[heroes.Count];
            for (int s = 0; s < lineup.Length; s++)
            {
                Slot slot = lineup[s];
                var pos = new Vector2(side * slot.Pos.x, slot.Pos.y);
                int hero = takenBy[s];

                if (hero < 0)
                {
                    tracked.Add(new TrackedUnit(
                        SyntheticUnits.ReferenceAlly(slot.Role, classes, team, pos),
                        slot.Role.ToString().ToLowerInvariant(), "ally"));
                    continue;
                }

                heroIds[hero] = tracked.Count;
                tracked.Add(new TrackedUnit(env.Real(heroes[hero], null, team, pos),
                    heroes[hero].name, heroes[hero].name));
            }

            return heroIds;
        }

        private static int FindSlot(Slot[] lineup, int[] takenBy, UnitClass role)
        {
            for (int s = 0; s < lineup.Length; s++)
                if (takenBy[s] < 0 && lineup[s].Role == role) return s;

            UnitClass fallback = IsFrontline(role) ? UnitClass.Bruiser : UnitClass.Ranged;
            for (int s = 0; s < lineup.Length; s++)
                if (takenBy[s] < 0 && lineup[s].Role == fallback) return s;

            for (int s = 0; s < lineup.Length; s++)
                if (takenBy[s] < 0) return s;

            return -1;
        }
    }
}
