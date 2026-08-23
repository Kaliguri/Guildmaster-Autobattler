# Ключи звука, действия и каталог

Читать перед добавлением звука или нового действия. Здесь — адресация и резолв, изолированные от
движка (покрыты юнит-тестом).

## Ключ = `{contentId}.{action}`

Игровой код не знает FMOD-событий — он говорит «сыграй `AudioAction` на этом юните»:

- `contentId` — `id` контента из data-authoring (`Unit.Id`, `domain.name`, напр. `relic.defender`).
- `action` — строковый вид `AudioAction` (`AudioResolver.ActionKey`, напр. `attack`/`hit`/`death`).
- Ключ — `relic.defender.attack`. Звук чужой `id` не выдумывает — берёт из модели.

## `AudioAction` — канон действий (ординалы!)

`Presentation/Audio/AudioAction.cs`: `Attack, Hit, Death, Cast, Ui, Fire, Evade, Shield, Heal,
Apply, Expire, Tick, Stinger`. **Ординалы сериализуются** в `AudioCatalog.asset`
(`_defaults[].Action`) — **только добавлять в конец**, не переставлять и не вставлять в середину,
иначе дефолты в ассете съедут на чужие действия. Строковый вид — `AudioResolver.ActionKey`
(switch; при добавлении действия добавь и кейс, иначе фолбэк `ToString().ToLowerInvariant()`).

## `AudioResolver` — точная → дефолт → тишина

`Presentation/Audio/AudioResolver.cs` — чистая логика над `IAudioCatalog` (поэтому тестируется
фейк-каталогом, без FMOD):

1. Есть `contentId` и в каталоге есть точная запись `{id}.{action}` → вернуть её.
2. Иначе есть дефолт действия (`HasDefault(action)`) → вернуть строку действия (`hit`).
3. Иначе `null` = тишина. На каждый уникальный промах — **один** `Debug.Log` за сессию
   (`_loggedMisses`), не спамим.

Так контент «доозвучивается» инкрементально: сначала дефолты по действию, потом точечные записи
на конкретные мементо.

## `AudioCatalog` — маппинг ключ→FMOD-событие (SO)

`Presentation/Audio/AudioCatalog.cs` (`[CreateAssetMenu]`, реализует `IAudioCatalog`):

- `Entry[] _entries` — точные `Key` (`{id}.{action}`) → `EventReference`. Ключ перекрывает дефолт.
- `ActionDefault[] _defaults` — `AudioAction` → `EventReference`, играет когда точной записи нет.
  Должны покрывать весь `AudioAction`.
- `TryGetEvent(key, out evt)` — точная запись, затем дефолт по действию; `false`, если события нет
  ИЛИ `EventReference.IsNull` (нет банка) → вызывающий (`FmodAudioService`) молчит.
- `EditorSetContents` (`#if UNITY_EDITOR`) — перезапись из FMOD-манифеста инструментом
  `AudioCatalogPopulator` (вики impl «09» §П5). Не для рантайма.

Каталог — единственное место, где `EventReference` встречается вне `FmodAudioService`. Это его
работа (маппинг), поэтому FMOD-тип тут легален. Наружу (резолвер, фасад) он не протекает.

## `AudioPresenter` — проводка событий боя

`Presentation/Audio/AudioPresenter.cs` (POCO `IStartable`/`IDisposable`, регистрируется
EntryPoint в `CombatLifetimeScope`). Подписан на C#-события `CombatSimulation` **напрямую** (не
MessagePipe — он в `Presentation.Audio`, видит `Combat`) и на `AbilitySystem.OnAbilityCast`.
Маппинг событие → действие:

| Событие sim | Действие | Нюанс |
|---|---|---|
| `OnAttackStarted` | `Attack` (на источнике) | |
| `OnDamageDealt` | `Shield` (если поглощён щит) + `Hit` (на цели) | + `feel.kill`/`Stinger` при добивании |
| `OnHealed` | `Heal` | |
| `OnAttackEvaded` | `Evade` | |
| `OnProjectileSpawned` | `Fire` (на источнике) | |
| `OnUnitDied` | `Death` | |
| `OnAbilityCast` | `Cast` (на касторе) | |
| `OnBattleEnded` | `battle.victory`/`battle.defeat` + `Stinger` | глазами локального игрока (`ILocalPlayer.Team`); ничья = поражение |

Ещё не озвучены (нужны хуки/id, отдельный заход): apply/expire конкретного эффекта, DoT/HoT-тик,
UI (пауза/скорость/расстановка), стингер старта боя, feel-слои (heavy_hit/death_shatter). Это
список роста, зафиксированный в самом презентере.

## Спец-ключи (не `{id}.{action}`)

Событийные стингеры адресуются фиксированными ключами, не через contentId:
`feel.kill` (Stinger), `battle.victory`/`battle.defeat` (Stinger). Их кладём в каталог как точные
записи. Это нормально — не весь звук завязан на контент-id.

## Антипаттерны

- **Перестановка `AudioAction`.** Только в конец — иначе порча `.asset`.
- **`EventReference` в резолвере/фасаде/геймплее.** Только в каталоге и `FmodAudioService`.
- **Выдуманный `contentId`.** Берётся из `Unit.Id` (data-authoring); звук id не сочиняет.
- **Спам-лог на тишину.** Один лог на промах за сессию (уже реализовано) — не убирать в цикл.
- **Наполнение каталога hand-YAML.** `EventReference` = FMOD-GUID; привязка in-editor
  (инспектор/`AudioCatalogPopulator`).
