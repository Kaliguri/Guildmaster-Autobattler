---
name: xgaida-x-nixi-data-authoring
description: >-
  Дата-слой Guildmaster: авторинг контента и конфигов — три слоя SO→POCO→DTO, строковые id
  domain.name, ScriptableObject-определения (UnitData, RelicData, EffectData и родня),
  стат-блоки и StatsConfig, реестр контента, лок-ключи, валидация и контракты сериализации.
  Зови на любую работу с данными и всё под Assets/_Project/Scripts/Data и ScriptableObjects. НЕ
  применять к: ПОВЕДЕНИЮ эффектов и боевой логике (combat-sim — здесь только определение), окну
  Content Hub, дизайн-тексту ГДД (gdd-scribe), рантайм-UI (uitk).
---
# Data Authoring — рабочий контур Guildmaster

Этот скилл — процедура, а не справка. Он превращает правила дата-слоя в чеклист,
который прогоняется на КАЖДОЙ задаче с контентом. Цель — чтобы весь игровой контент
жил в трёх чётких слоях, связывался стабильными строковыми id, проходил валидацию и
не тянул за собой запрещённые практики сериализации, а новый контент ложился в готовые
контракты (`ContentDefinition`, `ContentDomains`, `IContentDatabase`), а не рядом с ними.

## Прежде всего: карта дата-слоя

Каркас уже построен и покрыт EditMode-тестами. Ничего не изобретай — читай, продолжай,
встраивайся в существующие швы.

| Что | Где |
|---|---|
| Базовый класс контент-SO (id `domain.name`, Edit Id) | `Assets/_Project/Scripts/Data/Definitions/ContentDefinition.cs` |
| Маппинг тип→домен + генерация id из имени | `Assets/_Project/Scripts/Data/Definitions/ContentDomains.cs` |
| Боевой кит юнита (база всего на арене) | `Assets/_Project/Scripts/Data/Definitions/UnitData.cs` |
| Реликвия игрока / враг (мета над китом) | `.../Definitions/RelicData.cs`, `.../Definitions/EnemyData.cs` |
| Прочие контент-типы (эффект/предмет/сосуд/энкаунтер/ивент…) | `Assets/_Project/Scripts/Data/Definitions/*.cs` |
| Стат-модификатор / список статов / операции | `Assets/_Project/Scripts/Data/Stats/StatModifier.cs`, `StatType.cs`, `ModifierOp.cs` |
| Глобальный шаблон статов (дефолты + константы) | `Assets/_Project/Scripts/Data/Definitions/StatsConfig.cs` |
| Реестр контента (шов код↔контент) | `.../Definitions/IContentDatabase.cs`, `ContentRegistry.cs`, `ContentDatabase.cs` |
| Лок-мост «контент ↔ String Table Content» | `Assets/_Project/Scripts/Data/Editor/ContentLocalization.cs` |
| id-утилиты / поиск ассетов (editor) | `Assets/_Project/Scripts/Data/Editor/ContentIdUtility.cs` |
| Массовая правка ассетов (Undo + аудит + обратный пресет) | `Assets/_Project/Scripts/Data/Editor/ContentEditService.cs`, `ContentEditBatch.cs`, `ContentCrudService.cs` |
| Валидация контента (id/дубли/null-ref) | `Assets/_Project/Scripts/EditorTools/ContentHub/Core/ContentValidationService.cs` |
| Тесты контента (EditMode) | `Assets/_Project/Tests/EditMode/Content/*.cs`, `.../ContentHub/*.cs` |
| Сами ассеты контента | `Assets/_Project/ScriptableObjects/**` |
| Канон дизайна дата-слоя | `docs/wiki/tech/13. Дата-слой и контент-каркас (SO и конфиги).md` |

**Слои (asmdef) — зависимость строго вниз:** `Core ← Data ← Combat`. `Guildmaster.Data`
не тянет `Combat`/`Presentation`/движковую презентацию. Контент-SO — чистые данные;
логика (сборка юнита, тик, эффекты) живёт в `Combat` и читает данные, а не наоборот.

## Три слоя данных (держать в голове всегда)

1. **SO (авторинг).** `ScriptableObject`-определения (`ContentDefinition` и наследники).
   Иммутабельны в рантайме. Их правит человек/инструмент в редакторе.
2. **Baked POCO (сим).** Чистый C#, снятый со снапшота SO на старте боя. С ним работает
   симуляция — детерминированно, без обращения к ассетам.
3. **DTO (сейвы/реплеи/Workshop).** Плоская сериализуемая форма для диска и сети. Ссылки
   между слоями — ТОЛЬКО строковый id `domain.name`, никогда прямой object-ссылкой.

Скилл владеет **контрактами** всех трёх слоёв. Реализация сим-POCO — на стороне `combat-sim`;
плумбинг сейв/загрузки (свой `JsonFileSaveService` за `ISaveService`) — будущий `save-system`.
Здесь — форма и id-дисциплина.

## Шесть правил, нарушение которых = переделка (HARD)

Каждое закрывает конкретный способ, которым дата-слой незаметно загнивает. Пойми «почему» —
тогда не придётся заучивать «нельзя».

1. **Связь слоёв — только строковый id `domain.name`; SO не мутируется в рантайме.**
   POCO/DTO ссылаются на контент по id, а не прямой object-ссылкой. SO — иммутабельный
   источник; менять его значения в игре нельзя (кроме осознанного live-тюнинга в play mode,
   см. `references/stats-and-configs.md`).
   *Почему:* id переживает пересборку, сеть и Workshop; прямые ссылки в POCO/DTO ломают
   детерминизм и сейвы. Мутация SO в рантайме — молчаливая порча общего источника.

2. **Запрещено навсегда:** Odin **Serializer** (`SerializedScriptableObject` — blob, AOT,
   конфликты netcode/Workshop); `AnimationCurve.Evaluate` из тика сима (кривые бейкаются в
   таблицы); `Resources.Load`/статик-доступ к конфигам; тихий code-fallback при отсутствующем
   конфиге (`config == null ? 5f` — падать громко при композиции, не подставлять число).
   *Почему:* каждый пункт — прецедент конкретного бага (потеря данных, недетерминизм,
   невидимая зависимость, «работало на дефолте, сломалось на реальных данных»).
   *Граница:* Odin **Inspector** (type-picker для `[SerializeReference]`, атрибуты
   `LabelText/Button/EnableIf`) — РАЗРЕШЁН, это удобство инспектора. Запрещён только Serializer.

3. **id генерируется и лочится.** `domain.name`, где `domain` — из ЗАКРЫТОГО словаря
   `ContentDomains` (новый тип → сперва зарегистрировать домен), `name` — `lower_snake` из
   имени ассета (авто через `OnValidate`). После выхода контента «в мир» (сейвы/реплеи/Workshop)
   id НЕ меняется; правка — только через кнопку **Edit Id** с предупреждением.
   *Почему:* id — стабильная идентичность. Меняешь id вышедшего контента — рвёшь чужие сейвы и
   реплеи. Rename ассета не должен трогать id (потому синхронизация имя→id одноразовая).

4. **Весь доступ к контенту — через `IContentDatabase`.** Геймплей берёт определения только
   из реестра (`Get<T>(id)` / `TryGet` / `All<T>`), зарегистрированного в DI. Никаких прямых
   SO-ссылок из логики и `Resources.Load`.
   *Почему:* реестр — единственный шов код↔контент. Он же — точка, куда позже сядут
   source-namespace под моды и addressable-загрузка (см. `references/localization-and-loading.md`).
   *Готча:* новый контент-SO не виден геймплею, пока не прогнан
   `Tools/Guildmaster/Sync Content Database` (в скрипте — `ContentDatabaseSync.Sync`). Симптом
   выглядит как «id не найден», хотя ассет на диске есть.
   Прямая ссылка в обход реестра убивает этот шов.

5. **Весь player-facing текст — лок-ключами, RU заполняем, прочие локали прочерк.**
   Ключи `{id}.name`/`{id}.desc` (+ спец-суффиксы, см. политику в `ContentLocalization`) в
   String Table `Content`. Текст в SO НЕ хранить — заводить/править ключи editor-хелпером.
   *Почему:* заложить локализацию сразу дёшево, ретрофитить — дорого. Прямая строка в SO =
   откат к нелокализуемому тексту.

6. **`[MovedFrom]` / `[FormerlySerializedAs]`-дисциплина.** Rename/перенос типа под
   `[SerializeReference]` — только с `[MovedFrom]`; переименование сериализованного поля —
   только с `[FormerlySerializedAs]`; перенос поля в базовый класс безопасен при сохранении
   имени.
   *Почему:* Unity матчит сериализацию по именам. Молчаливый rename обнуляет данные в сотнях
   ассетов без ошибки компиляции.

**Плюс сквозной инвариант (тоже HARD): значения — из данных/конфигов, не хардкод.**
Тюнеры и статы живут в ассетах (`StatsConfig`, стат-блок SO), а не в инициализаторах полей и не
магическими числами в коде. Дефолт живёт в `.asset`, а не в инициализаторе (правка инициализатора
НЕ обновляет существующие ассеты).

## Границы со смежными скиллами

Дата-слой стыкуется со многими контурами — режем чётко, на стыке взаимная ссылка, а не спор.

- **combat-sim (эффект живёт на два дома).** data-authoring владеет ОПРЕДЕЛЕНИЕМ: `EffectData`
  SO, `id`, баланс-цифры, состав компонентов через `[SerializeReference]`, loc. combat-sim
  владеет ПОВЕДЕНИЕМ: логика `IRuntimeEffectComponent`, тик, стакинг-механика, реактивы.
  Задача «новый эффект» трогает оба — определение здесь, поведение там.
- **content-hub (будущий скилл).** Редакторное ОКНО `ContentHubWindow` (уже написано на чистом
  UITK) — контур content-hub. data-authoring владеет КОНТРАКТАМИ данных, которые окно правит, и
  инвариантами валидации. Окно не перестраиваем — используем как инструмент авторинга.
- **save-system (будущий скилл).** data-authoring держит форму DTO (id-ссылки, `schemaVersion`,
  `[MovedFrom]`-дисциплина). Плумбинг сейв/загрузки (свой бэкенд на Newtonsoft, Steam Auto-Cloud,
  миграции сейвов) — контур save-system.
- **gdd-scribe.** Там — дизайн-ТЕКСТ (карточки, баланс-намерение, термины). Здесь — ДАННЫЕ,
  реализующие этот дизайн. Числа в ассете ≠ дизайн-решение в ГДД.
- **uitk / gamefeel-vfx.** Визуальные ссылки (иконки, `viewPrefab`, `AnimationArchetypeData`) авторятся
  здесь как поля SO, но их отрисовку/полиш держат UI/визуальные скиллы.

## Как я авторю данные — ГИБРИД (файл + проверка через MCP)

1. **Пишу C#-типы SO напрямую** (`Write`/`Edit`) — контролирую код и его слой (asmdef `Data`,
   зависимость строго вниз).
2. **Новый контент-тип →** регистрирую домен в `ContentDomains`, добавляю в реестр
   (`ContentDatabase`), завожу инвариант в валидации/тестах.
3. **После C#-правок — `read_console`** (Unity MCP): дождаться компиляции, ноль ошибок, только
   потом использовать новые типы.
4. **Ассеты** создаю через `CreateAssetMenu`/Content Hub; id авто-заполняется из имени;
   лок-ключи завожу `ContentLocalization` (create-missing-keys), значения статов — в ассете.
5. **Массовая правка существующих ассетов** — editor-миграцией (образец `Migrations/`), не
   руками по одному и не hand-YAML (Unity перезапишет).
6. **Перед «готово» — `run_tests`** по Content/ContentHub-подмножеству; полный прогон — CI.

## Чеклист сдачи задачи с данными

Прогнать перед тем, как сказать «готово»:

- [ ] Связь слоёв — по id; SO в рантайме не мутируется; POCO/DTO не держат прямых object-ссылок
- [ ] Ноль запрещённых практик: нет Odin Serializer, curve-eval в тике, `Resources.Load`,
      тихого code-fallback при null-конфиге
- [ ] Новый контент-тип: домен в `ContentDomains`, регистрация в реестре, инвариант в валидации
- [ ] id формата `domain.name`, домен под тип, уникален; не менял id вышедшего «в мир» контента
- [ ] Доступ к контенту — только через `IContentDatabase`, не прямой ссылкой / `Resources.Load`
- [ ] Player-facing текст — лок-ключами (RU заполнен, прочие прочерк); текста в SO нет
- [ ] Rename типа/поля — с `[MovedFrom]`/`[FormerlySerializedAs]`
- [ ] Значения — в ассетах/конфигах, не хардкод; дефолт в `.asset`, не в инициализаторе
- [ ] `read_console` чист (компиляция); `run_tests` по Content зелёный
- [ ] Массовые правки ассетов — миграцией, не руками

## Справочные файлы (читать по надобности)

- `references/three-layers-and-ids.md` — три слоя SO→POCO→DTO, `ContentDefinition`, жизненный
  цикл id и `ContentDomains`, реестр `IContentDatabase`/`ContentRegistry`, source-namespace шов
  под моды, `[MovedFrom]`-дисциплина. Читать перед созданием нового контент-типа.
- `references/stats-and-configs.md` — `StatType`/`StatModifier`/`ModifierOp`, авторинг базы через
  `Override`, `StatsConfig`, формула сборки, конфиг-бейк/снапшот/tainted, запрет curve-eval,
  анти-хардкод. Читать перед правкой статов или конфигов.
- `references/localization-and-loading.md` — лок-ключи `{id}.suffix`, политика обязательных
  суффиксов по доменам, `ContentLocalization`-хелпер, RU-only, Addressables только под Loc,
  прямые ссылки на контент, триггеры пересмотра под моды. Читать перед правкой текста/загрузки.
- `references/validation-and-authoring.md` — инварианты валидации, `ContentValidationService`,
  связь с Content Hub, Odin-в-инспекторе, процедура авторинга ассета, editor-миграции. Читать
  перед заведением валидации или массовой правкой ассетов.
