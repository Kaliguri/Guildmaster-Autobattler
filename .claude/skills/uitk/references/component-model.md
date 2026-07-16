# Компонентная модель и USS-нейминг

Читать перед созданием нового переиспользуемого элемента. Здесь — как выбрать между
UXML-темплейтом и custom control, точный синтаксис Unity 6 и правила BEM.

## Оглавление
- [Развилка: темплейт или custom control](#развилка)
- [UXML-темплейт (без логики)](#uxml-темплейт)
- [Custom control (с логикой)](#custom-control)
- [Граница с hard-правилом «UXML only»](#граница)
- [BEM-нейминг](#bem)
- [Антипаттерны](#антипаттерны)

## Развилка

| Признак | Бери |
|---|---|
| Чисто визуальный повтор, состояния/логики нет | **UXML-темплейт** |
| Есть внутреннее состояние, реакция на ввод, вычисляемая подпись | **Custom control** |
| Нужны типизированные атрибуты в UXML/UI Builder | **Custom control** |
| Собираешь из готовых `.gm-*` без нового поведения | Просто композиция в экране, компонент не нужен |

Правило дублирования: **скопировал разметку/стиль дважды — на третий выноси.** До
третьего раза инлайн допустим (преждевременная абстракция дороже).

## UXML-темплейт

Для повторяющегося визуального блока без C#. Определяешь фрагмент в отдельном `.uxml`
и инстанцируешь:

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:Template src="RelicBadge.uxml" name="RelicBadge" />
    <ui:Instance template="RelicBadge" class="reward__badge" />
    <ui:Instance template="RelicBadge" class="reward__badge" />
</ui:UXML>
```

Плюсы: ноль кода, редактируется в UI Builder. Минус: параметризация ограничена —
данные проставляются снаружи (из View по `name`/классу).

## Custom control

Для блока с логикой/состоянием. Эталон — `SliderRow`
(`Assets/_Project/Scripts/UI/Components/SliderRow.cs`). Канон Unity 6:

```csharp
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    [UxmlElement]                       // экспонирует в UXML и UI Builder
    public partial class SliderRow : VisualElement   // ОБЯЗАТЕЛЬНО partial
    {
        private readonly Slider _slider;

        [UxmlAttribute]                 // атрибут виден в UXML: <gm:SliderRow LabelText="..." />
        public string LabelText { get => _label.text; set => _label.text = value; }

        public SliderRow()
        {
            AddToClassList("gm-slider-row");            // BEM-класс блока в конструкторе
            _slider = new Slider { name = "slider" };
            _slider.AddToClassList("gm-slider");
            Add(_slider);
        }

        // VM → UI без обратного события (иначе цикл):
        public void SetValueWithoutNotify(float v) => _slider.SetValueWithoutNotify(v);
    }
}
```

Использование в UXML (префикс `gm:`, неймспейс объявлен в корне документа):

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:gm="Guildmaster.UI.Components">
    <gm:SliderRow name="row-master" LabelText="Общая громкость" />
</ui:UXML>
```

Правила custom control:
- Класс **`partial`** — генератор Unity 6 дописывает фабрику. Без `partial` не скомпилится.
- **`[UxmlElement]` вместо `UxmlFactory`/`UxmlTraits`** — старая пара deprecated в Unity 6,
  новые контролы на них не пиши.
- `[UxmlAttribute]` — только примитивы (string, float, bool, enum). Сложные данные
  (списки, SO) передавай в рантайме через C#/биндинг, не через UXML.
- BEM-классы вешай в конструкторе через `AddToClassList`, вид держи в `components.uss`,
  не инлайном.
- Колбэки — предпочтительно статические лямбды (меньше аллокаций), если не нужен `this`.

## Граница

Hard-правило «разметка только UXML, никогда кодом» — про **композицию экранов**. Внутри
атомарного custom control строить под-элементы кодом в конструкторе (`new Label(); Add(...)`)
— **норма**: это и есть переиспользуемый кирпич, у которого нет осмысленного UXML-тела.
`SliderRow` — доказательство. Нарушение — это когда **экран** собирается императивно в C#
вместо `.uxml`.

## BEM

`block__element--modifier`. Официальная рекомендация Unity и наш де-факто стандарт.

- **Block** — самостоятельная сущность: `gm-panel`, `gm-button`, `gm-slider-row`.
- **Element** — часть блока, через `__`: `gm-panel__title`, `gm-slider-row__value`.
- **Modifier** — флаг вида/состояния, через `--`: `gm-button--primary`, `gm-tab--active`,
  `gm-tab-page--hidden`.

Имена — по роли, не по типу: `inventory__slot--equipped`, а не `inventory__button--red`.
Читаемость важнее краткости.

## Антипаттерны

- ❌ Имена типов и id в селекторах стиля (`Button {}`, `#save-btn {}`) — только классы.
- ❌ Инлайн-`style` в UXML/C# для того, что должно быть классом.
- ❌ Хардкод `rgb()/px` вместо `var(--gm-*)`.
- ❌ Новый контрол на `UxmlFactory`/`UxmlTraits` (deprecated).
- ❌ `[UxmlAttribute]` на сложном типе — не сериализуется, передавай в рантайме.
- ❌ Компонент есть, а в `UiGalleryScreen.uxml` его нет — витрина рассинхронилась.
