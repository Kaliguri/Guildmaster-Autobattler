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

**Исключить стили — ОДИН И ТОТ ЖЕ набор во всех блоках:**
```
taiko, war march, triumphant fanfare, j-pop, metal riff
```
Состав задаётся точно в позитиве, поэтому латать исключениями больше нечего. Канон состава по ролям —
`tone-and-lineup.md` §КАНОН СОСТАВА.

### 01-A · инструментал: мрак снизу, бой от пульса, нежность только в лиде

Текст → **Инструментал** (тумблер).

```
dark cinematic battle score, tense and desperate, violin lead beautiful and deadly, slightly overdriven
electric guitar with rare solo, low cello ostinato and bass guitar underneath, hard-hitting drums,
builds in steps then drops away, 150 BPM, D dorian, no vocals
```

**Состав из канона после разбора SAO:** скрипка и барабаны вернулись, гитара стала грязноватой с
редким соло, добавлена бас-гитара. `builds in steps then drops away` — ступенчатость и паузы, без
которых трек «не развлекает собой».

**Теги арки сюда не ставятся.** Они живут в поле Lyrics, а его занимает тумблер «Инструментал» —
включить оба нельзя, а отключить тумблер значит остаться без запрета голоса. Проверяются в 01-B, где
текст нужен по существу.

### 01-B · женский вокализ

Текст → **«Писать»** (не «Промпт»: тот отдаст сочинение модели, и она напишет английские слова).

```
cinematic battle score, female soprano wordless vocalise in the lead, desperate and determined,
melancholic undertone, airy but powerful, controlled vibrato, close-mic breath, clean electric guitar
answering the voice, warm string section, restrained low drum pulse, 132 BPM, D dorian, no autotune
```

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
dark cinematic battle score, grim and resolute, clean electric guitar lead with long sustain, heavy
low-mid string section, church organ underneath, restrained low drum pulse, 138 BPM, D dorian, no intro,
no vocals, weight without dread
```

### 01-F · хор вместо солистки

```
cinematic battle score, female choir singing wordless syllables in pairs, desperate and determined,
melancholic undertone, clean electric guitar lead, warm string section, restrained low drum pulse,
132 BPM, D dorian, no intro, resolute not frightening
```
Текст → **Инструментал** (хор просим составом, не текстом).
Исключить — тот же постоянный набор **плюс** `solo vocals` вместо `solo violin`: здесь нужен именно хор.

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
