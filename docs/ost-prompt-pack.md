# OST — промпт-пакеты для Suno

**Рабочий инструмент, не документация.** Копируешь блок → вставляешь в Suno → слушаешь.
Держит **только актуальное состояние промптов**: истории правок здесь нет намеренно (решение
`2026-07-30/19`).

| Что искать | Где |
|---|---|
| прогоны, seed'ы, вердикты | `docs/ost-run-log.md` |
| почему промпт такой и что отвергли | `docs/wiki/gdd/00-meta/journal-adr.md` |
| правила промптинга, проверенные ушами | `.claude/skills/xgaida-x-nixi-audio/references/suno-prompting.md` |
| замысел: закон трека, рефы, роль вокала | `.../references/voice-and-music.md` |

Модель — **v5.5**. Режим — **«Расширенный»** (в «Простом» нет ни Exclude, ни слайдеров).

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

### 01-A · инструментал: нежная и сильная, сила приходит аркой

Промпт **намеренно короткий** — шесть тегов вместо десяти: слова про энергию складываются, и трек
уезжает в боевик. Сила берётся не из плотности, а из **арки** (теги в Lyrics) и гармонии.

```
epic anime battle score, tender but powerful, lyrical melody over restrained percussion, warm strings
with electric guitar lift, 132 BPM, D dorian
```
Исключить: `busy drums, taiko, war march, flute, solo violin`

Текст → **«Писать»**, и туда только теги арки, без слов:
```
[Intro - Soft]
[Verse - Intimate]
[Chorus - Powerful]
[Bridge - Quiet]
[Final Chorus - Climactic]
[Outro - Diminuendo]
```

Если после этого модель всё же запоёт — вернуть тумблер **Инструментал** и снять теги.

### 01-B · женский вокализ

Текст → **«Писать»** (не «Промпт»: тот отдаст сочинение модели, и она напишет английские слова).

```
epic anime battle score, female soprano wordless vocalise in the lead, airy but powerful, controlled
vibrato, close-mic breath, staccato string section ostinato, electric guitar singing over it, drum kit
and shimmering bells, 158 BPM, D dorian, no autotune
```
Исключить: `flute, solo violin, taiko, war march, spoken word`

Текст — **на всю длину трека**, секции разделяются переносами строк:

```
[Intro]
a-a-i-a... o-o-ve-la...

[Verse]
mia so-o-nta ve-la
e-na do-re-a li-a-ne
so-la mi-re-na va-ia
o-re-na ta-li-a so

[Chorus]
e-lun do-re-a li-a-ne
ai-a so-o-nta ve-la
e-lun do-re-a mi-o
la-ia ve-so-o-na

[Verse]
ne-va so-li-a re-o
mi-e-na ta-la vo-re-a
so-o-la ni-a me-na
e-ra li-o-ne va

[Chorus]
e-lun do-re-a li-a-ne
ai-a so-o-nta ve-la
e-lun do-re-a mi-o
la-ia ve-so-o-na

[Bridge]
o-o-o... a-i-a...
so-la-re-na... ve-o-na...

[Chorus]
e-lun do-re-a li-a-ne
ai-a so-o-nta ve-la
e-lun do-re-a mi-o
la-ia ve-so-o-na

[Outro]
a-a-ia... ve-la...
```

### 01-C · тёмная краска, из которой вырастет фаза 2

```
dark epic anime battle score, heavy and resolute, staccato low-mid strings driving hard, church organ
underneath, electric guitar singing over it, drum kit, 152 BPM, D dorian, no intro, no vocals, weight
without dread
```
Исключить: `flute, solo violin, bright bells, celesta, war march`

### 01-F · хор вместо солистки

```
epic anime battle score, female choir singing wordless syllables in pairs, staccato string section
ostinato, electric guitar singing over it, drum kit and shimmering bells, 158 BPM, D dorian, no intro,
resolute not frightening
```
Текст → **Инструментал** (хор просим составом, не текстом).
Исключить: `solo vocals, flute, solo violin, taiko, spoken word`

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
solo lute, medieval fantasy, intimate and unhurried, 76 BPM, D dorian, close-mic, dry room,
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
sparse and warm fantasy, unhurried, 72 BPM, D dorian, no percussion, intimate room, no vocals
```

**Критерий выживания:** мотив узнаётся во всех трёх и нигде не звучит чужеродно.

---

## Пакет 03 — вокал, конланг и Persona (собирается после 02)

Заводится **только когда мотив утверждён**. Persona — **до массовой генерации**, иначе к десятому
треку тембр уедет. Палитра слогов и приёмы — в `suno-prompting.md`.
