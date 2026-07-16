# Экраны, MVVM и проводка данных

Как связать UXML-экран с данными. Наш паттерн — MVVM + runtime binding UI Toolkit.
Эталоны в коде: `SettingsViewModel` + `SettingsScreen.uxml`, `LoadoutViewModel` +
`LoadoutHubView`, `MenuRouter`/`IMenuRouter`.

## Слои

```
Model (сервис/данные)  ──  ViewModel (POCO)  ──  View (грузит UXML, проводит контролы)
   ISettingsService          SettingsViewModel        SettingsScreen.uxml + View-класс
```

- **Model** — сервис за интерфейсом (`ISettingsService`), ничего не знает про UI.
- **ViewModel** — POCO, создаётся через DI, **не MonoBehaviour**. Держит состояние
  редактирования, отдаёт значения, поднимает `event Changed`. Тестируется без сцены.
- **View** — грузит UXML, достаёт контролы, подписывает VM на события и наоборот.

## ViewModel — канон

Эталон `SettingsViewModel` (`Assets/_Project/Scripts/UI/SettingsViewModel.cs`):

```csharp
public sealed class SettingsViewModel
{
    private readonly ISettingsService _settings;
    private AudioVolumeSettings _baseline;              // снапшот для Cancel

    public SettingsViewModel(ISettingsService settings) => _settings = settings;  // DI

    public float Master => _settings.Audio.Master;      // read-модель для View
    public event Action Changed                          // View подписывается на обновление
    {
        add => _settings.Changed += value;
        remove => _settings.Changed -= value;
    }

    public void BeginEdit() => _baseline = _settings.Audio;   // точка отката при открытии
    public void SetMaster(float v) => _settings.SetMasterVolume(v);  // применяется живьём
    public void Save()   { _settings.Save(); _baseline = _settings.Audio; }
    public void Cancel() { _settings.SetMasterVolume(_baseline.Master); /* ... */ }
}
```

Ключевые черты, которые повторяй:
- Конструктор принимает зависимости (DI), никаких статических синглтонов.
- Baseline-снапшот, если экран умеет Cancel/откат.
- `event Changed` — единая точка, на которую View перерисовывается.
- Значения в понятной шкале (у настроек — [0..1]).

## View — проводка

View грузит UXML и связывает контролы с VM в обе стороны. Критично: **VM → UI шли без
события**, иначе `RegisterValueChangedCallback` поймает своё же обновление и зациклит.

```csharp
// UI → VM: пользователь двигает слайдер
row.Slider.RegisterValueChangedCallback(e => _vm.SetMaster(e.newValue));

// VM → UI: пришло Changed — переставить контрол БЕЗ эха
void Redraw() => row.SetValueWithoutNotify(_vm.Master);
_vm.Changed += Redraw;
```

Достаёт контролы через `Q<T>()` по имени/классу (`root.Q<SliderRow>("row-master")`)
или, для custom control, напрямую как типизированный элемент.

## Runtime binding (Unity 6)

Для простой синхронизации «свойство ↔ контрол» можно использовать декларативный
runtime binding UI Toolkit вместо ручного `Q<>()`+событий. Уместно, когда связь
один-к-одному и не нужна логика отката/валидации в момент изменения. Для экранов со
снапшотами и живым применением (как настройки) ручная проводка через VM прозрачнее —
выбирай по сложности, не тащи биндинг ради биндинга.

## Роутер

`IMenuRouter`/`MenuRouter` переключают экраны/страницы (табы). Логика показа/скрытия
и активной страницы живёт в роутере, а не в UXML — в разметке только каркас с
модификаторами (`gm-tab-page--hidden`). Новую страницу заводи как модификатор + ветку
в роутере.

## Чеклист нового экрана

- [ ] UXML собран из `.gm-*`, каркас страниц через модификаторы
- [ ] ViewModel — POCO, зависимости через конструктор (DI), не MonoBehaviour
- [ ] View подписал UI→VM (события контролов) и VM→UI (`SetValueWithoutNotify`)
- [ ] Текст через loc-ключи
- [ ] VM покрыт тестом без сцены (логика отката/сохранения)
