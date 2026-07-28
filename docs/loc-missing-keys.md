# Локализация: рецепт заведения ключей и текущий остаток

Заведение 35 ключей и перенос 15 выполнены (`3fae1637`). Здесь остаётся то, ради чего документ нужен дальше: **как это делать** и **что ещё висит**.

## Рецепт: завести ключи скриптом через Unity MCP

Работает, занимает ~1.5 секунды на 35 ключей. Две готчи, каждая стоила попытки:

**1. `using`-директив в `execute_code` быть не может.** Код исполняется как ТЕЛО метода, поэтому `using UnityEditor.Localization;` наверху — синтаксическая ошибка, а не удобство. Нужны полные имена: `UnityEditor.Localization.LocalizationEditorSettings`. Коварство в том, что при падении компиляции ответ иногда теряется по таймауту или обрывом соединения, и выглядит это как «Unity подвис» — хотя редактор в полном порядке и просто вернул ошибку. Прежде чем винить Unity, замерьте: `GetStringTableCollection` открывает коллекцию за 3 мс.

**2. `AssetDatabase.SaveAssets()` не использовать.** Он пишет ВСЕ грязные ассеты проекта — при параллельных сессиях это чужая незакоммиченная работа заодно с вашей. Только точечно: `AssetDatabase.SaveAssetIfDirty(asset)` по каждой затронутой таблице.

Рабочая форма:

```csharp
var col = UnityEditor.Localization.LocalizationEditorSettings.GetStringTableCollection("UI");
UnityEngine.Localization.Tables.StringTable ru = null, en = null;
foreach (var t in col.StringTables)
{
    string code = t.LocaleIdentifier.Code;
    if (code.StartsWith("ru")) ru = t; else if (code.StartsWith("en")) en = t;
}

if (!col.SharedData.Contains(key)) col.SharedData.AddKey(key);
ru.AddEntry(key, russianText);
en.AddEntry(key, "—");                     // прочие локали — прочерк

UnityEditor.EditorUtility.SetDirty(col.SharedData);
UnityEditor.EditorUtility.SetDirty(ru);
UnityEditor.AssetDatabase.SaveAssetIfDirty(col.SharedData);
UnityEditor.AssetDatabase.SaveAssetIfDirty(ru);
```

Перенос строки между таблицами — то же самое плюс `srcTable.RemoveEntry(key)` по каждой локали и `content.SharedData.RemoveKey(key)` в конце. Значения копировать **по локалям**, сопоставляя `LocaleIdentifier.Code`, а не по индексу.

Откуда брать русский текст: он, как правило, уже написан — в C#-фолбэках вида `L("ui.chest.title", "Сундук")`. Собрать пары регуляркой `\b(?:L|Loc)\(\s*"(ui\.[a-z0-9_.]+)"\s*,\s*"([^"]+)"\s*\)` и завести. Это перенос, а не сочинение.

## Что осталось

**Восемь строк-сирот в таблице UI** — заведены, но их никто не спрашивает:

`ui.dev.stat_probe`, `ui.stat.aspd.desc`, `ui.stat.dmg.desc`, `ui.stat.hp.desc`, `ui.stat.marmor.desc`, `ui.stat.move.desc`, `ui.stat.parmor.desc`, `ui.stat.range.desc`

Семь из них — описания статов, похожие на заготовку под тултипы характеристик. Не удалено: текст написан, а потребитель может появиться. Решение за Максом — либо подключить их к панели статов, либо снести как незавершённый задел.

**Шесть ключей `ui.hub.*` в таблице Content** — принадлежат экрану хаба лоадаута, который стоит в волне 2 на снос (R1-21). Уйдут вместе с ним, отдельной работы не требуют.

**`UI_en` — 44 прочерка на 76 ключей.** Новые заведены с `—`, но у части старых английской строки нет вовсе. Английская сборка на них показывает пустоту (см. также BE-4: локаль выбирается из ОС, переключателя в игре нет).
