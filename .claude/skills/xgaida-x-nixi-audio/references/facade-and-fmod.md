# Фасад IAudioService и FMOD за ним

Читать перед правкой реализации звука. Ключевой инвариант: игровая логика знает только фасад,
FMOD живёт за ним.

## `IAudioService` — Core-фасад

`Core/Audio/IAudioService.cs` — вся звуковая поверхность для игрового кода:

- `Play(soundKey)` / `Stop(soundKey)` / `StopAll()` — по резолвнутому строковому ключу.
- `SetMasterVolume` / `SetMusicVolume` / `SetSfxVolume` (0..1) — шины `bus:/`, `bus:/Music`,
  `bus:/SFX`.
- `SetGlobalParameter(name, value)` — глобальный параметр микса по строковому имени
  (`AudioParameters`), не FMOD-типом. Чтобы Core не знал о движке.

Живёт в **Core** намеренно: `Presentation`-аудио и любой геймплей зовут его без ссылки на
`Game`/FMOD (тот же приём, что `ILocalizationService`).

## Две реализации за фасадом

- **`FmodAudioService`** (`Game/Services`) — боевая. Резолвнутый ключ → `EventReference` через
  `AudioCatalog` → `PlayOneShot` для one-shot, `CreateInstance`+`start` для лупов.
  Громкости → `RuntimeManager.GetBus(path).setVolume`.
  Параметры → `RuntimeManager.StudioSystem.setParameterByName`. Регистрируется как `IAudioService`
  (Singleton) в `RootLifetimeScope`.
- **`UnityAudioService`** (`Game/Services`) — заглушка (`Debug.Log`, параметры молча глотает).
  Для Фазы 1 / headless / где FMOD не нужен. Смена реализации не трогает зависимости — весь код
  видит только `IAudioService`.

## Лупы: музыка и амбиент

`Stop` для one-shot'ов — no-op (fire-and-forget, хендл не нужен). А вот длящиеся события
(музыка, амбиент — у них в Studio снят флаг one-shot) получают хранимый `EventInstance`:

- `Play` спрашивает у события `EventDescription.isOneshot()`. One-shot → `PlayOneShot` и забыли;
  не one-shot → создаём инстанс, стартуем и кладём в словарь по ключу. Повторный `Play` того же
  ключа — no-op, а не второй слой поверх играющего.
- `Stop(key)` — `stop(ALLOWFADEOUT)` + `release` (фейд события отрабатывает, обрыв не слышен).
- `StopAll()` — гасит все петли разом: смена сцены, выход в меню, перезапуск боя (dev-R).

Без этого музыки не могло быть в принципе: `Stop` был заглушкой, и остановить петлю было нечем.

## Шины

`bus:/SFX/{Combat,UI,Ambient,Stingers}` и `bus:/Music` создаёт `populate.js` из `BUS_TREE` карты.
Слайдеры настроек пишут в `bus:/`, `bus:/Music`, `bus:/SFX` — под-шины обязаны висеть именно под
ними. Если шины нет, `SetBusVolume` тихо выходит: это спасает пустой проект, но и прячет ошибку —
ровно так слайдеры «Музыка» и «Звук» полгода не работали, потому что писали в несуществующие шины.

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

FMOD-банки — в `Assets/StreamingAssets`: `Master.bank`, `Master.strings.bank`, `SFX.bank` и
`Music.bank` (музыка отдельным банком, треки идут стримом). Собираются CLI:
`fmodstudiocl.exe -build -ignore-warnings -export-guids project.fspro`. **Готча:** заливка и
сборка — ДВЕ отдельные команды, `-script` и `-build` вместе не работают.

## Границы «фасад vs движок»

- **Моё:** `IAudioService` и обе реализации, шины/громкости/параметры, маппинг ключ→событие,
  проводка событий во всех трёх презентерах, И САМ FMOD-ПРОЕКТ — он собирается из карты
  `scripts/audio/audio_map.py` скриптом `populate.js`, а не правится руками.
- **Не моё:** как это ЗВУЧИТ. Выбор «этот удар лучше того», финальное сведение на слух, вкусовые
  решения — Макс. Я даю числа, воспроизводимость и список «что послушать».

## Антипаттерны

- **FMOD из геймплея.** `RuntimeManager`/`EventReference`/`bus:/` вне `FmodAudioService`+
  `AudioCatalog` — запрещено. Только `IAudioService`.
- **FMOD-типы в сигнатурах фасада/резолвера.** Наружу — только строки/примитивы.
- **Ассерт на пустой контент.** Нет события/банка = тишина, не падение.
- **Прямая запись `TimeScale`-параметра из джуса/UI.** Его пишет `TimeScaleService` (combat-sim).
