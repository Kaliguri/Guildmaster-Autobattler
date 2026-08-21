using System.Collections.Generic;
using Guildmaster.Core.Random;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Книга ран забега: кладёт последствие на «Сосуд» по правилам каскада, считает возраст ран и
    /// снимает те, что прошли сами. Единственное место, где <see cref="RosterSlot.Injuries"/> меняется.
    /// </summary>
    /// <remarks>
    /// <b>Ролл ведётся от переданного сида, а не от общего генератора забега.</b> Причина сетевая:
    /// наложение раны едет командой (<c>RunCommandKind.InflictInjury</c>), а команда обязана
    /// воспроизводиться из лога сама по себе. Возьми мы поток <c>IRngService</c> забега — результат
    /// зависел бы от того, сколько раз этот поток дёрнули ДО команды, то есть от порядка чужих
    /// действий; у хозяина и гостя он разъехался бы молча. Сид в команде делает выдачу самодостаточной.
    /// <para><b>Каскад здесь не переписан</b> — правило слотов спрашивается у <see cref="InjuryCascade"/>.
    /// Здесь только работа с состоянием: что положить, что снять, кого состарить.</para>
    /// </remarks>
    public static class InjuryLedger
    {
        /// <summary>Занятые слоты «Сосуда»: ступени берутся у ассетов по id.</summary>
        public static InjurySlots SlotsOf(RosterSlot slot, IContentDatabase content)
        {
            if (slot?.Injuries == null || slot.Injuries.Length == 0 || content == null) return default;

            var grades = new List<InjuryGrade>(slot.Injuries.Length);
            for (int i = 0; i < slot.Injuries.Length; i++)
            {
                if (TryResolve(slot.Injuries[i], content, out ConsequenceData def) && def.IsInjury)
                    grades.Add(def.Grade);
            }
            return InjurySlots.Of(grades);
        }

        /// <summary>
        /// Положить на «Сосуд» последствие ступени <paramref name="requested"/>: ступень уточняет
        /// каскад, конкретную рану выбирает взвешенный ролл от <paramref name="rollSeed"/>.
        /// </summary>
        /// <returns>
        /// Исход каскада. <see cref="InjuryOutcome.Retired"/> — слоты кончились и «Сосуд» выбывает из
        /// забега; травма при этом НЕ кладётся (класть некуда), а само выбывание — механика, которой в
        /// игре ещё нет: сейчас исход только сообщается вызывающему.
        /// </returns>
        public static InjuryOutcome Inflict(RosterSlot slot, InjuryGrade requested,
                                            IContentDatabase content, ulong rollSeed)
        {
            InjurySlots occupied = SlotsOf(slot, content);
            InjuryOutcome outcome = InjuryCascade.Resolve(occupied, requested);
            if (slot == null || outcome.Retired) return outcome;

            ConsequenceData picked = Roll(outcome.Grade, content, rollSeed);
            if (picked == null) return outcome; // пула нет — исход сообщён, класть нечего

            Append(slot, new Injury(picked.Id));
            return outcome;
        }

        /// <summary>
        /// Узел маршрута пройден: состарить все раны на один узел и снять те, чей срок вышел.
        /// Возвращает, сколько последствий снялось само.
        /// </summary>
        /// <remarks>
        /// Срок спрашивается у ассета (<c>ExpiresAfterNodes</c>), а не хранится в сейве: иначе правка
        /// баланса не дошла бы до уже идущего забега. <c>0</c> у ассета = не проходит никогда — так
        /// живут средние, тяжёлые и вся закалка.
        /// </remarks>
        public static int AdvanceNode(RunState run, IContentDatabase content)
        {
            if (run?.Guild == null || content == null) return 0;

            int healed = 0;
            for (int s = 0; s < run.Guild.Length; s++)
            {
                RosterSlot slot = run.Guild[s];
                if (slot?.Injuries == null || slot.Injuries.Length == 0) continue;

                var kept = new List<Injury>(slot.Injuries.Length);
                for (int i = 0; i < slot.Injuries.Length; i++)
                {
                    Injury injury = slot.Injuries[i];
                    if (injury == null) continue;

                    injury.NodesSurvived++;

                    int expiry = TryResolve(injury, content, out ConsequenceData def) ? def.ExpiresAfterNodes : 0;
                    if (expiry > 0 && injury.NodesSurvived >= expiry) { healed++; continue; }

                    kept.Add(injury);
                }

                if (kept.Count != slot.Injuries.Length) slot.Injuries = kept.ToArray();
            }
            return healed;
        }

        /// <summary>
        /// Снять с «Сосуда» одно последствие по id (торговец, привал). Снимается ПЕРВОЕ совпадение:
        /// две одинаковые раны различает только порядок получения, и платить игрок собирался за одну.
        /// <c>false</c> — такого последствия на «Сосуде» нет.
        /// </summary>
        public static bool Remove(RosterSlot slot, string consequenceId)
        {
            if (slot?.Injuries == null || string.IsNullOrEmpty(consequenceId)) return false;

            int index = -1;
            for (int i = 0; i < slot.Injuries.Length; i++)
                if (slot.Injuries[i]?.Id == consequenceId) { index = i; break; }
            if (index < 0) return false;

            var kept = new List<Injury>(slot.Injuries.Length - 1);
            for (int i = 0; i < slot.Injuries.Length; i++)
                if (i != index) kept.Add(slot.Injuries[i]);

            slot.Injuries = kept.ToArray();
            return true;
        }

        /// <summary>Взвешенный выбор травмы нужной ступени. Порядок пула стабилен — на нём стоит детерминизм.</summary>
        private static ConsequenceData Roll(InjuryGrade grade, IContentDatabase content, ulong rollSeed)
        {
            if (content == null) return null;

            IReadOnlyList<ConsequenceData> all = content.All<ConsequenceData>();
            if (all == null || all.Count == 0) return null;

            var pool = new List<ConsequenceData>(all.Count);
            float total = 0f;
            for (int i = 0; i < all.Count; i++)
            {
                ConsequenceData def = all[i];
                if (def == null || !def.IsInjury || def.Grade != grade || def.Weight <= 0f) continue;
                pool.Add(def);
                total += def.Weight;
            }
            if (pool.Count == 0 || total <= 0f) return null;

            var rng = new XorShiftRng(rollSeed);
            float roll = rng.NextFloat() * total;
            for (int i = 0; i < pool.Count; i++)
            {
                roll -= pool[i].Weight;
                if (roll < 0f) return pool[i];
            }
            return pool[pool.Count - 1]; // недостижимо при total > 0; страховка от накопленной погрешности
        }

        private static bool TryResolve(Injury injury, IContentDatabase content, out ConsequenceData def)
        {
            def = null;
            return injury != null && !string.IsNullOrEmpty(injury.Id) && content.TryGet(injury.Id, out def);
        }

        private static void Append(RosterSlot slot, Injury injury)
        {
            Injury[] old = slot.Injuries ?? System.Array.Empty<Injury>();
            var grown = new Injury[old.Length + 1];
            System.Array.Copy(old, grown, old.Length);
            grown[old.Length] = injury;
            slot.Injuries = grown;
        }
    }
}
