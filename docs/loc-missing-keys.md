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

Сверка ниже — **ночной заход 03.08.2026**, по файлам таблиц на диске (Unity был закрыт). Размер на
тот момент: `UI Shared Data` — 106 ключей, `Content Shared Data` — 392.

**36 ключей код спрашивает, а в таблицах их нет.** Живут на RU-фолбэке `L("ключ", "текст")`, то есть
по-русски выглядят правильно и в EN-сборке остаются русскими. Крупные группы:

- `ui.newgame.*` — 14 (экран «Новая игра»: режим, гильдия, лобби и подсказки);
- `ui.settings.*` — 10 (громкости, режим окна, разрешение, частота);
- `ui.boot.*` — 3, `ui.mainmenu.create` / `.join`, `ui.menu.invite`, `ui.outcome.continue`;
- `ui.unit.percent` / `.seconds` / `.per_second` — они не литералы, а константы `DescriptionService`,
  поэтому обычным грепом по коду не находятся.

**`tag.bleed.name` в таблице нет,** хотя тег живёт в резолвере и у остальных 19 тегов ключ заведён.
Единственная дыра в контентной таблице — и единственная, где текста нет вообще нигде.

**14 строк-призраков.** Переводы семи базовых предметов (Cracked Shield, Notched Sword, Worn Knife,
Crooked Bow, Cracked Amulet, Chipped Skull, Bundle of Herbs) лежат в `Content_en` и `Content_ru`, а
ключа в `SharedTableData` нет: id не резолвится, Unity эти строки не отдаст никогда. Рядом живёт
общий `relic.base.name` — стоит решить, кто теперь держит имена стартовых предметов, прежде чем
чистить.

**Английская таблица практически пуста.** В `UI_en` из 106 ключей реальный перевод **один**, остальное
— заглушки `-`/`—` или отсутствие строки. В `Content_en` реальных 85 из 392. Локаль выбирается из ОС,
переключателя в игре нет (BE-4), так что на английской машине это видно сразу.

**В RU не заполнены 80 ключей `Content`** — эффекты (41), враги (26), виды (7), предметы (6).
Например `effect.antiheal_{weak,medium,strong}.name`, `effect.bandit_*.name`.

**Строк-сирот в UI больше не ищем поимённо:** прежний список из восьми устарел, а сироты контентной
таблицы почти все ложные — ключ собирается из id динамически (`{id}.name`, `.desc`, `.desc.full`,
падежи), и «ноль вхождений грепом» про них ничего не доказывает.
