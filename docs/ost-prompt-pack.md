# OST — промпт-пакеты для Suno

**Рабочий инструмент, не документация.** Копируешь блок → вставляешь в Suno → слушаешь.
Держит **только актуальное состояние промптов**: истории правок здесь нет намеренно (решение
`2026-07-30/19`).

| Что искать | Где |
|---|---|
| прогоны, seed'ы, вердикты | `docs/ost-run-log.md` |
| почему промпт такой и что отвергли | `docs/wiki/gdd/00-meta/journal-adr.md` |
| правила промптинга, проверенные ушами | `.claude/skills/xgaida-x-nixi-music/references/suno-prompting.md` |
| целевой тон, канон состава, рефы | `.../xgaida-x-nixi-music/references/tone-and-lineup.md` |
| тариф, права, гигиена библиотеки | `.../xgaida-x-nixi-music/references/account-and-rights.md` |

Режим — **«Расширенный»** (в «Простом» нет ни Exclude, ни слайдеров).

**Модель — v5.5 базой, v5 вторым прогоном того же промпта.** Версии различаются характером, а не
свежестью: v5.5 выразительная и слушает указания, v5 чище по миксу. Подробности — в
`suno-prompting.md` §Выбор модели.

---

## Пакет 01 — настроение босса

**Цель: найти настроение на самом важном материале** — боссовом бою. Проверяется главный закон трека:
**«решительно, а не страшно»**.

### Общие настройки

| Параметр | Значение |
|---|---|
| Странность | **60%** |
| Влияние стиля | **60%** |
| Audio Influence («+ Аудио») | не используем — сэмпла на входе ещё нет |
| Длительность | Auto |
| Тональный центр | **D** во всех блоках (иначе слои в FMOD не сойдутся) |
| Название песни | `01A - Boss Theme - 1`, нумерация сквозная по генерациям |

**В «Исключить стили» слово `no` не пишется** — поле само означает исключение. Отрицание идёт в поле
Стилей (`no vocals` там работает).

**Исключить стили — ОДИН-ДВА пункта на промпт**, свои у каждого блока (перегрузка негативами путает
модель так же, как перегрузка тегами). Лишнее вытесняется точным позитивом, а не запретами.
Правила формулировки — `suno-prompting.md` §НЕГАТИВНЫЕ ПРОМПТЫ, канон состава по ролям —
`tone-and-lineup.md` §КАНОН СОСТАВА.

### 01-A · ДВЕ ТЕМЫ раздельно, а не один трек

**Почему раздельно.** Suno держит 6-12 тегов, а наших требований больше двадцати — одним промптом их не
уложить. И не нужно: боссовая кульминация у Кадзиуры устроена как **две контрастные темы**, а у нас трек
всё равно живёт слоями в FMOD. Гоним части, соединяет FMOD. Подробнее — `suno-prompting.md` §СТРАТЕГИЯ.

Текст → **Инструментал** (тумблер) в обеих.

**01-A1 · агрессивная тема — рок против медиевальной мелодии**
```
dark battle score, medieval modal melody against driving rock percussion and heavy synth, open fourths
and fifths, hard-hitting drums, bass guitar underneath, tense and desperate, 150 BPM, D dorian, no vocals
```
Исключить: `taiko, war march`

**01-A2 · смертельная тема — соло-скрипка**
```
solo violin outlining a modal melancholic melody, beautiful and deadly, high register, slightly
overdriven electric guitar answering with a rare solo, low cello underneath, sparse hard drums,
132 BPM, D dorian, no vocals
```
Исключить: `triumphant fanfare, j-pop`

**Что здесь новое и откуда:** `driving rock percussion and heavy synth` — прямая формула SAO из разбора
(рок и синт **противопоставлены** средневековой мелодии, чтобы обозначить «это внутри видеоигры»).
`open fourths and fifths` — её приём средневековости. Синт разрешён: прежний запрет касался электронной
подачи целиком, а не краски.

**Ступени в промпт не просим.** «В 4 шага» Suno по заказу не сделает — четыре слоя в FMOD дают то же
самое и управляются в рантайме.

**Теги арки сюда не ставятся** — поле Lyrics занято тумблером «Инструментал». Проверяются в 01-B.

### 01-B · женский вокализ

Текст → **«Писать»** (не «Промпт»: тот отдаст сочинение модели, и она напишет английские слова).

```
cinematic battle score, female soprano wordless vocalise in the lead, desperate and determined,
controlled vibrato, close-mic breath, violin answering the voice, slightly overdriven guitar, low cello
and bass guitar underneath, hard drums, 140 BPM, D dorian, no autotune
```
Исключить: `spoken word, j-pop`

Текст — **на всю длину трека**, секции разделяются переносами строк. **Теги арки стоят прямо в
заголовках секций** — здесь они проверяются заодно, поле Lyrics всё равно занято:

```
[Intro - Soft]
a-a-i-a... o-o-ve-la...

[Verse - Intimate]
mia so-o-nta ve-la
e-na do-re-a li-a-ne
so-la mi-re-na va-ia
o-re-na ta-li-a so

[Chorus - Powerful]
e-lun do-re-a li-a-ne
ai-a so-o-nta ve-la
e-lun do-re-a mi-o
la-ia ve-so-o-na

[Verse - Building]
ne-va so-li-a re-o
mi-e-na ta-la vo-re-a
so-o-la ni-a me-na
e-ra li-o-ne va

[Chorus - Powerful]
e-lun do-re-a li-a-ne
ai-a so-o-nta ve-la
e-lun do-re-a mi-o
la-ia ve-so-o-na

[Bridge - Quiet]
o-o-o... a-i-a...
so-la-re-na... ve-o-na...

[Final Chorus - Climactic]
e-lun do-re-a li-a-ne
ai-a so-o-nta ve-la
e-lun do-re-a mi-o
la-ia ve-so-o-na

[Outro - Diminuendo]
a-a-ia... ve-la...
```

### 01-C · тёмная краска, из которой вырастет фаза 2

```
dark battle score, grim and resolute, violin lead over heavy low-mid strings, church organ underneath,
slightly overdriven guitar, hard drums and bass guitar, 138 BPM, D dorian, no intro, no vocals,
weight without dread
```
Исключить: `bright bells, triumphant fanfare`

### 01-F · хор вместо солистки

```
dark battle score, female choir singing wordless syllables in pairs, desperate and determined, violin
over driving rock percussion and heavy synth, bass guitar underneath, 145 BPM, D dorian, no intro,
resolute not frightening
```
Текст → **Инструментал** (хор просим составом, не текстом).
Исключить: `solo vocals, j-pop` — здесь нужен хор, а не солистка.

### Что слушать при отборе

1. **Держит ли «решительно, а не страшно»** — музыка на стороне игрока, а не описывает угрозу.
2. **Есть ли фраза, которую можно просвистеть** — 4-7 нот. Стена звука без фразы темой не станет.
3. **Что несёт мелодию** — голос, гитара, струнные или хор. Это определит все остальные контексты.
4. **Заряжает ли** (вопрос от рефа Windblown) — или просто красиво звучит.

Плюс проверка, которая обычно решает: **выключи и вернись через десять минут.**

### Что делать с найденным

Скачать **сразу**, WAV, в `FMOD Project/MusicSource/`, имя как в поле «Название песни». Вердикт и
seed — в `docs/ost-run-log.md`.

---

## Пакет 02 — тест на выживание (собирается после 01)

Кандидат режется до **30-60 с чистого мотива** (меньше 15 — Suno лупит вербатим, больше 60 —
фрагментирует) и подаётся сэмплом через **Audio Influence 65%**: мелодия сохраняется, остальное
переоркеструется.

**02-A · соло, беднейшая подача — обязательный первый тест.** Вокал только на боссах, значит тему
обязано быть чем играть на карте и в таверне. Рассыпается здесь — значит это была аранжировка, а не
тема.
```
solo lute, neo-medieval, intimate and unhurried, 76 BPM, D dorian, close-mic, dry room,
no percussion, no vocals
```

**02-B · тёмная аранжировка — фаза 2.** Тёмный **не значит тихий**: плотность и громкость не падают,
уходит свет.
```
same melody darker, 152 BPM, D dorian, massive low-mid strings and organ, heavy and resolute,
no bright bells, no high strings
```

**02-C · мир — карта и таверна.**
```
sparse and warm neo-medieval, unhurried, 72 BPM, D dorian, delicate piano, spacious reverb,
no percussion, no vocals
```

**Критерий выживания:** мотив узнаётся во всех трёх и нигде не звучит чужеродно.

---

## Пакет 03 — вокал, конланг и Persona (собирается после 02)

Заводится **только когда мотив утверждён**. Persona — **до массовой генерации**, иначе к десятому
треку тембр уедет. Палитра слогов и приёмы — в `suno-prompting.md`.
