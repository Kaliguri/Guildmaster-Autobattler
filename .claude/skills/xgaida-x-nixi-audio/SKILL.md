---
name: xgaida-x-nixi-audio
description: >-
  Рабочий контур звука (audio / SFX / музыка) Guildmaster — весь аудио-слой за
  фасадом IAudioService: Core-фасад и две реализации (FmodAudioService на FMOD,
  UnityAudioService-заглушка), боевой аудио-презентер (AudioPresenter слушает
  события боя), резолвер ключей {contentId}.{action} (AudioResolver), каталог
  ключ→FMOD-событие (AudioCatalog + EventReference), канон действий (AudioAction),
  глобальные параметры микса (AudioParameters, напр. TimeScale-питч), шины и
  громкости (bus:/Music, bus:/SFX через SettingsService), FMOD-банки в
  StreamingAssets. Используй ВСЕГДА, когда задача касается звука: audio, звук,
  sound, SFX, музыка, стингер, микс, шина/bus, громкость/volume, FMOD, FMOD
  Studio, банк/bank, EventReference, IAudioService, AudioPresenter, AudioCatalog,
  AudioResolver, AudioAction, AudioParameters, ключ звука, {id}.{action}, озвучка
  удара/каста/смерти, глобальный параметр микса, или когда правишь что-либо под
  Assets/_Project/Scripts/Core/Audio, Assets/_Project/Scripts/Presentation/Audio,
  аудио-сервисы в Game/Services (FmodAudioService, UnityAudioService, аудио-часть
  SettingsService) и банки в Assets/StreamingAssets. Срабатывай, даже если слова
  «audio» нет, но по сути правится озвучка/звуковой мост/микс. НЕ применять к:
  боевому времени (TimeScaleService — combat-sim; звук лишь принимает от него
  параметр TimeScale), джусу/VFX/тряске (gamefeel-vfx), поведению эффектов и
  sim-логике (combat-sim), ОПРЕДЕЛЕНИЮ игрового контента и id (UnitData/EffectData,
  ContentDomains — data-authoring; звук лишь резолвит по чужому id), самому
  FMOD-Studio-проекту и звуковому контенту/сведению (аудио-дизайнер/Макс).
  Инженерную тех-доку об аудио-слое (docs/wiki/tech) ведёт tech-scribe.
---

# Audio — рабочий контур Guildmaster

Этот скилл — процедура, а не справка. Он превращает правила звукового слоя в чеклист,
который прогоняется на КАЖДОЙ аудио-задаче. Цель — чтобы весь звук ходил через один
фасад (`IAudioService`), игровая логика не знала о FMOD, а новый звук ложился в готовую
цепочку «событие → ключ → каталог → FMOD-событие», а не рядом с ней.

**Роль на этом слое:** я держу КОНТРАКТ звука (фасад, ключи, резолв, каталог, проводку
событий) и код. Само звучание — какие сэмплы, сведе́ние, FMOD-Studio-проект, банки — за
Максом/аудио-дизайнером. Не-звуково верю резолверу и тестам (`AudioResolver` покрыт
юнит-тестом); «как звучит» — приёмка Макса.

## Прежде всего: карта аудио-слоя

Слой уже построен и живёт за фасадом. Ничего не изобретай — читай, продолжай, встраивайся.

| Что | Где |
|---|---|
| Фасад звуковой подсистемы (Core, без ссылки на движок) | `Assets/_Project/Scripts/Core/Audio/IAudioService.cs` |
| Имена глобальных FMOD-параметров (строковый контракт со Studio) | `Assets/_Project/Scripts/Core/Audio/AudioParameters.cs` |
| FMOD-реализация фасада (PlayOneShot, шины, setParameter) | `Assets/_Project/Scripts/Game/Services/FmodAudioService.cs` |
| Заглушка фасада (Debug.Log, Фаза 1 / headless) | `Assets/_Project/Scripts/Game/Services/UnityAudioService.cs` |
| Громкости шин из настроек → фасад | `Assets/_Project/Scripts/Game/Services/SettingsService.cs` |
| Боевой аудио-презентер (подписан на события боя) | `Assets/_Project/Scripts/Presentation/Audio/AudioPresenter.cs` |
| Резолвер ключа `{contentId}.{action}` → точная/дефолт/тишина | `Assets/_Project/Scripts/Presentation/Audio/AudioResolver.cs` |
| Каталог ключ→FMOD-событие (SO, EventReference) | `Assets/_Project/Scripts/Presentation/Audio/AudioCatalog.cs`, `IAudioCatalog.cs` |
| Канон звуковых действий (enum, ординалы сериализуются) | `Assets/_Project/Scripts/Presentation/Audio/AudioAction.cs` |
| FMOD-банки (собираются из Studio/CLI) | `Assets/StreamingAssets/Master.bank`, `Master.strings.bank`, `SFX.bank` |
| Регистрация DI (FmodAudioService как IAudioService, каталог) | `Assets/_Project/Scripts/Game/RootLifetimeScope.cs`, `CombatLifetimeScope.cs` |

**Слои (asmdef):** фасад `IAudioService` и `AudioParameters` живут в **Core** — чтобы
нижние слои (`Presentation`-аудио) звали звук без ссылки на композит-рут `Game` (тот же
приём, что `ILocalizationService`). Резолвер/каталог/действия — в `Presentation.Audio`.
FMOD-реализация — в `Game` (там есть ссылка на `FMODUnity`). Регистрация — в `Game` через
VContainer.

## Три правила, нарушение которых = переделка (HARD)

1. **Весь звук — через `IAudioService`; игровая логика FMOD НЕ трогает.** `FMODUnity`,
   `RuntimeManager`, `EventReference`, шины `bus:/…` — живут ТОЛЬКО в `FmodAudioService` и
   `AudioCatalog` (каталог держит `EventReference`, потому что это его работа — маппинг). Ни
   один боевой/UI/геймплейный класс не зовёт FMOD напрямую — только `IAudioService.Play/Stop/
   Set*Volume/SetGlobalParameter`.
   *Почему:* фасад — единственная причина, по которой можно гонять headless-тесты (заглушка
   `UnityAudioService`), переживать пустой проект без банков и в будущем сменить движок звука
   не трогая геймплей. Прямой вызов FMOD из логики убивает всё это разом.

2. **Звук адресуется строковым ключом `{contentId}.{action}`, не FMOD-типами.** Игровой код
   говорит «сыграй `Hit` на этом юните» (`AudioAction` + `contentId` из `Unit.Id`); строковый
   ключ строит `AudioResolver`; в FMOD-событие его превращает только `AudioCatalog`. Core и
   резолвер про `EventReference` не знают.
   *Почему:* так резолв (точная запись → дефолт действия → тишина) — чистая логика над
   `IAudioCatalog`, покрытая юнит-тестом с фейк-каталогом; а привязка к движку изолирована в
   одном SO.

3. **`AudioAction` — только ДОБАВЛЯТЬ в конец.** Ординалы enum сериализуются в
   `AudioCatalog.asset` (`_defaults[].Action`). Переставишь/вставишь в середину — дефолты в
   ассете съедут на чужие действия.
   *Почему:* это молчаливая порча данных — компилятор промолчит, а звук поедет. Новое действие
   — в конец списка, и всё.

## Цепочка озвучки (как звук попадает на колонки)

```
боевое событие (sim/abilities)                 // OnDamageDealt, OnAbilityCast, OnUnitDied…
  → AudioPresenter                             // маппит событие → (contentId, AudioAction)
  → AudioResolver.Resolve(contentId, action)   // {id}.{action} → точная запись
                                               //   ↘ нет → дефолт действия ("hit")
                                               //     ↘ нет → null (тишина, лог раз за сессию)
  → IAudioService.Play(key)                    // фасад
  → FmodAudioService                           // key → EventReference через AudioCatalog
  → RuntimeManager.PlayOneShot(evt)            // FMOD; пустой EventReference → тихо, без ошибок
```

`AudioPresenter` (POCO `IStartable`) подписан на C#-события `CombatSimulation` **напрямую**
(как `CombatPresenter`) — не через MessagePipe. Это осознанно: он в `Presentation.Audio` и
видит `Combat`. Точечные звуки реликвий/эффектов (`relic.cryomancer.attack`) резолвер
подхватывает автоматически — достаточно выстрелить нужным `AudioAction` на нужном юните.

**Безопасность пустого контента — это шов, не баг.** Пустой каталог, пустой `EventReference`,
невалидная шина (банк не загружен) — везде тихий no-op, игра не падает. Звук «приезжает»
контентом позже, код при этом не меняется.

## Стыки со смежными скиллами

- **combat-sim** владеет боевым временем. `TimeScaleService` пишет `AudioParameters.TimeScale`
  (slowmo-питч боевой шины) через `IAudioService.SetGlobalParameter` — **вызов** геймплейный
  (combat-sim), а **имя параметра и смысл микса** — мои. FMOD за `Time.timeScale` сам не следит,
  потому связь идёт этим параметром. `AudioPresenter` слушает боевые события sim — sim про звук
  не знает.
- **gamefeel-vfx** — джус и звук подписываются на одно событие НЕЗАВИСИМО: на добивающий удар
  `AudioPresenter` даёт kill-стингер (`feel.kill`), `CombatFeelDirector` — slowmo+shake. Разные
  слои, одно событие, друг через друга не ходят.
- **data-authoring** владеет `id` контента (`domain.name`, `ContentDomains`). Звук лишь
  РЕЗОЛВИТ по чужому `id` (`Unit.Id` → `contentId`). `AudioCatalog` (маппинг ключ→FMOD-событие) —
  мой, это не игровой контент, а звуковой конфиг. Новый озвучиваемый контент = новый `id` в
  data-authoring + запись/дефолт в каталоге здесь.
- **Настройки/UI:** `SettingsService` гонит громкости в шины (`SetMasterVolume/Music/Sfx` →
  `bus:/`, `bus:/Music`, `bus:/SFX`). UI-звук — `AudioAction.Ui`.

## Как я авторю аудио-код — ГИБРИД (файл + editor)

1. **Пишу C#-файлы напрямую** (`Write`/`Edit`) — фасад, резолвер, презентер, действия.
2. **`AudioCatalog` наполняется в редакторе:** `EventReference` — это FMOD-GUID, руками в YAML
   не пишем (как префаб-ref). Привязка ключ→событие — in-editor через инспектор/editor-инструмент
   (`AudioCatalogPopulator`, вики impl «09» §П5). Ассет каталога:
   `Assets/_Project/ScriptableObjects/Audio/AudioCatalog`.
3. **Банки** собираются из FMOD Studio (или CLI `fmodstudiocl`) и кладутся в `StreamingAssets`
   (`Master.bank`/`Master.strings.bank`/`SFX.bank` уже там). Сам FMOD-проект/сведение — Макс.
4. **После C#-правок — `read_console`** (Unity MCP): компиляция, ноль ошибок.
5. **Звуковая приёмка — за Максом.** Логику резолва проверяю тестом; «как звучит» — на слух Макса.

## Чеклист сдачи аудио-задачи

- [ ] Звук идёт только через `IAudioService`; FMOD/`EventReference`/`RuntimeManager` не утекли в геймплей
- [ ] Адресация строковым ключом `{contentId}.{action}`; резолв точная→дефолт→тишина цел
- [ ] Новое `AudioAction` добавлено В КОНЕЦ (ординалы в `.asset` не съехали)
- [ ] Пустой каталог/EventReference/шина безопасны (тихий no-op, игра не падает)
- [ ] `contentId` берётся из `Unit.Id` (data-authoring), звук чужой id не выдумывает
- [ ] Глобал-параметры — по имени из `AudioParameters`; `TimeScale` пишет только `TimeScaleService`
- [ ] На нетривиальный резолв — EditMode-тест с фейк-каталогом (тесты под игру)
- [ ] `read_console` чист; звуковую приёмку (если есть сэмплы) показал Максу
- [ ] Новые банки — в `StreamingAssets`; каталог наполнен in-editor, не hand-YAML

## Справочные файлы (читать по надобности)

- `references/facade-and-fmod.md` — `IAudioService`, `FmodAudioService`/`UnityAudioService`,
  шины и громкости, глобальные параметры, банки/StreamingAssets, готча `fmodstudiocl`, границы
  «фасад vs движок». Читать перед правкой реализации звука.
- `references/keys-and-catalog.md` — ключи `{contentId}.{action}`, `AudioAction` (ординалы),
  `AudioResolver` (точная→дефолт→тишина), `AudioCatalog` + `EventReference`, наполнение каталога,
  подписка `AudioPresenter`. Читать перед добавлением звука/действия.
