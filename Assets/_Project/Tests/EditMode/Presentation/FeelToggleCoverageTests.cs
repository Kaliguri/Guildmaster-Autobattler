using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Guildmaster.Presentation;
using Guildmaster.Presentation.Design;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// ПЕРЕПИСЬ джуса: каждый вход сочности должен либо иметь тумблер в <see cref="CombatFeelConfig"/>,
    /// либо стоять здесь с явной причиной, почему тумблера у него нет.
    ///
    /// <b>Зачем этот тест существует.</b> Вопрос Макса (30.07): «есть ли список всех gamefeel/VFX, чтобы ни
    /// о ком не забыть». Ответ был «нет»: выключалось четырьмя разными способами, а пять эффектов не
    /// выключались вообще — и «список тумблеров» отвечал на вопрос «всё ли включено» неправдой. Отдельный
    /// SO-список тут не помог бы: он стал бы вторым владельцем и разошёлся бы с кодом на первом же эффекте.
    /// Перепись живёт в тесте, поэтому расхождение не остаётся незамеченным — новый эффект без записи
    /// роняет сборку, а не тихо появляется в бою без ручки.
    /// </summary>
    public sealed class FeelToggleCoverageTests
    {
        /// <summary>Чем вход выключается. Порядок значений — от «есть ручка» к «ручки нет и не надо».</summary>
        private enum Switch
        {
            /// <summary>Булев тумблер в конфиге, читаемый презентацией.</summary>
            Toggle,
            /// <summary>
            /// Тумблер есть, но применяется ВНУТРИ конфига и наружу не торчит: конфиг сам решает, что
            /// вернуть. Так живёт цвет вспышки по школе — презентации незачем знать, откуда цвет.
            /// </summary>
            InternalToggle,
            /// <summary>Нулевая длительность/размер в конфиге: 0 = эффекта нет.</summary>
            Duration,
            /// <summary>Пустая ссылка на ассет: нет ассета — нет эффекта.</summary>
            Asset,
            /// <summary>Это не эффект, а СИГНАЛ симуляции виду. Выключать нечего.</summary>
            NotAnEffect,
        }

        private readonly struct Entry
        {
            public readonly string Name;    // метод UnitView или поле конфига
            public readonly Switch Kind;
            public readonly string Detail;  // имя тумблера/числа/ассета или причина

            public Entry(string name, Switch kind, string detail)
            {
                Name = name; Kind = kind; Detail = detail;
            }
        }

        // --- ПЕРЕПИСЬ. Правишь джус — правь здесь, иначе тест упадёт и напомнит. ---
        private static readonly Entry[] Registry =
        {
            // Тело и удар
            new Entry("OnDamageReceived",   Switch.Toggle, nameof(CombatFeelConfig.EnableHitFlash)),
            new Entry("OnHealed",           Switch.Toggle, nameof(CombatFeelConfig.EnableHealFlash)),
            new Entry("ShowTelegraph",      Switch.Toggle, nameof(CombatFeelConfig.EnableTelegraphFlash)),
            new Entry("RaiseGuard",         Switch.Toggle, nameof(CombatFeelConfig.EnableGuardPose)),
            new Entry("OnAttackStarted",    Switch.Toggle, nameof(CombatFeelConfig.EnableAttackAnticipation)),
            new Entry("OnAttackLunge",      Switch.Toggle, nameof(CombatFeelConfig.EnableAttackerLunge)),

            // Смерть
            new Entry("OnDeath",            Switch.Toggle, nameof(CombatFeelConfig.EnableDeathShatter)),

            // Время
            new Entry("OnHitstop",          Switch.Duration, "длительность приходит из презентера; 0 = заморозки нет"),
            new Entry("HoldHitFrame",       Switch.Duration, nameof(CombatFeelConfig.FinisherShatterDuration)),
            new Entry("HoldFrame",          Switch.Duration, nameof(CombatFeelConfig.FinisherShatterDuration)),

            // Каст
            new Entry("PlayCastOutline",    Switch.Duration, nameof(CombatFeelConfig.CastOutlineDuration)),
            new Entry("PlayCastCharge",     Switch.Duration, nameof(CombatFeelConfig.CastOutlineDuration)),
            new Entry("PlayCastGlow",       Switch.Toggle,   nameof(CombatFeelConfig.EnableCastGlow)),
            new Entry("PlayBlockGlow",      Switch.Toggle,   nameof(CombatFeelConfig.EnableBlockGlow)),

            // Сигналы симуляции — не джус
            new Entry("OnAttackInterrupted", Switch.NotAnEffect, "сим сообщает, что замах оборван"),
            new Entry("OnBattleEnded",       Switch.NotAnEffect, "сим сообщает конец боя — вид доигрывает"),

            // Тумблеры без публичного входа: живут внутри тика вида или в презентере
            new Entry("PlayHitSquash",       Switch.Toggle, nameof(CombatFeelConfig.EnableHitSquash)),
            new Entry("PlayHitNudge",        Switch.Toggle, nameof(CombatFeelConfig.EnableHitNudge)),
            new Entry("TickIdleBreath",      Switch.Toggle, nameof(CombatFeelConfig.EnableIdleBreath)),
            new Entry("RequestFacingFlip",   Switch.Toggle, nameof(CombatFeelConfig.EnableFacingFlipSquash)),
            new Entry("TickTargetAcquireTell", Switch.Toggle, nameof(CombatFeelConfig.EnableTargetAcquireTell)),
            new Entry("TickLocomotionDust",  Switch.Toggle, nameof(CombatFeelConfig.EnableContactDust)),
            new Entry("SchoolFlashColor",    Switch.InternalToggle, "_enableSchoolFlash — применяется внутри конфига"),
            new Entry("ImpactFrameHold",     Switch.Toggle, nameof(CombatFeelConfig.EnableImpactFrame)),
            new Entry("DeathAnticipate",     Switch.Toggle, nameof(CombatFeelConfig.EnableDeathAnticipation)),
            new Entry("FloatingTextArc",     Switch.Toggle, nameof(CombatFeelConfig.EnableFloatingTextArc)),
            new Entry("HpBarPunch",          Switch.Toggle, nameof(CombatFeelConfig.EnableHpBarPunch)),

            // Удар: две стадии и след, что от него остался
            new Entry("AddBodyCut",      Switch.Toggle, nameof(CombatFeelConfig.EnableBodyCuts)),
            new Entry("HealBodyCuts",    Switch.Toggle, nameof(CombatFeelConfig.EnableBodyCuts)),
            new Entry("HitForm",         Switch.Toggle, nameof(CombatFeelConfig.EnableHitForm)),
            new Entry("HitFormBreak",    Switch.Toggle, nameof(CombatFeelConfig.EnableHitFormBreakOnShield)),
            new Entry("HitFormLine",     Switch.Toggle, nameof(CombatFeelConfig.EnableHitFormLine)),
            new Entry("SwingArc",        Switch.Toggle, nameof(CombatFeelConfig.EnableSwingArc)),
            new Entry("SwingArcShaping", Switch.InternalToggle, "_enableSwingArcShaping — применяется внутри конфига"),
            // Зона удара выбирает МЕСТО попадания, а не рисует эффект: выключенная, она возвращает удар в
            // HitPoint — то есть в поведение до 06.08.2026. Оба режима сравниваются в бою.
            new Entry("ImpactZone",      Switch.Toggle, nameof(CombatFeelConfig.EnableImpactZones)),

            // Шлейф из призрачных копий: копии снимает ПРЕЗЕНТЕР (он читает признак рывка из кадра), поэтому
            // входа на виде у него нет — есть только тумблер, и спрашивается он в CombatPresenter.
            new Entry("DashGhostTrail",  Switch.Toggle, nameof(CombatFeelConfig.EnableDashGhostTrail)),
            // Иллюзия уклонения — там же: копию оставляет презентер по событию evade, входа на виде нет.
            new Entry("DodgeIllusion",   Switch.Toggle, nameof(CombatFeelConfig.EnableDodgeIllusion)),

            // Взмах и его геометрия — ЗАПРОСЫ к виду, а не эффекты: отвечают «где клинок», ничего не рисуя.
            new Entry("TryGetSwingArc",      Switch.NotAnEffect, "геометрия взмаха для дуги — запрос, не эффект"),
            new Entry("TryGetSwingProgress", Switch.NotAnEffect, "насколько прошёл взмах — запрос, не эффект"),
            new Entry("TryGetStrikeDirection", Switch.NotAnEffect, "куда шёл клинок в момент касания — запрос, не эффект"),

            // VFX — выключаются пустой ссылкой на VfxData
            new Entry("VfxHitForm",     Switch.Asset, "форма удара по A→B"),
            new Entry("VfxSwingArc",    Switch.Asset, "дуга за клинком на взмахе"),
            new Entry("VfxHitSpark",    Switch.Asset, "искры попадания в HitPoint"),
            new Entry("VfxMuzzle",      Switch.Asset, "вспышка выстрела в ShotPoint"),
            new Entry("VfxImpactDust",  Switch.Asset, "пыль удара у ног"),
            new Entry("VfxContactDust", Switch.Asset, "пыль шага у ног"),
            new Entry("VfxHeal",        Switch.Asset, "искры лечения в HitPoint"),
            new Entry("VfxCastBurst",   Switch.Asset, "всплеск каста в HitPoint"),
        };

        /// <summary>
        /// Каждый публичный вход вида должен быть в переписи. Это и есть защита от «забыли»: новый
        /// <c>Play*</c>/<c>Show*</c>/<c>Raise*</c> на <see cref="UnitView"/> без записи роняет тест.
        /// </summary>
        [Test]
        public void EveryPublicFeelEntryOnTheViewIsInTheRegistry()
        {
            var known = new HashSet<string>(Registry.Select(e => e.Name));

            IEnumerable<string> entries = typeof(UnitView)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(m => m.Name)
                .Where(IsFeelEntryName)
                .Distinct();

            var missing = entries.Where(n => !known.Contains(n)).ToArray();

            Assert.IsEmpty(missing,
                "Эти входы сочности не переписаны — у них нет ни тумблера, ни причины его не иметь: " +
                string.Join(", ", missing) + ". Добавь запись в Registry (и тумблер в CombatFeelConfig, " +
                "если эффект должен выключаться).");
        }

        /// <summary>Каждый тумблер конфига обязан быть в переписи — иначе завели ручку и не подключили.</summary>
        [Test]
        public void EveryToggleInTheConfigIsAccountedFor()
        {
            var referenced = new HashSet<string>(
                Registry.Where(e => e.Kind == Switch.Toggle).Select(e => e.Detail));

            var orphans = ToggleNames().Where(n => !referenced.Contains(n)).ToArray();

            Assert.IsEmpty(orphans,
                "Тумблеры есть в конфиге, но ни один вход на них не заявлен: " + string.Join(", ", orphans) +
                ". Либо подключи, либо убери — ручка, которая ничего не выключает, хуже её отсутствия.");
        }

        /// <summary>
        /// Тумблер должен ЧИТАТЬСЯ в коде презентации. Рефлексия этого не видит: свойство существует и
        /// возвращает значение, даже если его никто не спрашивает, — а именно так выглядит «завели ручку и
        /// забыли подключить». Поэтому проверка текстовая, по исходникам.
        /// </summary>
        [Test]
        public void EveryToggleIsActuallyReadByThePresentation()
        {
            string root = Path.Combine(Application.dataPath, "_Project", "Scripts", "Presentation");
            Assert.IsTrue(Directory.Exists(root), "Не найден корень презентации: " + root);

            string code = string.Concat(Directory
                .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith("CombatFeelConfig.cs", StringComparison.Ordinal))
                .Select(File.ReadAllText));

            var unread = ToggleNames().Where(n => !code.Contains(n, StringComparison.Ordinal)).ToArray();

            Assert.IsEmpty(unread,
                "Эти тумблеры не читает никто в презентации — выключение их ничего не изменит: " +
                string.Join(", ", unread));
        }

        /// <summary>Запись переписи не должна ссылаться на несуществующее имя в конфиге (ловит переименование).</summary>
        [Test]
        public void RegistryPointsAtRealConfigMembers()
        {
            var members = new HashSet<string>(typeof(CombatFeelConfig)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name));

            var broken = Registry
                .Where(e => e.Kind == Switch.Toggle || e.Kind == Switch.Duration)
                .Where(e => e.Detail.Length > 0 && char.IsUpper(e.Detail[0]) && !members.Contains(e.Detail))
                .Select(e => e.Name + " -> " + e.Detail)
                .ToArray();

            Assert.IsEmpty(broken,
                "Перепись ссылается на то, чего в конфиге нет (переименовали?): " + string.Join(", ", broken));
        }

        /// <summary>VFX-поля конфига тоже переписываются: пустая ссылка — это способ выключения.</summary>
        [Test]
        public void EveryVfxSlotIsAccountedFor()
        {
            var known = new HashSet<string>(Registry.Where(e => e.Kind == Switch.Asset).Select(e => e.Name));

            var slots = typeof(CombatFeelConfig)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType.Name == "VfxData")
                .Select(p => p.Name)
                .ToArray();

            var missing = slots.Where(n => !known.Contains(n)).ToArray();
            Assert.IsEmpty(missing, "VFX-слоты не переписаны: " + string.Join(", ", missing));
        }

        // Имена, которые считаются входом сочности. On* сюда попадает целиком, а сигналы сима отсеиваются
        // записью NotAnEffect — так новый On-хук всё равно требует решения, а не проскакивает по префиксу.
        private static bool IsFeelEntryName(string name)
            => name.StartsWith("Play", StringComparison.Ordinal)
            || name.StartsWith("Show", StringComparison.Ordinal)
            || name.StartsWith("Raise", StringComparison.Ordinal)
            || name.StartsWith("Hold", StringComparison.Ordinal)
            || name.StartsWith("On", StringComparison.Ordinal);

        private static IEnumerable<string> ToggleNames()
            => typeof(CombatFeelConfig)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(bool) && p.Name.StartsWith("Enable", StringComparison.Ordinal))
                .Select(p => p.Name);
    }
}
