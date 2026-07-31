using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Guildmaster.Core.DevConsole;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Core
{
    /// <summary>
    /// Реестр dev-команд и разбор строки (Трек К UI-реворка): токенизация, арность, автодополнение,
    /// живучесть при падении команды. Чистый headless — ни сцены, ни консольного экрана.
    /// </summary>
    public sealed class DevConsoleRegistryTests
    {
        private enum Mode { Off, Slow, Fast }

        private static DevCommandRegistry Registry() => new DevCommandRegistry();

        // ── Регистрация ───────────────────────────────────────────────────────────────────

        [Test]
        public void Register_DuplicateName_Throws()
        {
            var reg = Registry();
            reg.Register("gm_ping", "первая", _ => "ok");

            Assert.Throws<System.InvalidOperationException>(
                () => reg.Register("gm_ping", "вторая", _ => "ok"),
                "дубль имени — опечатка при копировании модуля, тихое замещение выключило бы команду молча");
        }

        [Test]
        public void Register_DuplicateName_IgnoresCase()
        {
            var reg = Registry();
            reg.Register("gm_ping", "первая", _ => "ok");

            Assert.Throws<System.InvalidOperationException>(
                () => reg.Register("GM_PING", "вторая", _ => "ok"),
                "имена сравниваются без учёта регистра — иначе появились бы две «разные» одинаковые команды");
        }

        [Test]
        public void All_IsAlphabetical_RegardlessOfRegistrationOrder()
        {
            var reg = Registry();
            reg.Register("gm_zeta", "з", _ => null);
            reg.Register("gm_alpha", "а", _ => null);
            reg.Register("gm_beta", "б", _ => null);

            var names = new List<string>();
            foreach (DevCommand c in reg.All) names.Add(c.Name);

            Assert.AreEqual(new[] { "gm_alpha", "gm_beta", "gm_zeta" }, names,
                "порядок стабилен: от него зависят список команд и автодополнение");
        }

        [Test]
        public void Unregister_RemovesFromLookupAndList()
        {
            var reg = Registry();
            reg.Register("gm_temp", "живёт со скоупом", _ => "ok");

            Assert.IsTrue(reg.Unregister("gm_temp"));
            Assert.IsFalse(reg.TryGet("gm_temp", out _), "снятая команда не находится по имени");
            Assert.AreEqual(0, reg.Count, "и исчезает из списка");
            Assert.IsFalse(reg.Unregister("gm_temp"), "повторное снятие — false, не исключение");
        }

        // ── Вызов ─────────────────────────────────────────────────────────────────────────

        [Test]
        public void Execute_EmptyLine_IsEmptyStatus()
        {
            Assert.AreEqual(DevCommandStatus.Empty, Registry().Execute("   ").Status);
        }

        [Test]
        public void Execute_UnknownCommand_NamesIt()
        {
            DevCommandResult result = Registry().Execute("gm_nope 1");

            Assert.AreEqual(DevCommandStatus.UnknownCommand, result.Status);
            StringAssert.Contains("gm_nope", result.Message, "в тексте ошибки — то, что человек напечатал");
        }

        [Test]
        public void Execute_CaseInsensitiveName()
        {
            var reg = Registry();
            reg.Register("gm_ping", "проверка", _ => "pong");

            Assert.AreEqual("pong", reg.Execute("GM_Ping").Message,
                "печатать с Caps Lock в спешке — норма, регистр не должен мешать");
        }

        [Test]
        public void Execute_TooFewArguments_ReportsUsage()
        {
            var reg = Registry();
            reg.Register("gm_sep_radius", "радиус тела", a => a.GetFloat(0).ToString(CultureInfo.InvariantCulture),
                new DevParam("value", DevParamType.Float));

            DevCommandResult result = reg.Execute("gm_sep_radius");

            Assert.AreEqual(DevCommandStatus.BadArguments, result.Status);
            StringAssert.Contains("gm_sep_radius <value>", result.Message, "форма вызова печатается сразу");
        }

        [Test]
        public void Execute_TooManyArguments_Rejected()
        {
            var reg = Registry();
            reg.Register("gm_ping", "проверка", _ => "pong");

            Assert.AreEqual(DevCommandStatus.BadArguments, reg.Execute("gm_ping лишнее").Status,
                "лишний аргумент — почти всегда опечатка или непонимание формы");
        }

        [Test]
        public void Execute_OptionalArgument_MayBeOmitted()
        {
            var reg = Registry();
            reg.Register("gm_spawn", "заспавнить", a => $"n={a.GetInt(0, 1)}",
                new DevParam("count", DevParamType.Int, optional: true));

            Assert.AreEqual("n=1", reg.Execute("gm_spawn").Message, "необязательный опущен — работает дефолт");
            Assert.AreEqual("n=7", reg.Execute("gm_spawn 7").Message);
        }

        [Test]
        public void Execute_HandlerThrows_ConsoleSurvives()
        {
            var reg = Registry();
            reg.Register("gm_boom", "падает", _ => throw new System.InvalidOperationException("нет симуляции"));

            DevCommandResult result = reg.Execute("gm_boom");

            Assert.AreEqual(DevCommandStatus.Failed, result.Status,
                "падение дев-команды не должно ронять консоль — это её единственная защита");
            StringAssert.Contains("нет симуляции", result.Message, "причина видна без открывания кода");
        }

        // ── Аргументы ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Args_Float_ParsesWithDotUnderRussianCulture()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");

                var reg = Registry();
                reg.Register("gm_sep_ally", "мягкость", a => a.GetFloat(0).ToString("0.00", CultureInfo.InvariantCulture),
                    new DevParam("value", DevParamType.Float));

                DevCommandResult result = reg.Execute("gm_sep_ally 0.35");

                Assert.AreEqual(DevCommandStatus.Ok, result.Status,
                    "синтаксис консоли не зависит от локали машины: точка — десятичный разделитель всегда");
                Assert.AreEqual("0.35", result.Message);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void Args_Bool_AcceptsWideVocabulary()
        {
            var reg = Registry();
            reg.Register("gm_toggle", "тумблер", a => a.GetBool(0) ? "on" : "off",
                new DevParam("state", DevParamType.Bool));

            Assert.AreEqual("on",  reg.Execute("gm_toggle true").Message);
            Assert.AreEqual("on",  reg.Execute("gm_toggle 1").Message);
            Assert.AreEqual("on",  reg.Execute("gm_toggle ON").Message);
            Assert.AreEqual("off", reg.Execute("gm_toggle no").Message);
        }

        [Test]
        public void Args_BadNumber_MentionsPositionAndExpectation()
        {
            var reg = Registry();
            reg.Register("gm_int", "число", a => a.GetInt(0).ToString(),
                new DevParam("value", DevParamType.Int));

            DevCommandResult result = reg.Execute("gm_int абв");

            Assert.AreEqual(DevCommandStatus.BadArguments, result.Status);
            StringAssert.Contains("абв", result.Message);
            StringAssert.Contains("#1", result.Message, "номер аргумента важнее его типа: команд с тремя числами хватает");
        }

        [Test]
        public void Args_Enum_ByNameIgnoringCase_AndListsValuesOnError()
        {
            var reg = Registry();
            reg.Register("gm_mode", "режим", a => a.GetEnum<Mode>(0).ToString(),
                new DevParam("mode", DevParamType.Enum));

            Assert.AreEqual("Fast", reg.Execute("gm_mode fast").Message);

            DevCommandResult bad = reg.Execute("gm_mode turbo");
            Assert.AreEqual(DevCommandStatus.BadArguments, bad.Status);
            StringAssert.Contains("Slow", bad.Message, "ошибка сама перечисляет допустимые значения");
        }

        [Test]
        public void Tokenize_QuotedStringStaysOneToken()
        {
            var tokens = new List<string>();
            int n = DevCommandLine.Tokenize("gm_say \"два слова\" 3", tokens);

            Assert.AreEqual(3, n);
            Assert.AreEqual("два слова", tokens[1]);
            Assert.AreEqual("3", tokens[2]);
        }

        [Test]
        public void Tokenize_UnclosedQuote_TakesRestOfLine()
        {
            var tokens = new List<string>();
            DevCommandLine.Tokenize("gm_say \"ещё печатаю", tokens);

            Assert.AreEqual(2, tokens.Count);
            Assert.AreEqual("ещё печатаю", tokens[1],
                "ругаться на незакрытую кавычку во время набора — худшее, что может сделать подсказка");
        }

        // ── Автодополнение ────────────────────────────────────────────────────────────────

        [Test]
        public void Match_FiltersByPrefix_EmptyPrefixGivesAll()
        {
            var reg = Registry();
            reg.Register("gm_sep_radius", "р", _ => null);
            reg.Register("gm_sep_iters", "и", _ => null);
            reg.Register("gm_arena_swap", "с", _ => null);

            var hits = new List<DevCommand>();

            Assert.AreEqual(2, reg.Match("gm_sep", hits));
            Assert.AreEqual(3, reg.Match("", hits), "пустой префикс — весь список, это открытая палитра команд");
        }

        [Test]
        public void CommonPrefix_GrowsToSharedPart_NotToFirstMatch()
        {
            var reg = Registry();
            reg.Register("gm_sep_radius", "р", _ => null);
            reg.Register("gm_sep_iters", "и", _ => null);
            reg.Register("gm_sep_ally", "а", _ => null);

            Assert.AreEqual("gm_sep_", reg.CommonPrefix("gm_sep"),
                "Tab дописывает общее, а не прыгает на первую команду списка");
        }

        [Test]
        public void CommonPrefix_NoMatches_ReturnsInputUnchanged()
        {
            var reg = Registry();
            reg.Register("gm_ping", "п", _ => null);

            Assert.AreEqual("zzz", reg.CommonPrefix("zzz"));
        }

        [Test]
        public void CommonPrefix_SingleMatch_CompletesFully()
        {
            var reg = Registry();
            reg.Register("gm_arena_swap", "с", _ => null);
            reg.Register("gm_ping", "п", _ => null);

            Assert.AreEqual("gm_arena_swap", reg.CommonPrefix("gm_ar"));
        }
    }
}
