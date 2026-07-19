**Тех-ресерч: темы для разговора (не execution)**  
**Модель:** Cursor Grok 4.5  
**Дата:** 2026-07-19  
**Тип:** research agenda — мнение, не бэклог задач; всё **`proposed`**  
**Контекст:** чисто технические развилки Guildmaster; без UITK, без ГДД-продукта, без «давай чинить сейчас»

Пара к аудитам того же дня: combat · seams/data/flow/stats · patterns-to-add.

---

## Зачем файл

Список тем, по которым **имеет смысл говорить** (whiteboard / спор / ADR), потому что ошибка дорого чинится задним числом.  
Это не план спринта и не список багов — если тема закрылась решением Макса, её переносят в `tech-changelog`, а здесь помечают `decided` / вычёркивают.

---

## Как вести (формат одной темы)

1. Вопрос одной фразой  
2. 2–3 варианта  
3. Критерии выбора (детерминизм / persist / host-auth / стоимость ретрофита)  
4. Что сознательно **не** берём  
5. Куда потом записать (reference / explanation / changelog)

Без «почини по ходу» в той же сессии.

---

## Приоритет для разговора

| # | Тема | Почему сейчас | Ориентир глубины |
|---|---|---|---|
| **A** | Владелец фазы + teardown persist | Живой persist-мир; cancel без reset уже ломает арену | 1 сессия → ADR |
| **B** | Intent → `ISimCommand` + checksum | Host-auth выбран; без формы команд MP будет переписывать Deployment | 1–2 сессии |
| **C** | Два часа (тики / float CD / timeScale) | Уже три шкалы; до коопа надо решить, что «истина» | 1 сессия |
| **D** | Frame clock / alpha | Известный долг §3.3; дёшево решить на бумаге | полсессии |
| **E** | Outward event policy (C# vs MessagePipe) | Dual bus уже путает consumers | полсессии |
| **F** | Ability как контракт данных | XOR heal/damage + вне реестра — язык авторов | 1 сессия |
| **G** | Save `schemaVersion` migrations | Док требует, рантайма нет; до внешнего билда | 1 сессия |
| **H** | Зомби-тик (`HP≤0` / `!IsDead`) | Модель времени тика, не «баг Death» | полсессии |
| **I** | `ICombatContext` mutation vs notify | До раздувания API | полсессии |
| **J** | Write-back `RunState` (что durable) | Persist обещает «отряд = забег» | 1 сессия |
| **K** | Semantic content validation | Какие правила в CI vs Doctor | полсессии |
| **L** | Pause façade (sim + TimeScale) | Два пути паузы уже разъезжаются | полсессии |
| **M** | Seed pipeline (где ещё Random) | Репро / ретраи / будущий кооп | 1 сессия |
| **N** | Authority гейт тика / late-join | Только после B; иначе абстракция в вакууме | позже |
| **O** | Replay log / sim pools | Инструменты и масштаб; не блокер фестиваля | позже |

**Одна тема на вечер (рекомендация Cursor Grok 4.5):** начать с **A**, затем **B**. Остальное — по мере касания кода.

---

## Карточки тем (мнение)

### A. Phase owner + teardown

**Вопрос:** кто единственный пишет `BattlePhase` и гарантирует world-reset при cancel/dispose?  
**Варианты:** (1) только `BattleSession` API enter/exit; (2) `BattleBootstrap` как единственный teardown; (3) оставить split + дисциплина.  
**Мнение:** (1) или тонкий фасад над session — иначе persist-регрессии будут повторяться. Cancel → `try/finally RequestReset` — следствие решения, не отдельный «фиксык».  
**Не брать:** выгрузку BattleScene на каждый узел (откат persist-модели).

### B. Intent → Command + checksum

**Вопрос:** какая форма player intent и что входит в отпечаток боя?  
**Варианты:** тонкий checksum как сейчас; расширенный fingerprint; seed+command log как источник реплея.  
**Мнение:** сначала зафиксировать **форму команды** `(tick, playerId, seq, payload)` и запрет клиенту писать в sim; checksum расширять под MP, не под перфекционизм соло.  
**Не брать:** lockstep / fixed-point (уже отвергнуто).

### C. Два (три) часа

**Вопрос:** что является каноном длительности — int-тики, float-секунды способностей, или wall-clock через timeScale?  
**Мнение:** сим-истина = тики; Ability CD лучше со временем в тики (или явный «float только UI-facing»); timeScale — только презентация/петля, не формула урона.  
**Не брать:** смешивать cinematic slowmo с сид-реплеем без отдельного канала.

### D. Frame clock

**Вопрос:** кто владеет `InterpolationAlpha`?  
**Мнение:** единственный писатель — `CombatLoopService` (`accumulator / TickDelta`); presenter только читает. Дешёвый ADR, закрывает §3.3.  
**Не брать:** интерполяцию внутри Combat-сборки.

### E. Outward events

**Вопрос:** C# events полные, MessagePipe — subset. Кто truth?  
**Мнение на фестиваль:** C# = presentation truth; MP = удобство Game (Feel/UI); **явно в доке**. Перед богатым кооп-UI — полный republish в MP.  
**Не брать:** третий канал (SO events).

### F. Ability contract

**Вопрос:** один язык payload и место Ability в реестре?  
**Мнение:** сначала контракт heal+damage vs TargetMode (валидатор); `ability.*` ids даже nested; ScalableValue для Ability — отдельное решение, не мешать в один ADR.  
**Не брать:** вынос Ability в отдельные SO «потому что красиво» без нужды в loc/audio/реестре.

### G. Save migrations

**Вопрос:** цепочка `schemaVersion` на load.  
**Мнение:** pipeline миграций за `ISaveService` до первого внешнего сейва у игроков; editor migrations ≠ runtime.  
**Не брать:** ES3 как повод отложить versioning (бэкенд сменится, DTO+version останутся).

### H. Зомби-тик

**Вопрос:** юнит с `HP≤0` действует до Death в конце тика — фича или дыра?  
**Мнение:** либо явный контракт «last breath» в simulation.md, либо early-out `CurrentHP<=0` в AA/Ability. Молчание хуже любого из двух.  
**Не брать:** перенос Death в середину тика без пересмотра event drain / thorns.

### I. Context vs sink

**Вопрос:** notify на `ICombatContext` ок?  
**Мнение:** вынести в sink/events до следующего notify-метода; mutation API держать узким. Не срочно, если не плодим методы.

### J. RunState write-back

**Вопрос:** позиции/экип на арене — превью или durable?  
**Мнение:** commit на `StartCombat` (и опц. выход из тест-зоны) через один mutator; иначе persist врёт игроку.  
**Не брать:** запись в RunState на каждый pixel drag.

### K. Content validation

**Вопрос:** что блокирует CI?  
**Мнение:** Error в CI — id, null SerializeReference, Ability XOR/mode, polarity↔tag; Warning — legacy tags, resource soft-mismatch.  
**Не брать:** валидацию баланса чисел в CI.

### L. Pause façade

**Вопрос:** один API на sim + TimeScale?  
**Мнение:** да, до net PauseCommand; иначе третий путь паузы разъедет часы снова. Связано с C.

### M. Seeds

**Вопрос:** где ещё недетерминизм?  
**Мнение:** инвентаризация всех `UnityEngine.Random` / `DateTime` в Game/Combat path; battle seed = `runSeed + battleIndex` без retry (уже в доке) — проверить соблюдение.  
**Не брать:** Monte-Carlo в CI, пока бой RNG-free.

### N / O — позже

Authority гейт тика, late-join, replay, sim pools — после B и стабильного persist. Иначе ресерч в вакууме.

---

## Что не стоит как тех-ресерч

- CQRS / MediatR / DOTS / UniRx «для зрелости»  
- Полировка UITK-навигатора (это execution)  
- Балансные числа и карточки контента (ГДД / data-authoring)  
- Мелочи гигиены (`?.` на DI, мёртвые alias) — чинить по касанию, не обсуждать вечером

---

## Связь с patterns-to-add

| Ресерч-тема | Паттерн из proposals |
|---|---|
| A | Teardown / Phase owner |
| B | Intent → Command |
| D | Frame clock |
| E | Outward event policy |
| F / K | Content contract rules |
| G | Save migration pipeline |
| I | Presentation sink |
| J | Durable write-back |
| L | Pause façade |

Ресерч решает **что выбрать**; patterns-файл — **как это обычно называют и куда класть в код**.

---

## Подпись

**Автор мнения:** Cursor Grok 4.5  
**Статус:** `proposed` research agenda  
**Следующий шаг (если Макс согласен):** взять тему A или B → короткий ADR в `tech-changelog` + ссылка из explanation
