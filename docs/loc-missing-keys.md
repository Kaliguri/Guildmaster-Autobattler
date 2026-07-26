# Локализация: что осталось завести

Рабочий список к корню RC-4. Собран из кода: русский текст для этих ключей уже написан — он лежит
прямо в C#-фолбэках вида `L("ui.chest.title", "Сундук")`, поэтому заведение сводится к переносу,
а не к сочинению текстов.

Почему не сделано скриптом: `LocalizationEditorSettings` инициализирует систему синхронно и не
укладывается в таймаут моста Unity MCP — две попытки оборвались, ничего не записав. Обрыв на
середине записи испортил бы таблицы, поэтому заводить вручную в редакторе либо отдельным
Editor-скриптом с прогрессом.

Правило заполнения: **RU заполняем, прочие локали — прочерк** (`—`).

---

## Завести в таблицу UI (35)

| Ключ | RU | Где используется |
|---|---|---|
| `ui.chest.hint` | Нажми, чтобы открыть | ChestScreenView.cs |
| `ui.chest.title` | Сундук | ChestScreenView.cs |
| `ui.loadout.basics` | Основное | LoadoutInventoryView.cs |
| `ui.loadout.filter.banners` | Знамёна | LoadoutInventoryView.cs |
| `ui.loadout.filter.items` | Предметы | LoadoutInventoryView.cs |
| `ui.loadout.filter.relics` | Реликвии | LoadoutInventoryView.cs |
| `ui.loadout.search` | Поиск… | LoadoutInventoryView.cs |
| `ui.loadout.skills` | Способности | LoadoutInventoryView.cs |
| `ui.loadout.sort.name` | Имя | LoadoutInventoryView.cs |
| `ui.loadout.stats` | Характеристики | LoadoutInventoryView.cs |
| `ui.loadout.upgrades` | Улучшения | LoadoutInventoryView.cs |
| `ui.loadout.video` | видео-вставка 16:9 | LoadoutInventoryView.cs |
| `ui.mainmenu.continue` | Продолжить | MainMenuScreenView.cs |
| `ui.mainmenu.quit` | Выход | MainMenuScreenView.cs |
| `ui.mainmenu.settings` | Настройки | MainMenuScreenView.cs |
| `ui.mainmenu.start` | Начать забег | MainMenuScreenView.cs |
| `ui.menu.quit` | Выйти из игры | MenuRouter.cs |
| `ui.menu.to_main_menu` | В главное меню | MenuRouter.cs |
| `ui.mode.menu` | Меню | RunModeBarView.cs |
| `ui.outcome.defeat` | Поражение | OutcomeScreenView.cs |
| `ui.outcome.defeat_sub` | Забег окончен. | OutcomeScreenView.cs |
| `ui.outcome.to_menu` | В меню | OutcomeScreenView.cs |
| `ui.outcome.victory` | Победа | OutcomeScreenView.cs |
| `ui.outcome.victory_sub` | Акт пройден. | OutcomeScreenView.cs |
| `ui.run.floor` | Веха | RunModeBarView.cs |
| `ui.settings.card_anim` | Анимация карточек | MenuRouter.cs |
| `ui.settings.card_attack` | Анимация атаки карточек | MenuRouter.cs |
| `ui.shop.buy` | Купить | ShopScreenView.cs |
| `ui.shop.gold` | Золото | ShopScreenView.cs |
| `ui.shop.leave` | Уйти | ShopScreenView.cs |
| `ui.shop.no_space` | Нет места — продай реликвию! | ShopScreenView.cs |
| `ui.shop.reroll` | Обновить | ShopScreenView.cs |
| `ui.shop.sell` | Продать | ShopScreenView.cs |
| `ui.shop.sold` | Куплено | ShopScreenView.cs |
| `ui.shop.title` | Лавка | ShopScreenView.cs |

## Не заводить — мёртвая ветка (1)

Экран хаба лоадаута идёт под снос волной 2 (R1-21). Ключи ему не нужны.

- `ui.hub.hint` — Перетащи реликвию из запаса на сосуд — наденешь; с сосуда в запас — снимешь.

## Перенести из Content в UI (16)

Эти строки существуют и работают, но лежат не в своей таблице: сейчас их подхватывает фолбэк
`LocalizationService`, каждый раз выписывая предупреждение с обеими таблицами.

- `ui.hub.banners`
- `ui.hub.close`
- `ui.hub.gold`
- `ui.hub.stash`
- `ui.hub.team`
- `ui.hub.title`
- `ui.mainmenu.title`
- `ui.reward.drop_hint`
- `ui.reward.skip`
- `ui.reward.take`
- `ui.reward.title`
- `ui.run.act`
- `ui.run.start`
- `ui.titlecard.hint`
- `ui.titlecard.studio`
- `ui.titlecard.title`

## Сироты в таблице UI (8)

Строки заведены, но их никто не спрашивает — либо потребитель удалён, либо ключ переименован:

- `ui.dev.stat_probe`
- `ui.stat.aspd.desc`
- `ui.stat.dmg.desc`
- `ui.stat.hp.desc`
- `ui.stat.marmor.desc`
- `ui.stat.move.desc`
- `ui.stat.parmor.desc`
- `ui.stat.range.desc`

## Ещё замечено

`UI_en` содержит 9 записей на 41 ключ — прочерки проставлены не для всех. Заводя новые ключи,
проставить `—` и заодно закрыть остальные, иначе английская сборка показывает пустоту.