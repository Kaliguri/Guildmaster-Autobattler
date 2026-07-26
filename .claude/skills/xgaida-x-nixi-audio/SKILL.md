---
name: xgaida-x-nixi-audio
description: >-
  Рабочий контур звука (audio / SFX / музыка) Guildmaster — весь аудио-слой за
  фасадом IAudioService плюс генеративный пайплайн scripts/audio: карта звука
  (audio_map.py — ключ→категория→сэмплы), нормализация громкости, генерация
  populate.js, сборка FMOD-банков, наполнение AudioCatalog. Core-фасад и две
  реализации (FmodAudioService на FMOD с хранимыми EventInstance для лупов,
  UnityAudioService-заглушка), боевой аудио-презентер (AudioPresenter),
  звук забега вне боя (RunAudioPresenter в root-скоупе), звук интерфейса одним
  слушателем на корне панели (UiSoundSystem), резолвер ключей {contentId}.{action}
  (AudioResolver), каталог ключ→FMOD-событие (AudioCatalog + EventReference),
  канон действий (AudioAction), глобальные параметры микса (AudioParameters,
  TimeScale-питч), шины bus:/SFX/{Combat,UI,Ambient,Stingers} и bus:/Music,
  громкости через SettingsService, банки в StreamingAssets, музыка и амбиент.
  Используй ВСЕГДА, когда задача касается звука: audio, звук, sound, SFX, музыка,
  амбиент, луп, стингер, микс, шина/bus, громкость/volume, LUFS, нормализация,
  FMOD, FMOD Studio, банк/bank, populate, EventReference, IAudioService,
  AudioPresenter, RunAudioPresenter, UiSoundSystem, AudioCatalog, AudioResolver,
  AudioAction, AudioParameters, ключ звука, {id}.{action}, озвучка удара/каста/
  смерти/клика/узла карты, анти-каша голосов, voice stealing, подбор сэмплов,
  CLAP, Freesound, ElevenLabs, Stable Audio, или когда правишь что-либо под
  scripts/audio, FMOD Project, Assets/_Project/Scripts/Core/Audio,
  Assets/_Project/Scripts/Presentation/Audio, Assets/_Project/Scripts/UI/UiSoundSystem.cs,
  аудио-сервисы в Game/Services (FmodAudioService, RunAudioPresenter,
  UnityAudioService, аудио-часть SettingsService) и банки в Assets/StreamingAssets.
  Срабатывай, даже если слова «audio» нет, но по сути правится озвучка/звуковой
  мост/микс/подбор сэмплов. НЕ применять к: боевому времени (TimeScaleService —
  combat-sim; звук лишь принимает от него параметр TimeScale), джусу/VFX/тряске
  (gamefeel-vfx — но feel-ЗВУКИ живут здесь), поведению эффектов и sim-логике
  (combat-sim), ОПРЕДЕЛЕНИЮ игрового контента и id (UnitData/EffectData,
  ContentDomains — data-authoring; звук лишь резолвит по чужому id), самому
  звучанию и финальному сведению на слух (Макс).
  Инженерную тех-доку об аудио-слое (docs/wiki/tech) ведёт tech-scribe.
---

# Audio — рабочий контур Guildmaster

Этот скилл — процедура, а не справка. Он превращает правила звукового слоя в чеклист,
который прогоняется на КАЖДОЙ аудио-задаче. Цель — чтобы весь звук ходил через один
фасад (`IAudioService`), игровая логика не знала о FMOD, новый звук ложился в готовую
цепочку «событие → ключ → каталог → FMOD-событие», а FMOD-проект собирался из карты, а не
руками.

**Роль на этом слое:** я держу КОНТРАКТ звука (фасад, ключи, резолв, каталог, проводку
событий), КОД и ПАЙПЛАЙН (карта → нормализация → FMOD → каталог), включая числа микса.
Чего я не могу — услышать: «сочно или дохло», «этот удар лучше того» решает Макс. Всё, что
проверяется числами и моделью, проверяю сама (см. §Инструменты).

## Прежде всего: карта аудио-слоя

Слой построен и живой. Ничего не изобретай — читай, продолжай, встраивайся.

### Пайплайн (источник правды — карта, не FMOD-проект)

| Что | Где |
|---|---|
| **Карта звука: ключ → категория → сэмплы + описания** | `scripts/audio/audio_map.py` |
| Нормализация громкости + манифест | `scripts/audio/build_source_audio.py` |
| Генератор скрипта заливки FMOD | `scripts/audio/build_populate.py` |
| Сгенерированный скрипт заливки | `FMOD Project/Tooling/populate.js` |
| Манифест (вход каталога и тестов) | `FMOD Project/Scripts/manifest.json` |
| Нормализованные исходники (артефакт, в .gitignore) | `FMOD Project/SourceAudio/` |
| Копии, которые версионируются (их кладёт FMOD при импорте) | `FMOD Project/Assets/` |
| Сырьё музыки | `FMOD Project/MusicSource/` |
| Генератор reference-доки | `scripts/audio/gen_audio_reference.py` |

### Код

| Что | Где |
|---|---|
| Фасад звука (Core, без ссылки на движок) | `Assets/_Project/Scripts/Core/Audio/IAudioService.cs` |
| Имена глобальных FMOD-параметров | `Assets/_Project/Scripts/Core/Audio/AudioParameters.cs` |
| FMOD-реализация (one-shot + хранимые `EventInstance` для лупов) | `Assets/_Project/Scripts/Game/Services/FmodAudioService.cs` |
| Заглушка фасада (headless) | `Assets/_Project/Scripts/Game/Services/UnityAudioService.cs` |
| Громкости шин из настроек | `Assets/_Project/Scripts/Game/Services/SettingsService.cs` |
| Боевой аудио-презентер | `Assets/_Project/Scripts/Presentation/Audio/AudioPresenter.cs` |
| **Звук забега вне боя + музыка (root-скоуп)** | `Assets/_Project/Scripts/Game/Services/RunAudioPresenter.cs` |
| **Звук интерфейса: один слушатель на корне панели** | `Assets/_Project/Scripts/UI/UiSoundSystem.cs` |
| Feel-звуки (килл, тяжёлый удар, финишер) | `Assets/_Project/Scripts/Game/Services/CombatFeelDirector.cs` |
| Резолвер `{contentId}.{action}` | `Assets/_Project/Scripts/Presentation/Audio/AudioResolver.cs` |
| Каталог ключ→FMOD-событие | `.../Audio/AudioCatalog.cs`, `IAudioCatalog.cs` |
| Канон действий (ординалы сериализуются) | `.../Audio/AudioAction.cs` |
| Наполнение каталога из манифеста | `Assets/_Project/Scripts/EditorTools/Audio/AudioCatalogPopulator.cs` |
| Тесты покрытия (код ↔ каталог ↔ манифест) | `Assets/_Project/Tests/EditMode/Audio/AudioCoverageTests.cs` |
| Банки | `Assets/StreamingAssets/{Master,Master.strings,SFX,Music}.bank` |

**Слои (asmdef):** фасад и `AudioParameters` — в **Core** (чтобы нижние слои звали звук без
ссылки на композит-рут). Резолвер/каталог/действия — `Presentation.Audio`. FMOD-реализация и
`RunAudioPresenter` — `Game`. `UiSoundSystem` — `UI`. Тестовая сборка FMOD-типов НЕ видит:
каталог отвечает на её вопросы сам (`HasSound`, `EntryKeys`, `KeysWithoutEvent`).

## Пять правил, нарушение которых = переделка (HARD)

1. **Весь звук — через `IAudioService`; игровая логика FMOD НЕ трогает.** `FMODUnity`,
   `RuntimeManager`, `EventReference`, шины `bus:/…` живут ТОЛЬКО в `FmodAudioService` и
   `AudioCatalog`.
   *Почему:* фасад — единственная причина, по которой можно гонять headless-тесты, переживать
   пустой проект без банков и когда-нибудь сменить движок звука не трогая геймплей.

2. **Звук адресуется ключом `{contentId}.{action}`, не FMOD-типами.** Игровой код говорит
   «сыграй `Hit` на этом юните»; ключ строит `AudioResolver`; в событие его превращает только
   `AudioCatalog`.
   *Почему:* резолв (точная запись → дефолт действия → тишина) остаётся чистой логикой, покрытой
   юнит-тестом, а привязка к движку живёт в одном SO.

3. **`AudioAction` — только ДОБАВЛЯТЬ в конец.** Ординалы сериализуются в `AudioCatalog.asset`.
   *Почему:* переставишь — дефолты в ассете молча съедут на чужие действия. Компилятор промолчит.

4. **FMOD-проект собирается из карты, а не правится руками.** Новый звук = запись в
   `scripts/audio/audio_map.py` + прогон пайплайна. Ручные события и ручной роутинг в FMOD Studio
   `populate.js` снесёт на следующем прогоне (он чистит всё под `event:/SFX`, `event:/Stingers`,
   `event:/Music` и пересоздаёт шины).
   *Почему:* до этого FMOD-проект и код расходились молча — события были, а играть их было некому,
   и наоборот. Один источник правды делает рассинхрон невозможным, а не «маловероятным».
   *Исключение:* крутить микс живьём через **Live Update** можно и нужно — но удачные числа
   переносятся в `CATEGORIES`/`BUS_TREE` карты, иначе следующий прогон их затрёт.

5. **Числа громкости живут в карте, а обработка — в FMOD.** Нормализация сэмплов
   (−23 dB RMS активной части, потолок −1 dBTP), категорийные offset'ы, рандомизация и
   voice-макросы задаются в `audio_map.py`; эффекты, снапшоты и sidechain — в FMOD Studio.
   Свой C#-инструмент сведения НЕ строим (решение [[audio-subbuses]]).
   *Почему:* громкость должна быть воспроизводимой (прогнал — получил то же), а обработка звука —
   это микшер, дублировать его кодом бессмысленно.

## Цепочка озвучки

```
СОБЫТИЕ БОЯ (sim/abilities/effects)          OnDamageDealt, OnEffectApplied, OnUnitDied…
  → AudioPresenter                            событие → (contentId, AudioAction)
СОБЫТИЕ ЗАБЕГА (MessagePipe, фаза боя)       OpenRewardRequest, PhaseChanged, ScreenFade…
  → RunAudioPresenter                         + музыка: одна дорожка за раз
ЖЕСТ В ИНТЕРФЕЙСЕ (UITK, корень панели)      ClickEvent, PointerEnter, ChangeEvent, Wheel
  → UiSoundSystem                             разбор по типу элемента и USS-классу
      ↓
  AudioResolver.Resolve(contentId, action)    {id}.{action} → точная запись
                                                ↘ нет → дефолт действия ("hit")
                                                  ↘ нет → null (тишина + один лог за сессию)
  → IAudioService.Play(key)                   фасад
  → FmodAudioService                          one-shot → PlayOneShot; луп → хранимый EventInstance
```

**Безопасность пустого контента — это шов, не баг.** Пустой каталог, пустой `EventReference`,
невалидная шина (банк не загружен) — везде тихий no-op.

## Пайплайн: как добавить или поменять звук

```bash
# 1. правишь карту: ключ, категорию, файлы, описание для CLAP
#    scripts/audio/audio_map.py

# 2. нормализация + манифест
python scripts/audio/build_source_audio.py

# 3. генерация скрипта заливки и прогон FMOD (две команды — build отдельно, это готча FMOD)
python scripts/audio/build_populate.py
"C:/Program Files/FMOD SoundSystem/FMOD Studio 2.03.14/fmodstudiocl.exe" -script "FMOD Project/Tooling/populate.js" "FMOD Project/Guildmaster Autobattler Game.fspro"
"C:/Program Files/FMOD SoundSystem/FMOD Studio 2.03.14/fmodstudiocl.exe" -build -ignore-warnings -export-guids "FMOD Project/Guildmaster Autobattler Game.fspro"
cp "FMOD Project/Build/Desktop/"*.bank Assets/StreamingAssets/

# 4. каталог (в Unity): меню Alebardium/Audio/Populate Catalog from Manifest
# 5. проверки
python scripts/audio/audit_samples.py
python scripts/audio/gen_audio_reference.py     # обновить reference-доку
#    + EditMode-прогон: AudioCoverageTests должны быть зелёными
```

## Инструменты отбора (уши, которых у меня нет)

| Инструмент | Что делает | Оговорка |
|---|---|---|
| `audit_samples.py` | клиппинг, DC-offset, тишина в начале, обрыв хвоста, длительность, частота | числа, не вкус |
| `clap_pick.py --find` | подбор кандидатов по описанию из всех паков | **не верить на сэмплах < 0.6 с** |
| `clap_pick.py --verify` | сверка «сэмпл ↔ смысл ключа» (описания в `DESCRIPTIONS` карты) | то же |
| `freesound_fetch.py` | CC0-кандидаты с Freesound (ключ в `.env`) | превью, не финальные файлы |
| `sfx_generate.py` | генерация через ElevenLabs (ключ в `.env`) | платно |
| `sfx_generate_local.py` | Stable Audio Open локально (`HF_TOKEN` в `.env`, venv `.venv-gen`) | на CPU минуты на семпл, на CUDA — секунда |

**Два venv, и это не вкусовщина.** `scripts/audio/.venv` — CLAP и поиск; `scripts/audio/.venv-gen` —
локальная генерация. `stable-audio-tools` пинит `laion-clap==1.1.4`, чей чекпоинт не сходится с
весами, которые качает CLAP: в общем окружении они ломали друг друга по очереди (плюс numpy 2.x
против pandas и `torch.load` 2.6+ против весов CLAP). Оба в `.gitignore`, команды установки — в
шапках `clap_pick.py` и `sfx_generate_local.py`.

## Стыки со смежными скиллами

- **combat-sim** владеет боевым временем: `TimeScaleService` пишет `AudioParameters.TimeScale`,
  а кривая питча боевой шины — моя. Sim про звук не знает; `EffectSystem.OnEffectApplied/
  OnEffectEnded` заведены как уведомления, детерминизм ими не задет.
- **gamefeel-vfx** — джус и звук слушают одно событие НЕЗАВИСИМО, но feel-ЗВУКИ зовутся из
  `CombatFeelDirector`: там уже посчитаны пороги и кулдауны, и звук обязан идти под теми же
  воротами, что slowmo и тряска.
- **data-authoring** владеет `id` контента; звук лишь резолвит по чужому `id`. Новый озвученный
  контент = новый `id` там + запись в карте здесь.
- **uitk** владеет экранами; `UiSoundSystem` слушает их корень и в разметку не лезет. Экрану,
  которому нужен СВОЙ звук, его зовёт владелец флоу (`MenuRouter`, `ShopController`).
- **tech-scribe** ведёт `docs/wiki/tech`: reference по звуку ГЕНЕРИРУЕТСЯ (`gen_audio_reference.py`),
  решения пишутся в `tech-changelog`.

## Чеклист сдачи аудио-задачи

- [ ] Звук идёт только через `IAudioService`; FMOD-типы не утекли в геймплей и в тесты
- [ ] Новый звук добавлен В КАРТУ и прогнан пайплайном, а не заведён руками в FMOD Studio
- [ ] Новое `AudioAction` добавлено В КОНЕЦ (ординалы в `.asset` не съехали)
- [ ] Пустой каталог/EventReference/шина безопасны (тихий no-op)
- [ ] `contentId` берётся из данных (data-authoring), звук чужой id не выдумывает
- [ ] Прогнан `audit_samples.py`: нет `clip` и `late` (их слышно)
- [ ] `AudioCoverageTests` зелёные: код не зовёт ключей, которых нет; каталог не разошёлся с манифестом
- [ ] Банки пересобраны и лежат в `StreamingAssets`; каталог наполнен из меню, не руками
- [ ] Reference-дока перегенерирована; значимое решение — в `tech-changelog`
- [ ] `read_console` чист; что слушать — сказано Максу списком

## Справочные файлы (читать по надобности)

- `references/facade-and-fmod.md` — `IAudioService`, реализации, шины и громкости, лупы через
  `EventInstance`, глобальные параметры, банки, готчи `fmodstudiocl`, границы «фасад vs движок».
- `references/keys-and-catalog.md` — ключи `{contentId}.{action}`, `AudioAction`, резолв, каталог,
  наполнение, подписки презентеров.
- `references/pipeline-and-mix.md` — карта звука, нормализация и её числа, категории, шины,
  анти-каша (max instances / stealing / cooldown / priority), `TimeScale`-кривая, готчи FMOD
  scripting API.
