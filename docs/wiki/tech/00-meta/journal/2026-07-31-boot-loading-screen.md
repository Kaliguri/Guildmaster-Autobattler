---
title: "Journal - Boot Loading Screen"
date: 2026-07-31
tags: [ui, uitk, boot]
---

**Решили:** голый титул («печать + текст + „нажмите“» на чёрном) заменён на экран загрузки в духе
Ravenswatch: лого-lockup в тёплом свечении, ряд атрибуций внизу (`Alebardium / Made with Unity /
FMOD`), «Загрузка» + вращающийся знак, уход по ЛЮБОЙ клавише/клику с opacity-фейдом. Экран остался в
той же точке потока (`GameFlow.RunGameAsync` → title → menu) — переписаны только UXML/USS/View.

**Почему так, а не иначе:**
- **Свечение — PNG-ассет, не USS.** UI Toolkit USS не умеет radial-gradient; тёплый ореол за лого
  запечён в `BootGlow.png` (Pillow + Gaussian blur) и лёг `background-image`. Альтернатива (Painter2D-
  градиент кодом) дороже и ради temp-фона не оправдана.
- **Переход — opacity-фейд в USS, а не чернильная шторка нод.** `IScreenTransition` (чернила) сильнее,
  но подключить её к титулу = правка C#-сервисов + DI + domain reload у открытого редактора. Для temp
  выбран `.gm-boot--out { opacity: 0 }` с `transition`. Чернила — кандидат на потом.
- **Спиннер декоративен.** Загрузка мира (`GameBootstrap.BootAsync` → `LoadWorldAsync`) проходит ДО
  показа экрана, настоящего прогресса тут нет — знак крутится, пока игрок не нажмёт. Перестройка буте
  под реальный прогресс-бар ради temp отвергнута.
- **Иконки/лого атрибуций текстом.** Официальных логотипов Unity/FMOD нет файлами; FMOD-лицензия
  требует видимую атрибуцию — пока честный текст, официальный логотип подкладывается позже.

**Грабли:**
- **Auto Refresh выключен** (editor-compile-diet): после правки `.cs` изменения НЕ войдут в play, пока
  не сделать явный `AssetDatabase.Refresh()`/`refresh_unity`. Без него игрок запустил бы старый код.
- **`render_ui` требует `target`** (GameObject с UIDocument) и в play-mode работает в два вызова
  (queue → retrieve). Editor-mode render у рантайм-панели пуст — настоящий скрин снят коротким заходом
  в play с `target="UI Root"`.
- **`execute_code` не тянет `using UnityEngine.UIElements`** — extension-метод `Q<T>` не виден; звать
  через `UnityEngine.UIElements.UQueryExtensions.Q<T>(root, name)`.

**Инвариант, который легко нарушить:** `TitleCardScreenView.FadeOutMs` (350) обязан совпадать с
`transition-duration` класса `.gm-boot--out` в `components.uss` (0.35s). Разойдутся — меню либо
выпрыгнет до окончания гашения, либо повиснет лишнее время на чёрном. Связь C#↔USS держится
комментарием у обоих концов (теста нет — UI временный).

**Владелец правды:** `Assets/_Project/Scripts/UI/TitleCardScreenView.cs`,
`Assets/_Project/UI/Screens/TitleCardScreen.uxml`, раздел `.gm-boot*` в
`Assets/_Project/UI/Theme/components.uss`.
