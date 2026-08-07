<p>
  <a href="#english"><b>English</b></a>
  &nbsp;·&nbsp;
  <a href="#русский"><b>Русский</b></a>
</p>

# Guildmaster — Autobattler

> Co-op autobattler roguelike for 1–4 players. In active development.

<p>
  <img src="https://img.shields.io/badge/Unity-6000.4.8f1-black?logo=unity" alt="Unity"/>
  <img src="https://img.shields.io/badge/Platform-Windows%20%2F%20PC-blue" alt="Platform"/>
  <img src="https://img.shields.io/badge/Status-In%20Development-yellow" alt="Status"/>
  <img src="https://img.shields.io/badge/License-All%20Rights%20Reserved-red" alt="License"/>
  <a href="https://github.com/Kaliguri/Guildmaster-Autobattler/actions/workflows/ci.yml"><img src="https://github.com/Kaliguri/Guildmaster-Autobattler/actions/workflows/ci.yml/badge.svg" alt="CI"/></a>
  <a href="https://kaliguri.github.io/Guildmaster-Autobattler/"><img src="https://img.shields.io/badge/Docs-GitHub%20Pages-blueviolet?logo=github" alt="Docs"/></a>
</p>

<p>
  <a href="https://kaliguri.github.io/Guildmaster-Autobattler/"><b>Wiki &amp; design docs</b></a>
  &nbsp;·&nbsp;
  <a href="https://kaliguri.github.io/Guildmaster-Autobattler/api/"><b>C# API reference</b></a>
</p>

---

## English

You run a guild preparing for the Grand Championship. Your roster is made of **Vessels** — ordinary
people who cannot fight on their own; their power comes from the **Hero Mementos** they take on.
Before a fight you decide who carries what, where they stand and whom they focus — then the battle
plays out on its own while you bend it in real time with the **Guildmaster's** spells.

Tactical depth in a party-game shell, for one to four players. Inspired by *Slay the Spire*,
*Baldur's Gate 3*, *Divinity: Original Sin 2* and *Teamfight Manager*.

| | |
|---|---|
| **Vessels & Hero Mementos** | A Vessel is a person, a Memento is a fighter. Equipping one grants its stats and its whole kit — closer to a player picking a champion than to equipping a sword |
| **You do not control units** | Combat resolves from the positions, target priorities and AI profiles you set beforehand. No crits, no random misses — the fight is readable, and pause is always available |
| **The Guildmaster casts** | You never fight directly. Your spells land in real time, and they are the main lever you hold once the battle starts |
| **Roguelike run** | A branching act map of fights, events, shops and elite guilds, ending in a boss. Rewards are Mementos, gold and capacity |
| **Co-op for 1–4** | The host shares Vessels across the party over Steam; each player runs their own |

### What actually runs today

*Snapshot of 2026-08-07. This section is kept honest deliberately — the project is mid-development,
and a README that describes intentions is worth nothing to anyone reading the code.*

**Playable end to end:** boot → profile → main menu → guild selection → act map → nodes (battle,
text event, shop, chest, camp) → rewards → boss or defeat → outcome. The run autosaves at every node
transition and can be continued.

**Combat.** A deterministic simulation at 30 Hz with AI at 10 Hz, fifteen steps in a fixed tick
order, damage and healing applied in a single commit per tick. Sixty runtime effect components:
shields, thorns, lifesteal, control, stealth, freeze and burn stacks, marks, parry, block, summons,
delayed detonations, dispel. Auto-attacks run on integer ticks with a windup and a contact frame, and
a single swing can carry several hits. Battles are recorded to a tape, which is also what plays behind
the main menu.

**Content.** 108 effects, 27 Hero Mementos, 20 enemies, 56 tags, 30 AI profiles, 8 encounters —
all data-driven ScriptableObjects, no hardcoded stats.

**Tooling** (44 editor menu entries, all under one root): a headless balance bench with class corridors,
duel matrices and an HTML report site; an animation lab for the skeletal rig; a content hub; a palette
remapper; a post-FX lab; an in-game dev console with 30 commands.

**Quality.** Around 1300 EditMode tests plus a PlayMode suite, over a codebase of 25 assemblies. CI
runs EditMode on every push, adds PlayMode and a Windows player build on `master`, and separately
gates broken links in the documentation vault.

**In progress:** co-op networking (host-authoritative, over Steam). **Designed, not built yet:**
Vessels as authored content, meta-progression between runs, the guild courtyard as a place, camp
actions, acts beyond the first.

---

## Русский

> Кооперативный автобатлер-рогалик для 1–4 игроков. В активной разработке.

Вы ведёте гильдию, готовящуюся к Великому Чемпионату. Ваш отряд — **Сосуды**: обычные люди, которые
сами не умеют сражаться; сила приходит к ним из **Реликвий**, которые они принимают. Перед боем вы
решаете, кто какую возьмёт, где встанет и кого будет бить, — а дальше бой идёт сам, и вы гнёте его в
реальном времени заклинаниями **Гильдмастера**.

Тактическая глубина в оболочке весёлой вечеринки, на одного-четырёх игроков. Вдохновлён *Slay the
Spire*, *Baldur's Gate 3*, *Divinity: Original Sin 2* и *Teamfight Manager*.

| | |
|---|---|
| **Сосуды и Реликвии** | Сосуд — человек, Реликвия — боец. Принятая Реликвия даёт свои статы и весь свой кит: это ближе к выбору чемпиона, чем к надеванию меча |
| **Юнитами вы не управляете** | Бой считается по заранее заданным позициям, приоритетам целей и профилям AI. Ни крита, ни промаха — бой читается, и паузу можно поставить в любой момент |
| **Гильдмастер кастует** | Сам он не дерётся. Его заклинания ложатся в реальном времени и остаются главным рычагом, когда бой уже начался |
| **Рогалик-забег** | Ветвящаяся карта акта: бои, события, лавки, элитные гильдии и босс в конце. Награды — Реликвии, золото и вместимость |
| **Кооп на 1–4** | Хост делит Сосудов по отряду через Steam, каждый играет за своих |

### Что работает на самом деле

*Снимок на 07.08.2026. Раздел намеренно честный: проект в середине разработки, и README, описывающий
намерения, не стоит ничего для того, кто открыл код.*

**Проходится от начала до конца:** бут → профиль → главное меню → выбор гильдии → карта акта → узлы
(бой, текст-событие, лавка, сундук, привал) → награда → босс или поражение → экран исхода. Забег
автосохраняется на каждом переходе между узлами и продолжается с места.

**Бой.** Детерминированная симуляция на 30 Гц, AI на 10 Гц, пятнадцать шагов в фиксированном
порядке тика, урон и лечение применяются одним коммитом за тик. Шестьдесят рантайм-компонентов
эффектов: щиты, шипы, вампиризм, контроль, маскировка, стаки холода и огня, метки, парирование, блок,
призывы, отложенные взрывы, диспел. Авто-атаки идут на целых тиках с замахом и кадром контакта, один
свинг может нести несколько Ударов. Бой пишется лентой — та же лента крутит бой за главным меню.

**Контент.** 108 эффектов, 27 Реликвий, 20 врагов, 56 тегов, 30 AI-профилей, 8 энкаунтеров — всё
данными в ScriptableObject, без захардкоженных статов.

**Инструменты** (44 пункта редакторного меню под одним корнем): headless-стенд баланса с классовыми
коридорами, дуэльными матрицами и HTML-отчётом; лаборатория анимации для костяного рига; хаб контента;
ремаппер палитры; стенд пост-обработки; внутриигровая дев-консоль на 30 команд.

**Качество.** Около 1300 EditMode-тестов плюс PlayMode-набор поверх кодовой базы из 25 сборок. CI
гоняет EditMode на каждый пуш, добавляет PlayMode и сборку плеера под Windows на `master` и отдельно
блокирует битые ссылки в вики.

**В работе:** кооп-сеть (авторитет хоста, поверх Steam). **Спроектировано, но не построено:** Сосуды
как авторский контент, метапрогрессия между забегами, Двор гильдии как место, действия привала, акты
после первого.

---

## Screenshots

> Coming soon — the visual layer is being reworked. / Скоро: визуальный слой переделывается.

---

<details id="for-developers">
<summary><b>For developers</b></summary>

## Tech stack

| | |
|---|---|
| **Engine** | Unity 6 (6000.4.8f1), URP 17 |
| **Language** | C# |
| **Platform** | Windows / PC |
| **CI/CD** | GitHub Actions + [GameCI](https://game.ci) |
| **Docs** | Quartz v4 + Doxygen + GitHub Pages |

| Category | Package | Purpose |
|---|---|---|
| **DI / Events** | [VContainer](https://github.com/hadashiA/VContainer) | DI container — no singletons anywhere |
| | [MessagePipe](https://github.com/Cysharp/MessagePipe) | Typed pub/sub over DI. Deliberately stops at the combat assembly boundary |
| **Async** | [UniTask](https://github.com/Cysharp/UniTask) | Zero-alloc async/await instead of coroutines |
| **UI** | [UI Toolkit](https://docs.unity3d.com/Manual/UIElements.html) + MVVM | 26 UXML screens on a token-based design system |
| | [LitMotion](https://github.com/annulusgames/LitMotion) | Zero-alloc tweens |
| | [Shapes](https://acegikmo.com/shapes/) | Procedural vector graphics |
| **Multiplayer** | Custom netcode | Host-authoritative, written for this project — the battle ships as tape chunks, not per-tick state |
| | [Facepunch.Steamworks](https://github.com/Facepunch/Facepunch.Steamworks) | Steam lobbies and transport |
| **Data** | Custom JSON saves | Own `ISaveService`: atomic write, `.bak`, schema versions, Steam Auto-Cloud |
| | Newtonsoft.Json | DTO serialization |
| | Unity Localization | EN + RU. Keys are authored with the content, not retrofitted; the English tables are still catching up |
| **Audio** | FMOD | Behind an `IAudioService` seam; a test asserts every key called from code resolves to an event |
| **Tooling** | [Odin Inspector](https://odininspector.com) | Editor-only, `[SerializeReference]` dropdowns |

## How the code is organised

```
Assets/_Project/
├── Scripts/
│   ├── Core/         # Seams: input, audio, saves, RNG, simulation constants
│   ├── Data/         # ScriptableObject definitions, stats, content registry
│   ├── Combat/       # Deterministic simulation — 30 Hz, two-phase tick
│   ├── Presentation/ # Views, VFX, camera, feel — reads the sim, never writes to it
│   ├── Game/         # Boot, run flow, activities, deployment, session
│   ├── Guild/        # Roster, run state, act map generation
│   ├── Net/          # Co-op sessions and Steam transport
│   ├── UI/           # UI Toolkit screens and the design system
│   ├── Balance/      # SimBench — headless balance benchmarks
│   └── DevTools/     # Dev console and overlays
├── Tests/{EditMode,PlayMode}
└── ScriptableObjects/ · Prefabs/ · Scenes/ · UI/ · Art/
```

Everything else under `Assets/` is vendor code. `docs/wiki` is an Obsidian vault (game design +
technical), published as the docs site; `scripts/` holds the PowerShell tooling; `tools/` holds
console utilities that run without the editor.

### Principles the code actually follows

- **One fact, one owner.** Code owns the truth about code, a test owns any invariant that lives
  between files, and the decision journal owns *why*. A descriptive document that restates code is
  frozen on purpose — it cannot lie if nobody is asked to trust it.
- **Data, not objects.** Durable run state is a flat DTO of string ids; combat entities are built
  from it by a factory. That is why the save layer is ours and not a plugin.
- **Determinism is a contract.** No `UnityEngine.Random`, no `Time.deltaTime`, no physics inside the
  simulation; iteration order is fixed; effects judge by a start-of-tick snapshot rather than live
  state, so two clients cannot diverge.
- **Fallbacks only for outside failures.** A missing config, scene reference or DI registration is a
  wiring bug and must be loud. Silent degradation inside our own code is treated as a defect.

## CI/CD

| Workflow | Purpose |
|---|---|
| `ci.yml` | Unity Test Runner + player build. EditMode always; PlayMode and the build only where their cost is worth paying |
| `docs.yml` | Quartz + Doxygen → GitHub Pages |
| `docs-lint.yml` | Blocks a PR on broken internal links in the vault |
| `steam-deploy.yml` | Upload to Steam — the version comes from the tag |

Compile without opening the editor:
```powershell
./scripts/compile-check.ps1
```

Run the tests:
```powershell
./scripts/run-tests.ps1
```

</details>

---

## License

© 2026 Max Gaida. All rights reserved.

This repository is public for portfolio and demonstration purposes only.
No license is granted to use, copy, modify, or distribute any part of it
without prior written permission from the author.

See [LICENSE.md](LICENSE.md) for details.
