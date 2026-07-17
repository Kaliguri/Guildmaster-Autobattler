# Фасад IAudioService и FMOD за ним

Читать перед правкой реализации звука. Ключевой инвариант: игровая логика знает только фасад,
FMOD живёт за ним.

## `IAudioService` — Core-фасад

`Core/Audio/IAudioService.cs` — вся звуковая поверхность для игрового кода:

- `Play(soundKey)` / `Stop(soundKey)` — по резолвнутому строковому ключу.
- `SetMasterVolume` / `SetMusicVolume` / `SetSfxVolume` (0..1) — шины `bus:/`, `bus:/Music`,
  `bus:/SFX`.
- `SetGlobalParameter(name, value)` — глобальный параметр микса по строковому имени
  (`AudioParameters`), не FMOD-типом. Чтобы Core не знал о движке.

Живёт в **Core** намеренно: `Presentation`-аудио и любой геймплей зовут его без ссылки на
`Game`/FMOD (тот же приём, что `ILocalizationService`).

## Две реализации за фасадом

- **`FmodAudioService`** (`Game/Services`) — боевая. Резолвнутый ключ → `EventReference` через
  `AudioCatalog` → `RuntimeManager.PlayOneShot`. Громкости → `RuntimeManager.GetBus(path).setVolume`.
  Параметры → `RuntimeManager.StudioSystem.setParameterByName`. Регистрируется как `IAudioService`
  (Singleton) в `RootLifetimeScope`.
- **`UnityAudioService`** (`Game/Services`) — заглушка (`Debug.Log`, параметры молча глотает).
  Для Фазы 1 / headless / где FMOD не нужен. Смена реализации не трогает зависимости — весь код
  видит только `IAudioService`.

`Stop` для one-shot'ов — no-op (fire-and-forget, хендл не держим). Loop/музыка получат хранимые
`EventInstance` в отдельной итерации — это точка роста, а не пробел.

## Безопасность пустого проекта — намеренный шов

Всё «нет контента» — тихий no-op, не ошибка:

- Пустой/`IsNull` `EventReference` → `PlayOneShot` не зовётся, звук не играет, FMOD не ругается.
- Невалидная шина (банк не загружен) → `bus.isValid()` false → выходим; `BankLoadException`
  проглатывается. `bus:/SFX` подхватится, когда шины разведены в Studio — код не меняется.
- Неизвестный глобал-параметр → FMOD вернёт `EVENT_NOTFOUND` без исключения, спама нет.

Это даёт «звук приезжает контентом позже» без правок кода. Не превращай эти тихие ветки в
исключения/ассерты — они шов, а не баг.

## Глобальные параметры микса

`Core/Audio/AudioParameters.cs` — строковый контракт с FMOD-проектом: параметр с таким именем
должен существовать в Studio. Сейчас один — `TimeScale` (масштаб времени боя → питч боевой шины,
slowmo вниз / 2x-3x вверх). Пишет его **только** `TimeScaleService` (combat-sim): FMOD за
`Time.timeScale` сам не следит, потому связь идёт этим параметром. Новый глобал-параметр — новая
константа здесь + параметр в Studio.

## Банки и сборка

FMOD-банки — в `Assets/StreamingAssets` (`Master.bank`, `Master.strings.bank`, `SFX.bank` уже
есть). Собираются из FMOD Studio или CLI. **Готча `fmodstudiocl`:** CLI-сборка банков
(`fmodstudiocl.exe -build project.fspro`) — способ пересобрать банки без открытия Studio
(автоматизация/CI). Сам проект и сведение — за Максом/аудио-дизайнером; я работаю с уже
собранными банками и каталогом-маппингом.

## Границы «фасад vs движок»

- **Моё (за фасадом):** `IAudioService` и обе реализации, шины/громкости/параметры, маппинг
  ключ→событие в `AudioCatalog`, проводка событий в `AudioPresenter`.
- **Не моё:** FMOD-Studio-проект, сами события/сэмплы, сведение, разводка шин — Макс/аудио-дизайнер.
  Я не выдумываю звучание; я даю контракт и код, куда оно встанет.

## Антипаттерны

- **FMOD из геймплея.** `RuntimeManager`/`EventReference`/`bus:/` вне `FmodAudioService`+
  `AudioCatalog` — запрещено. Только `IAudioService`.
- **FMOD-типы в сигнатурах фасада/резолвера.** Наружу — только строки/примитивы.
- **Ассерт на пустой контент.** Нет события/банка = тишина, не падение.
- **Прямая запись `TimeScale`-параметра из джуса/UI.** Его пишет `TimeScaleService` (combat-sim).
