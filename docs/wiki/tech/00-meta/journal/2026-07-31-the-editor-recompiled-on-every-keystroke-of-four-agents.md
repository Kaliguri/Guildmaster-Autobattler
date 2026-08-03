---
title: "Journal - The Editor Recompiled On Every Keystroke Of Four Agents"
date: 2026-07-31
tags: [tooling, editor, packages]
---

**Решили:** Auto Refresh в редакторе **выключен** (`kAutoRefreshMode = 0`) — компиляция идёт по
Ctrl+R, по входу в Play или по вызову `refresh_unity` из MCP. Заодно к Enter Play Mode Options
добавлен `DisableSceneReload`, а из проекта вынесены Easy Save 3, Feel и пакет
`com.unity.visualscripting`.

**Почему:** замер одного цикла до правок — 13.9 с на изменение ОДНОГО файла (компиляция 4.6 с,
`ImportOutOfDateAssets` 5.7 с, domain reload 8.6 с отдельно). При включённом Auto Refresh цикл
запускает любое сохранение файла — в том числе агентское, а сессий параллельно четыре, и редактор
почти не выходил из RELOAD. Отвергли промежуточный режим «Enabled Outside Playmode»: он спасает
только play-mode, а фон правок остаётся прежним.

**Грабли и честная цена.** Рез пакетов дал меньше, чем настройка, и это стоит знать заранее:
domain reload как был ~9–10 с, так и остался, потому что его основная статья —
`ProcessInitializeOnLoadAttributes` (~3.2 с) — это Odin, FMOD, Quantum Console и сам MCP-мост, а не
удалённое. Что реально изменилось: проект похудел с 3265 скриптов до 2769 и с 14601 не-скриптового
ассета до 14035, из загрузки ушли восемь сборок Visual Scripting. Выигрыш не в длительности одного
цикла, а в их **количестве**.

Три места держали удалённое и молча сломались бы: `FMODUnityEditor.asmdef` жёстко ссылался на
`Unity.VisualScripting.*` (ссылки сняты; сам `BoltIntegration.cs` за дефайном
`UNITY_VISUALSCRIPTING_EXIST` гаснет сам), `ProjectSettings` нёс дефайны
`UNITY_VISUAL_SCRIPTING;ES3_TMPRO;ES3_UGUI` на все платформы, а докстринги `ISaveService`,
`JsonFileSaveService`, `ISettingsService` и комментарий в `RootLifetimeScope` числили ES3
референсом и `persistentDataPath` корнем сейвов — второе врало ещё до реза.

Отдельная готча процесса: **с выключенным Auto Refresh правку файла подхватывает только явный
рефреш**. Агентам звать `refresh_unity` (scope `all`) в конце пачки правок, а не после каждой —
ради этого всё и затевалось.

**Владелец правды:** `Packages/manifest.json`, `ProjectSettings/ProjectSettings.asset`
(`scriptingDefineSymbols`), `Assets/Plugins/FMOD/src/Editor/FMODUnityEditor.asmdef`, `CLAUDE.md`
(раздел «Ловушки стека»).
