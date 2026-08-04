using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Маркеры-как-данные на <see cref="AnimationClip"/> (вики «13» §3.1, «14»). Маркер — это
    /// <see cref="AnimationEvent"/>, чьё <c>functionName</c> называет ВИД маркера; контакт удара —
    /// <see cref="HitFunction"/>. Всё читается в рантайме (<c>clip.events/length/frameRate</c>), без
    /// editor-API. Источник тайминга удара авто-атаки: сим берёт
    /// <see cref="UnitVisual.AttackFrameCount"/>/<see cref="UnitVisual.AttackHitFrame"/>, которые
    /// выводятся отсюда. <c>Animator.fireEvents=false</c> — маркеры никогда не колбэчат.
    /// </summary>
    /// <remarks>
    /// <b>Имя маркера контакта — <c>"Hit"</c>, а не безликое <c>"Marker"</c></b> (2026-07-31). Пока вид
    /// маркера был один, родовое имя не мешало; с приходом разметки самого взмаха (границы strike-зоны
    /// для слеш-дуги) «маркер» становится словом для трёх разных событий, и молчаливое равенство
    /// «маркер = контакт» превратилось бы в ловушку: новый маркер с тем же именем сим засчитал бы за
    /// лишний удар. Имя вида отвечает за смысл, класс — за чтение.
    /// <para><b>Маркеров контакта может быть несколько — это Атака из нескольких Ударов</b>
    /// (2026-07-30/6). Каждый = одно разрешение контакта: свой расчёт урона, своё попадание, свой
    /// on-hit. Позиции считаются в нормированном времени клипа и умножаются на длительность свинга,
    /// поэтому серия сжимается вместе с ним; раздвижку до зазора в тик и клампы делает
    /// <c>AttackTiming</c>, а не авторинг — свинг сжимается рантайм-скоростью атаки, и валидатор клипа
    /// этого не увидел бы.</para>
    /// <para>Правила серии, которые держит уже не этот класс: рефанд кулдауна положен только свингу без
    /// единого контакта, промах считается состоявшимся контактом, каст между контактами не втискивается
    /// (<c>tech/00-meta/tech-debt</c> §3.9).</para>
    /// </remarks>
    public static class ClipMarkers
    {
        /// <summary>Имя маркера КОНТАКТА: один такой маркер = один разрешённый удар.</summary>
        public const string HitFunction = "Hit";

        /// <summary>
        /// Имя маркера НАЧАЛА ВЗМАХА — момент, с которого клинок пошёл на цель. Не контакт: урона здесь
        /// нет и быть не может, поэтому имя обязано отличаться от <see cref="HitFunction"/>.
        /// </summary>
        public const string StrikeStartFunction = "StrikeStart";

        /// <summary>Имя маркера КОНЦА ВЗМАХА — дальше идёт возврат, который ничего не сообщает.</summary>
        public const string StrikeEndFunction = "StrikeEnd";

        /// <summary>
        /// Имя маркера «ЩИТ ВСТАЛ»: кадр, на котором поза гвардии достигнута и дальше клип её только держит.
        /// </summary>
        public const string GuardUpFunction = "GuardUp";

        /// <summary>
        /// Имя маркера «ЩИТ ПОШЁЛ ВНИЗ»: кадр, с которого клип возвращает руку в стойку. Всё, что после
        /// него, — возврат, и играть его надо тогда, когда защита кончилась, а не когда кончился клип.
        /// </summary>
        public const string GuardDownFunction = "GuardDown";

        /// <summary>Время первого контакта (сек) или <c>-1</c>, если маркера нет.</summary>
        public static float FirstHitTime(AnimationClip clip) => FirstTimeOf(clip, HitFunction);

        /// <summary>Время первого маркера этого вида (сек) или <c>-1</c>, если такого в клипе нет.</summary>
        public static float FirstTimeOf(AnimationClip clip, string functionName)
        {
            if (clip == null) return -1f;
            AnimationEvent[] events = clip.events;
            for (int i = 0; i < events.Length; i++)
                if (events[i].functionName == functionName) return events[i].time;
            return -1f;
        }

        /// <summary>
        /// Сколько контактов размечено в клипе. <c>0</c> = клипа или маркеров нет.
        /// </summary>
        public static int HitCount(AnimationClip clip)
        {
            if (clip == null) return 0;
            AnimationEvent[] events = clip.events;
            int n = 0;
            for (int i = 0; i < events.Length; i++)
                if (events[i].functionName == HitFunction) n++;
            return n;
        }

        /// <summary>
        /// Нормированные позиции ВСЕХ контактов (0..1) по возрастанию времени, дописанные в
        /// <paramref name="result"/>. Возвращает их число.
        /// </summary>
        /// <remarks>
        /// Порядок событий в клипе Unity держит отсортированным по времени, но полагаться на это здесь
        /// нельзя: маркеры расставляются и руками, и генератором Animation Lab, а порядок контактов —
        /// это порядок ударов. Поэтому сортировка своя, вставкой: маркеров единицы.
        /// </remarks>
        public static int HitNormalizedAll(AnimationClip clip, System.Collections.Generic.List<float> result)
        {
            if (result == null) return 0;
            result.Clear();
            if (clip == null || clip.length <= 0f) return 0;

            AnimationEvent[] events = clip.events;
            for (int i = 0; i < events.Length; i++)
            {
                if (events[i].functionName != HitFunction) continue;

                float t = Mathf.Clamp01(events[i].time / clip.length);
                int at = result.Count;
                while (at > 0 && result[at - 1] > t) at--;
                result.Insert(at, t);
            }
            return result.Count;
        }

        /// <summary>Число кадров клипа = <c>round(length × frameRate)</c>. 0 если клип пуст.</summary>
        public static int FrameCount(AnimationClip clip) =>
            clip == null ? 0 : Mathf.Max(0, Mathf.RoundToInt(clip.length * clip.frameRate));

        /// <summary>Кадр первого контакта = <c>round(time × frameRate)</c>. 0 если клипа/маркера нет.</summary>
        public static int HitFrame(AnimationClip clip)
        {
            float t = FirstHitTime(clip);
            return t < 0f || clip == null ? 0 : Mathf.RoundToInt(t * clip.frameRate);
        }

        /// <summary>Нормированное время первого контакта (0..1). 0 если клипа/маркера/длины нет.</summary>
        public static float HitNormalized(AnimationClip clip)
        {
            float t = FirstHitTime(clip);
            return clip != null && t >= 0f && clip.length > 0f ? Mathf.Clamp01(t / clip.length) : 0f;
        }

        /// <summary>
        /// Окно ВЗМАХА в нормированном времени клипа: от <see cref="StrikeStartFunction"/> до
        /// <see cref="StrikeEndFunction"/>. На нём живёт дуга за клинком, и только на нём: до начала
        /// клинок ещё собирается, после конца — возвращается.
        /// </summary>
        /// <param name="clip">Клип атаки.</param>
        /// <param name="start">Нормированное начало взмаха.</param>
        /// <param name="end">Нормированный конец взмаха.</param>
        /// <returns>
        /// <c>false</c>, если клип не размечен целиком (нет одного из маркеров) или окно вырождено.
        /// Половина разметки — это не «часть данных», а неразмеченный клип: показывать взмах от маркера
        /// до конца клипа значило бы врать про возврат.
        /// </returns>
        /// <remarks>
        /// Границы взмаха размечаются по замеру скорости кончика оружия (<c>RigSweep</c>), а не на глаз:
        /// в клипе <c>Attack</c> костяного рига это 0.333–0.567 с при контакте на 0.467 с.
        /// </remarks>
        public static bool StrikeWindowNormalized(AnimationClip clip, out float start, out float end)
        {
            start = 0f;
            end   = 0f;
            if (clip == null || clip.length <= 0f) return false;

            float s = FirstTimeOf(clip, StrikeStartFunction);
            float e = FirstTimeOf(clip, StrikeEndFunction);
            if (s < 0f || e < 0f || e <= s) return false;

            start = Mathf.Clamp01(s / clip.length);
            end   = Mathf.Clamp01(e / clip.length);
            return end > start;
        }

        /// <summary>
        /// Окно ДЕРЖАНИЯ гвардии в нормированном времени клипа: от <see cref="GuardUpFunction"/> (щит
        /// встал) до <see cref="GuardDownFunction"/> (щит пошёл вниз). Всё до <paramref name="up"/> —
        /// подъём, всё после <paramref name="down"/> — возврат в стойку.
        /// </summary>
        /// <returns>
        /// <c>false</c>, если клип не размечен целиком или окно вырождено. Половина разметки — это
        /// неразмеченный клип: показу нужны обе границы, потому что он играет три куска клипа по трём
        /// разным часам (подъём — за время подводки, держание — пока живёт барьер, возврат — за своё).
        /// </returns>
        /// <remarks>
        /// Так же, как у взмаха, границы живут В КЛИПЕ, а не константой в показе. До 2026-08-04 доля
        /// подъёма стояла числом в <c>UnitView</c> (0.2 клипа) — и совпадала с позой ровно до первой правки
        /// клипа: автор снял промежуточный ключ на 0.117 с, подъём переехал на 0.25 с, а показ продолжил
        /// скрабить до 0.12 с и оставлял щит поднятым на 60% вместо 84% (замер перекрытия тела, RigSweep).
        /// Второй владелец одного факта — дефект; владельцем назначен клип.
        /// </remarks>
        public static bool GuardWindowNormalized(AnimationClip clip, out float up, out float down)
        {
            up   = 0f;
            down = 0f;
            if (clip == null || clip.length <= 0f) return false;

            float u = FirstTimeOf(clip, GuardUpFunction);
            float d = FirstTimeOf(clip, GuardDownFunction);
            if (u < 0f || d < 0f || d <= u) return false;

            up   = Mathf.Clamp01(u / clip.length);
            down = Mathf.Clamp01(d / clip.length);
            return down > up;
        }
    }
}
