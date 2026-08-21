using System;
using System.Collections.Generic;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Занятые слоты травм одного «Сосуда»: сколько ушибов, ран и увечий на нём висит.
    /// Считается из <see cref="RosterSlot.InjuryIds"/> — своего счётчика в состоянии забега нет
    /// намеренно, он был бы вторым владельцем того же факта.
    /// </summary>
    public readonly struct InjurySlots
    {
        public readonly int Bruises;
        public readonly int Wounds;
        public readonly int Maimings;

        public InjurySlots(int bruises, int wounds, int maimings)
        {
            Bruises  = bruises;
            Wounds   = wounds;
            Maimings = maimings;
        }

        /// <summary>Сколько слотов этой ступени уже занято.</summary>
        public int Occupied(InjuryGrade grade) => grade switch
        {
            InjuryGrade.Bruise  => Bruises,
            InjuryGrade.Wound   => Wounds,
            InjuryGrade.Maiming => Maimings,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), grade, "Неизвестная ступень травмы."),
        };

        /// <summary>Свободных слотов этой ступени (не меньше нуля).</summary>
        public int Free(InjuryGrade grade) => Math.Max(0, InjuryCascade.Capacity(grade) - Occupied(grade));

        /// <summary>Те же слоты плюс одна травма названной ступени.</summary>
        public InjurySlots With(InjuryGrade grade) => grade switch
        {
            InjuryGrade.Bruise  => new InjurySlots(Bruises + 1, Wounds, Maimings),
            InjuryGrade.Wound   => new InjurySlots(Bruises, Wounds + 1, Maimings),
            InjuryGrade.Maiming => new InjurySlots(Bruises, Wounds, Maimings + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(grade), grade, "Неизвестная ступень травмы."),
        };

        /// <summary>Пересчитать занятость по списку ступеней (закалки в список не попадают).</summary>
        public static InjurySlots Of(IReadOnlyList<InjuryGrade> grades)
        {
            if (grades == null) return default;

            int bruises = 0, wounds = 0, maimings = 0;
            for (int i = 0; i < grades.Count; i++)
            {
                switch (grades[i])
                {
                    case InjuryGrade.Bruise:  bruises++;  break;
                    case InjuryGrade.Wound:   wounds++;   break;
                    case InjuryGrade.Maiming: maimings++; break;
                }
            }

            return new InjurySlots(bruises, wounds, maimings);
        }

        public override string ToString() => $"{Bruises}/{Wounds}/{Maimings}";
    }

    /// <summary>Чем кончилась попытка положить травму на «Сосуд».</summary>
    public readonly struct InjuryOutcome
    {
        /// <summary>Какую ступень просил источник (смерть в бою обычно просит ушиб).</summary>
        public readonly InjuryGrade Requested;

        /// <summary>Какая ступень легла на самом деле. При <see cref="Retired"/> не читается.</summary>
        public readonly InjuryGrade Grade;

        /// <summary>Слотов не осталось нигде: «Сосуд» выбывает из забега, травма не кладётся.</summary>
        public readonly bool Retired;

        public InjuryOutcome(InjuryGrade requested, InjuryGrade grade, bool retired)
        {
            Requested = requested;
            Grade     = grade;
            Retired   = retired;
        }

        /// <summary>Ступень поднялась переполнением — просили мелкую, легла тяжелее.</summary>
        public bool Escalated => !Retired && Grade > Requested;
    }

    /// <summary>
    /// Каскад травм: куда ляжет очередная рана и что будет, когда класть уже некуда.
    /// <para>Правило (решение Макса 2026-08-21, ГДД <c>injuries-mettle</c> §Травма): слотов
    /// <b>3 мелких, 2 средних, 1 тяжёлая</b>. Травма, которой некуда лечь, <b>поднимается на ступень</b>;
    /// когда занят и единственный тяжёлый слот — «Сосуд» выбывает из забега.</para>
    /// </summary>
    /// <remarks>
    /// Каскад — вся суть системы: он превращает «ещё одна смерть» из линейного налога в растущую
    /// угрозу. Поэтому логика лежит отдельно от состояния забега и не знает ни про Unity, ни про
    /// ассеты: она проверяется быстрым прогоном и одинаково зовётся игрой и балансным стендом.
    /// <para>Числа слотов — константы, а не поля конфига: это правило игры, а не крутилка баланса.
    /// Меняются они вместе с самим правилом (и с текстом ГДД), а не подбором в ассете. Понадобится
    /// крутить — вынесем в <c>GameConfig</c> одним движением.</para>
    /// <para><b>Владелец правды — этот класс.</b> Модель <c>WoundSheet</c> в бенче забега
    /// (<c>RunBench</c>) писалась до него и обязана быть переведена сюда: две копии каскада разойдутся
    /// молча, и стенд начнёт мерить не ту игру, которую мы делаем.</para>
    /// </remarks>
    public static class InjuryCascade
    {
        public const int BruiseSlots  = 3;
        public const int WoundSlots   = 2;
        public const int MaimingSlots = 1;

        /// <summary>Сколько слотов этой ступени есть у «Сосуда» всего.</summary>
        public static int Capacity(InjuryGrade grade) => grade switch
        {
            InjuryGrade.Bruise  => BruiseSlots,
            InjuryGrade.Wound   => WoundSlots,
            InjuryGrade.Maiming => MaimingSlots,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), grade, "Неизвестная ступень травмы."),
        };

        /// <summary>
        /// Куда ляжет травма ступени <paramref name="incoming"/> при уже занятых
        /// <paramref name="occupied"/> слотах. Состояние не меняется — решение принимает вызывающий.
        /// </summary>
        public static InjuryOutcome Resolve(in InjurySlots occupied, InjuryGrade incoming)
        {
            // Ступени идут по возрастанию значений enum, поэтому подъём — это просто шаг вверх по нему.
            for (InjuryGrade grade = incoming; grade <= InjuryGrade.Maiming; grade++)
            {
                if (occupied.Occupied(grade) < Capacity(grade))
                    return new InjuryOutcome(incoming, grade, retired: false);
            }

            return new InjuryOutcome(incoming, InjuryGrade.Maiming, retired: true);
        }
    }
}
