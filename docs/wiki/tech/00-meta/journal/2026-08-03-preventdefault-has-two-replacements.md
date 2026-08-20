---
title: "Journal - PreventDefault Has Two Replacements, Not One"
date: 2026-08-03
tags: [ui, uitk, input, focus]
---

**Решили:** устаревший `EventBase.PreventDefault()` заменяем парой `StopPropagation()` +
`focusController.IgnoreEvent(evt)`, а не одним `StopPropagation()`.

**Почему:** у отменённого метода было две обязанности, и Unity разнесла их по двум вызовам.
`StopPropagation` гасит только дальнейшее распространение события по дереву; навигацию по фокусу —
ту самую, что уводит фокус на соседний элемент, — отменяет исключительно
`focusController.IgnoreEvent`. Отвергнутая альтернатива «просто убрать `PreventDefault`, ведь
`StopPropagation` рядом уже есть» выглядит естественной ровно до первого запуска: она возвращает
баг, ради которого обработчики и писались.

**Грабли:** предупреждение компилятора называет обе замены через «и/или», и «или» читается как
«достаточно любой». Достаточно не любой: для `NavigationMoveEvent` и `NavigationSubmitEvent` нужны
обе. Симптом отказа не в консоли, а в поведении — Tab и стрелки внутри дев-консоли уводят фокус на
постороннюю кнопку, и следующий Enter нажимает её.

**Владелец правды:** `DevConsoleScreen.Consume`, `DevBattleBrowserScreen.Consume` — оба метода
несут `<remarks>` с этим правилом.
