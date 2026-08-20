using System;
using Guildmaster.Core.Simulation;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;

namespace Guildmaster.Combat
{
    /// <summary>
    /// Чистая детерминированная арифметика тайминга авто-атаки (вики «14»).
    /// Целочисленная по построению — float участвует только в конверсии <c>AttackSpeed → intervalTicks</c>
    /// с явной политикой округления (<see cref="MidpointRounding.AwayFromZero"/>), чтобы исключить
    /// banker's rounding и рассинхрон чек-суммы между сборками/платформами.
    /// <para>
    /// Модель «фикс. длительность свинга + кламп»:
    /// <code>
    /// intervalTicks       = max(1, round(TickRate / AttackSpeed))
    /// attackDurationTicks = min(MaxAttackAnimTicks, intervalTicks)
    /// windupTicks         = clamp((hitFrame * attackDurationTicks) / frameCount, MinWindupTicks, intervalTicks − 1)
    /// </code>
    /// </para>
    /// </summary>
    public static class AttackTiming
    {
        /// <summary>
        /// Интервал между атаками в сим-тиках. Пол <c>≥ 1</c> закрывает край «очень высокий AttackSpeed»
        /// (иначе деление/кламп вырождаются). <paramref name="attackSpeed"/> ≤ 0 → максимально редкая атака.
        /// </summary>
        public static int IntervalTicks(float attackSpeed)
        {
            if (attackSpeed <= 0f) return int.MaxValue;
            int ticks = (int)Math.Round(SimConstants.TickRate / attackSpeed, MidpointRounding.AwayFromZero);
            return ticks < 1 ? 1 : ticks;
        }

        /// <summary>
        /// Атак в секунду — то, что игра ДЕЙСТВИТЕЛЬНО производит: <c>TickRate / IntervalTicks</c>, то есть
        /// с тиковой квантизацией. Сырой <c>AttackSpeed</c> в инструментах показывать нельзя — при 30 Гц он
        /// даёт число, которого в бою не бывает (1.4 и 1.5 обе живут в интервале 20 тиков = 1.5 атак/сек).
        /// <para>Единственный владелец этой формулы: панель инвентаря, Content Hub и балансный аудитор
        /// держали три копии, и аудиторская считала по сырому стату (аудит 2026-07-26, T-10/R1-74).</para>
        /// </summary>
        public static float AttacksPerSecond(float attackSpeed)
        {
            int interval = IntervalTicks(attackSpeed);
            if (interval <= 0 || interval == int.MaxValue) return 0f;
            return (float)SimConstants.TickRate / interval;
        }

        /// <summary>
        /// Длительность свинга в тиках = <c>min(потолок, intervalTicks)</c>. Потолок — либо свой у юнита
        /// (<see cref="UnitData.AttackSwingTicks"/>), либо глобальный <see cref="SimConstants.MaxAttackAnimTicks"/>.
        /// </summary>
        /// <param name="maxAnimTicks">Потолок юнита в тиках; <c>0</c> = глобальный дефолт.</param>
        public static int AttackDurationTicks(int intervalTicks, int maxAnimTicks = 0)
        {
            int cap = maxAnimTicks > 0 ? maxAnimTicks : SimConstants.MaxAttackAnimTicks;
            return intervalTicks < cap ? intervalTicks : cap;
        }

        /// <summary>
        /// Тики замаха до кадра контакта. Целочисленное деление = floor (детерминированно).
        /// Кламп: нижний — <see cref="SimConstants.MinWindupTicks"/> (телеграф-пол), верхний — <c>intervalTicks − 1</c>
        /// (удар не совпадает с тиком старта следующей атаки). Пустой клип (<paramref name="frameCount"/> ≤ 0)
        /// или <paramref name="hitFrame"/> ≤ 0 → нижний кламп.
        /// </summary>
        /// <summary>
        /// Дополнительное восстановление (сверх анимационного доигрыша) в тиках из секунд. Детерминированное
        /// округление (<see cref="MidpointRounding.AwayFromZero"/>), как у интервала. ≤ 0 сек → 0 тиков.
        /// </summary>
        public static int RecoveryTicks(float seconds)
        {
            if (seconds <= 0f) return 0;
            int ticks = (int)Math.Round(SimConstants.TickRate * seconds, MidpointRounding.AwayFromZero);
            return ticks < 0 ? 0 : ticks;
        }

        /// <summary>
        /// Хвост-доигрыш клипа удара после кадра контакта = <c>attackDurationTicks − windupTicks</c>.
        /// Это «задняя половина» анимации свинга: урон уже нанесён (на кадре контакта), но юнит всё ещё
        /// доигрывает движение и остаётся «занят» (рут либо штраф скорости). Выводится из той же модели,
        /// что и <see cref="WindupTicks"/>, поэтому автоматически масштабируется со скоростью атаки —
        /// в отличие от абсолютных секунд не «расклеивается» при баффах скорости.
        /// <para>
        /// Замах не объявлен (<paramref name="windupShare"/> ≤ 0) → он был чистым телеграфом (пол
        /// <see cref="SimConstants.MinWindupTicks"/>), доигрывать нечего → 0. Признаком служит доля, а не
        /// кадры клипа: тайминг объявлен в данных с 06.08.2026, и спрашивать про кадры здесь значило бы
        /// вернуть симуляции зависимость от разметки анимации ради одного условия.
        /// Вычитание (а не независимая пропорция «хвостовых кадров») сознательно: оно поглощает округление
        /// windup, поэтому <c>windup + доигрыш = attackDurationTicks</c> ровно, без фантомного зазора.
        /// </para>
        /// </summary>
        /// <param name="maxAnimTicks">Потолок свинга юнита в тиках; <c>0</c> = глобальный дефолт.</param>
        public static int FollowThroughTicks(float windupShare, int intervalTicks, int windupTicks,
            int maxAnimTicks = 0)
        {
            if (windupShare <= 0f) return 0;
            int tail = AttackDurationTicks(intervalTicks, maxAnimTicks) - windupTicks;
            return tail < 0 ? 0 : tail;
        }

        /// <summary>
        /// Тики замаха, которые получит СЛЕДУЮЩИЙ свинг этого юнита при текущей скорости атаки и клипе.
        /// Свёртка <see cref="IntervalTicks"/> + <see cref="WindupTicks"/> над данными юнита — чтобы гейт
        /// атаки и движение (предсказание «докрутит ли замах», <c>CombatPositioning.CanLandWindup</c>)
        /// считали ту же длину, что и <c>AutoAttackSystem.EnterWindup</c>. Пустой визуал → нижний кламп.
        /// </summary>
        /// <param name="ignoreRecast">
        /// Считать длину так, будто рекаста не было. Нужно ровно одному месту — расчёту доигрыша в
        /// <c>AutoAttackSystem.EnterWindup</c>: хвост меряется от контакта, и без базовой длины ускоренный
        /// замах не сократил бы свинг, а перелил бы сэкономленное в доигрыш.
        /// </param>
        public static int WindupTicksFor(RuntimeUnit unit, bool ignoreRecast = false)
        {
            // Канальная атака: вход в поток задан СЕКУНДАМИ и не клампится интервалом атаки. У такого
            // кита интервал отмеряет тик ВНУТРИ канала, а не период между атаками, поэтому обычный
            // потолок «интервал − 1» здесь не имеет смысла — он и обрезал бы длинный занос, ради
            // которого канал вообще существует.
            if (unit.Unit != null && unit.Unit.Channel.Exists && unit.Unit.Channel.WindupSeconds > 0f)
            {
                int channelWindup = RecoveryTicks(unit.Unit.Channel.WindupSeconds);
                return channelWindup < SimConstants.MinWindupTicks ? SimConstants.MinWindupTicks : channelWindup;
            }

            float attackSpeed = unit.Stats.Get(StatType.AttackSpeed);
            int intervalTicks = IntervalTicks(attackSpeed);

            // Удар с разбега считается ЗДЕСЬ, а не при входе в замах: гейт атаки и предсказание
            // «докрутит ли замах» берут длину отсюда, и своя длина у самого свинга означала бы, что
            // юнит начинает удар, который по расчёту гейта попадал, а по факту нет.
            float chargeMult = unit.ChargedAttackReady && unit.Unit != null ? unit.Unit.ChargeAttackWindupMult : 1f;

            // Взведённое комбо перебивает разбег: удар «вне очереди» на то и заявлен, чтобы выйти быстро,
            // и если бы разбег его удлинял, весь смысл контроль-лупа пропадал бы (§10.6).
            if (unit.NextWindupMult > 0f && !ignoreRecast) chargeMult = unit.NextWindupMult;

            // Потолок свинга — свой у юнита (0 = глобальный дефолт): длинный занос это идентичность кита,
            // а не свойство хореографии, поэтому он и остался на юните, когда доля замаха ушла в архетип.
            int maxAnimTicks = unit.Unit != null ? unit.Unit.AttackSwingTicks : 0;

            // Замах ОБЪЯВЛЕН долей на архетипе. Покадровый путь (hitFrame/frameCount из клипа) снят
            // 06.08.2026: он делал разметку анимации источником боевого тайминга, и правка маркера на два
            // кадра двигала баланс всему ростеру молча. Клип теперь показ, число живёт в данных.
            float share = unit.Unit != null ? unit.Unit.WindupShare : 0f;
            return WindupTicksFromShare(share, intervalTicks, chargeMult, maxAnimTicks);
        }

        /// <summary>
        /// Тики ВСЕХ контактов свинга (от его старта), дописанные в <paramref name="result"/>.
        /// Возвращает их число — оно же число Ударов в этой Атаке.
        /// </summary>
        /// <remarks>
        /// <b>Один контакт — прежний путь без изменений:</b> ровно <see cref="WindupTicksFor"/>, со всеми
        /// его множителями (разбег, взведённое комбо, канал). Так серия не может тихо сдвинуть тайминги
        /// китов, у которых её нет.
        /// <para><b>Серия висит на обычном замахе:</b> первый контакт там же, где был бы единственный, а
        /// остальные отстоят от него на расстояние из клипа, умноженное на длительность свинга. Поэтому
        /// ускорение атаки сжимает серию целиком, а ускоренный замах (разбег, комбо) двигает её как одно
        /// целое — вместо того чтобы растягивать промежутки между ударами.</para>
        /// <para><b>Два инварианта, оба обязательны:</b> соседние контакты отстоят минимум на тик (иначе
        /// два удара сливаются в один момент и второй не виден ни показу, ни игроку), и последний контакт
        /// не позже <c>интервал − 1</c> (удар не совпадает со стартом следующей Атаки). Из них следует
        /// потолок скорости для многоударного кита: <c>атак/сек ≤ TickRate / N</c>. При нашем темпе
        /// (0.55–1.0 атак/сек) три контакта влезают с десятикратным запасом; если кит когда-нибудь
        /// упрётся, контакты сядут вплотную к границе — это случай для отчёта аудита анимаций, а не для
        /// тихого отбрасывания: потерянный контакт — это потерянный стак.</para>
        /// </remarks>
        public static int ContactTicks(RuntimeUnit unit, System.Collections.Generic.List<int> result)
        {
            if (result == null) return 0;
            result.Clear();

            int first = WindupTicksFor(unit);

            // Моменты контактов ОБЪЯВЛЕНЫ долями на архетипе (06.08.2026). Раньше их читали из маркеров
            // клипа — то есть число ударов в Атаке задавала разметка анимации, и лишний маркер добавлял
            // юниту стак, ничего не сказав об этом ни данным, ни балансу.
            AnimationArchetypeData archetype = unit.Unit != null ? unit.Unit.Archetype : null;
            float[] shares = archetype != null ? archetype.ContactShares : System.Array.Empty<float>();
            int count = shares.Length;

            result.Add(first);
            if (count <= 1) return 1;   // одиночный удар: прежний путь целиком

            float attackSpeed   = unit.Stats.Get(StatType.AttackSpeed);
            int   intervalTicks = IntervalTicks(attackSpeed);
            int   maxAnimTicks  = unit.Unit != null ? unit.Unit.AttackSwingTicks : 0;
            int   durationTicks = AttackDurationTicks(intervalTicks, maxAnimTicks);
            int   lastAllowed   = intervalTicks > 1 ? intervalTicks - 1 : 1;

            float basePos = shares[0];
            for (int i = 1; i < count; i++)
            {
                int offset = (int)Math.Round((shares[i] - basePos) * durationTicks,
                    MidpointRounding.AwayFromZero);

                int tick = first + (offset > 0 ? offset : 0);
                int minTick = result[i - 1] + 1;          // раздвижка: удары не сливаются
                if (tick < minTick) tick = minTick;
                if (tick > lastAllowed) tick = lastAllowed; // упор в границу интервала

                result.Add(tick);
            }
            return result.Count;
        }

        /// <summary>
        /// Доля тика, в которую попадает момент контакта, — остаток, который целочисленный расчёт
        /// отбрасывает. Нужна ПОДАЧЕ: показ ставит вспышку, цифру, звук и hitstop в тот кадр, где меч
        /// коснулся, вместо границы тика (разброс до 33 мс).
        /// <para><b>Модель не меняется вовсе:</b> удар по-прежнему принадлежит тику
        /// <paramref name="actualWindupTicks"/>. Здесь только дробь от того же произведения
        /// <c>доля × длительность</c>, что даёт и сам замах — второго владельца формулы не заводим.</para>
        /// <para><b>Дробь стала честной 06.08.2026.</b> Пока путь доли округлял к БЛИЖАЙШЕМУ, истинный
        /// момент мог оказаться и раньше тика модели, и выразить его в [0..1) было нельзя — метод честно
        /// отдавал ноль. С округлением ВНИЗ остаток существует всегда и означает ровно то, что говорит.</para>
        /// <para><b>Возвращает 0 там, где точности нет:</b> замах склампился (телеграф-пол или потолок
        /// «интервал − 1») либо его сдвинул множитель (разбег, взведённое комбо) — момент уже сдвинут
        /// искусственно, и дробь от сдвинутого числа была бы точностью, которой нет.</para>
        /// </summary>
        /// <param name="actualWindupTicks">
        /// Длина замаха, с которой удар РЕАЛЬНО занесён (<see cref="WindupTicksFor"/>). Расхождение с
        /// несклампленным расчётом и есть признак того, что момент сдвинут.
        /// </param>
        public static float ContactSubTick(RuntimeUnit unit, int actualWindupTicks)
        {
            if (unit?.Unit == null) return 0f;
            if (unit.Unit.Channel.Exists && unit.Unit.Channel.WindupSeconds > 0f) return 0f;

            float share = unit.Unit.WindupShare;
            if (share <= 0f) return 0f;

            return ContactSubTick(share, IntervalTicks(unit.Stats.Get(StatType.AttackSpeed)),
                actualWindupTicks, unit.Unit.AttackSwingTicks);
        }

        /// <summary>
        /// Арифметика доли контакта — та же, что у <see cref="WindupTicksFromShare"/>, но отдельно от
        /// чтения данных юнита: так её проверяет тест без ассетов и без движка.
        /// </summary>
        /// <param name="actualWindupTicks">Длина замаха, с которой удар реально занесён.</param>
        public static float ContactSubTick(float windupShare, int intervalTicks,
            int actualWindupTicks, int maxAnimTicks = 0)
        {
            if (windupShare <= 0f) return 0f;

            int durationTicks = AttackDurationTicks(intervalTicks, maxAnimTicks);
            double raw = Math.Min(1f, Math.Max(0f, windupShare)) * durationTicks;

            if ((int)Math.Floor(raw) != actualWindupTicks) return 0f;   // кламп или множитель сдвинули момент

            return (float)(raw - Math.Floor(raw));
        }

        /// <summary>
        /// Замах из ДОЛИ свинга (0..1) — единственный путь с 06.08.2026. Кламп и множитель разбега те же,
        /// что были у покадрового расчёта: границы у замаха одни, кем бы он ни был задан.
        /// </summary>
        /// <remarks>
        /// <b>Округление ВНИЗ, и это не вкусовщина.</b> Прежний покадровый путь считал
        /// <c>(hitFrame × duration) / frameCount</c> целочисленным делением, то есть отбрасывал остаток.
        /// Округление к ближайшему сдвинуло бы момент удара на тик у 40% скоростей атаки — то есть перевод
        /// на доли молча поехал бы по всему ростеру. Вниз — ещё и честнее по смыслу: удар не должен
        /// прилетать ПОЗЖЕ, чем его показывает анимация.
        /// </remarks>
        /// <param name="maxAnimTicks">Потолок свинга юнита в тиках; <c>0</c> = глобальный дефолт.</param>
        public static int WindupTicksFromShare(float share, int intervalTicks, float windupMultiplier = 1f,
            int maxAnimTicks = 0)
        {
            int durationTicks = AttackDurationTicks(intervalTicks, maxAnimTicks);
            int raw = (int)Math.Floor(Math.Min(1f, Math.Max(0f, share)) * durationTicks);
            return ClampWindup(raw, intervalTicks, windupMultiplier);
        }

        // Здесь была перегрузка WindupTicks(hitFrame, frameCount, …) — покадровый расчёт замаха. Снята
        // 06.08.2026 вместе со всем покадровым путём, и не только за ненадобностью: её первый параметр
        // int против float у доли делал СТАРЫЕ вызовы молча компилируемыми — аргументы съезжали на
        // позицию, компилятор молчал, а тест падал в другом месте с невнятным «нет хвоста».

        // Границы замаха — один владелец на все способы его задать (кадры клипа, доля из данных):
        // пол MinWindupTicks (телеграф, ниже которого удар нечитаем) и потолок intervalTicks − 1
        // (удар не совпадает с тиком старта следующей атаки). Множитель применяется ДО клампа.
        private static int ClampWindup(int raw, int intervalTicks, float windupMultiplier)
        {
            if (windupMultiplier > 0f && Math.Abs(windupMultiplier - 1f) > 1e-4f)
                raw = (int)Math.Round(raw * windupMultiplier, MidpointRounding.AwayFromZero);

            int upper = intervalTicks - 1;
            if (upper < 0) upper = 0;

            int lower = SimConstants.MinWindupTicks;
            if (lower > upper) lower = upper;   // очень короткий интервал: пол не может превысить потолок

            if (raw < lower) raw = lower;
            if (raw > upper) raw = upper;
            return raw;
        }
    }
}
