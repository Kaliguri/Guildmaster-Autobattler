<p>
  <a href="#english"><b>English</b></a>
  &nbsp;·&nbsp;
  <a href="#русский"><b>Русский</b></a>
</p>

# Guildmaster — Autobattler

> Co-op autobattler roguelike for 1–4 players. In development.

<p>
  <img src="https://img.shields.io/badge/Unity-6000.4.8f1-black?logo=unity" alt="Unity"/>
  <img src="https://img.shields.io/badge/Platform-Windows%20%2F%20PC-blue" alt="Platform"/>
  <img src="https://img.shields.io/badge/Status-In%20Development-yellow" alt="Status"/>
  <img src="https://img.shields.io/badge/License-All%20Rights%20Reserved-red" alt="License"/>
  <a href="https://github.com/Kaliguri/Guildmaster-Autobattler/actions/workflows/ci.yml"><img src="https://github.com/Kaliguri/Guildmaster-Autobattler/actions/workflows/ci.yml/badge.svg" alt="CI"/></a>
  <a href="https://kaliguri.github.io/Guildmaster-Autobattler/"><img src="https://img.shields.io/badge/Docs-GitHub%20Pages-blueviolet?logo=github" alt="Docs"/></a>
</p>

| | |
|---|---|
| **Architecture** | <img alt="DI" src="https://img.shields.io/badge/DI-8957e5?style=flat-square"/> <img alt="MVVM" src="https://img.shields.io/badge/MVVM-8957e5?style=flat-square"/> <img alt="EventBus" src="https://img.shields.io/badge/EventBus-8957e5?style=flat-square"/> <a href="https://github.com/hadashiA/VContainer"><img alt="VContainer" src="https://img.shields.io/badge/VContainer-1f6feb?style=flat-square"/></a> <a href="https://github.com/Cysharp/MessagePipe"><img alt="MessagePipe" src="https://img.shields.io/badge/MessagePipe-1f6feb?style=flat-square"/></a> |
| **Patterns** | <img alt="State Machine" src="https://img.shields.io/badge/State_Machine-8957e5?style=flat-square"/> <img alt="Command" src="https://img.shields.io/badge/Command-8957e5?style=flat-square"/> <img alt="Object Pool" src="https://img.shields.io/badge/Object_Pool-8957e5?style=flat-square"/> <img alt="Strategy" src="https://img.shields.io/badge/Strategy-8957e5?style=flat-square"/> |
| **Async / UI** | <a href="https://github.com/Cysharp/UniTask"><img alt="UniTask" src="https://img.shields.io/badge/UniTask-1f6feb?style=flat-square"/></a> <a href="https://github.com/annulusgames/LitMotion"><img alt="LitMotion" src="https://img.shields.io/badge/LitMotion-1f6feb?style=flat-square"/></a> <a href="https://docs.unity3d.com/Manual/UIElements.html"><img alt="UI Toolkit" src="https://img.shields.io/badge/UI_Toolkit-222222?style=flat-square&logo=unity&logoColor=white"/></a> |
| **Multiplayer** | <a href="https://docs-multiplayer.unity3d.com/"><img alt="NGO" src="https://img.shields.io/badge/NGO-222222?style=flat-square&logo=unity&logoColor=white"/></a> <a href="https://github.com/Facepunch/Facepunch.Steamworks"><img alt="Facepunch.Steamworks" src="https://img.shields.io/badge/Facepunch.Steamworks-171a21?style=flat-square&logo=steam&logoColor=white"/></a> |
| **Data** | <a href="https://docs.unity3d.com/Packages/com.unity.addressables@latest"><img alt="Addressables" src="https://img.shields.io/badge/Addressables-222222?style=flat-square&logo=unity&logoColor=white"/></a> <a href="https://docs.unity3d.com/Packages/com.unity.localization@latest"><img alt="Unity Localization" src="https://img.shields.io/badge/Unity_Localization-222222?style=flat-square&logo=unity&logoColor=white"/></a> <a href="https://moodkie.com/easy-save/"><img alt="Easy Save 3" src="https://img.shields.io/badge/Easy_Save_3-1f6feb?style=flat-square"/></a> <a href="https://www.newtonsoft.com/json"><img alt="Newtonsoft.Json" src="https://img.shields.io/badge/Newtonsoft.Json-1f6feb?style=flat-square"/></a> <a href="https://odininspector.com"><img alt="Odin Inspector" src="https://img.shields.io/badge/Odin_Inspector-1f6feb?style=flat-square"/></a> |
| **Audio** | <a href="https://www.fmod.com"><img alt="FMOD" src="https://img.shields.io/badge/FMOD-000000?style=flat-square&logo=fmod&logoColor=white"/></a> |

<p>
  <a href="https://kaliguri.github.io/Guildmaster-Autobattler/"><b>🌐 Wiki & docs</b></a>
  &nbsp;·&nbsp;
  <a href="https://kaliguri.github.io/Guildmaster-Autobattler/api/"><b>🔧 C# API reference</b></a>
</p>

---

## Trailer & gameplay

<!-- Trailer / gameplay GIF goes here — the visual hook. -->
<p><i>🎬 Trailer &amp; gameplay GIF — coming soon.</i></p>

---

## English

**Guildmaster** is a co-op autobattler roguelike for 1–4 players — tactical depth (team-building, Hero Memento management, positioning) wrapped in party-game spirit. Inspired by *Slay the Spire*, *Baldur's Gate 3*, *Divinity: Original Sin 2* and *Teamfight Manager*.

You lead a guild preparing for the Grand Championship. Your roster is made of **Vessels** — ordinary people who can't fight on their own; their power comes from the **Hero Mementos** they equip. Before each battle you decide which Vessel takes which Memento, set positions and target priorities — then the fight plays out automatically while you sway it in real time with the **Guildmaster's** spells.

Built for **1–4 players**: solo, you command the whole guild; in co-op, each player runs their own Vessels.

> **What this project demonstrates:** a deterministic real-time combat simulation, a DI-driven architecture (VContainer + MessagePipe) with no singletons, host-authoritative co-op netcode (NGO + Steam), and a fully data-driven content pipeline (ScriptableObjects + Addressables) — covered by an EditMode/PlayMode test suite in CI.

### Key features

| Feature | Description |
|---|---|
| **Vessels & Hero Mementos** | Vessels are ordinary guild members who can't fight alone; equipping a **Hero Memento** grants them its stats and unique abilities — think a player and their champion in *LoL* |
| **Guildmaster** | Doesn't fight directly — casts spells in real time during the battle; your main lever on the outcome |
| **Readable auto-battle** | Combat runs automatically from your pre-set positions, target priorities and AI rules; deterministic, no crits or dodges — and you can pause any time |
| **Roguelike run** | A *Slay the Spire*-style map of events, elite guilds and end-of-act boss fights; rewards are Hero Mementos, resources and gold |
| **Co-op for 1–4** | The host shares Vessels across the party; each player runs their own over Steam (host-authoritative NGO) |

---

## Русский

> Кооперативный автобатлер-рогалик для 1–4 игроков. В разработке.

**Guildmaster** — кооперативный автобатлер-рогалик для 1–4 игроков: тактическая глубина (сбор отряда, управление Реликвиями (Hero Mementos), позиционирование) в духе весёлой вечеринки. Вдохновлён *Slay the Spire*, *Baldur's Gate 3*, *Divinity: Original Sin 2* и *Teamfight Manager*.

Вы возглавляете гильдию, готовящуюся к Великому Чемпионату (Grand Championship). Ваш отряд — это «Сосуды» (Vessels): обычные люди, которые сами не умеют сражаться; их сила приходит из **Реликвий (Hero Mementos)**, которые они принимают. Перед каждым боем вы решаете, кто из «Сосудов» какую Реликвию возьмёт, расставляете позиции и приоритеты целей — а дальше бой идёт сам, и вы влияете на него заклинаниями **Гильдмастера (Guildmaster)** в реальном времени.

Игра рассчитана на **1–4 игроков**: в соло вы ведёте всю гильдию, в кооперативе каждый управляет своими «Сосудами».

> **Что демонстрирует проект:** детерминированную симуляцию боя в реальном времени, DI-архитектуру (VContainer + MessagePipe) без синглтонов, host-authoritative кооп-нетворкинг (NGO + Steam) и полностью data-driven контент-пайплайн (ScriptableObjects + Addressables) — с покрытием EditMode/PlayMode-тестами в CI.

### Ключевые механики

| Механика | Описание |
|---|---|
| **«Сосуды» (Vessels) и Реликвии (Hero Mementos)** | «Сосуды» — обычные гильдийцы, что сами не сражаются; принятая **Реликвия** даёт им статы и уникальные способности (как игрок и его чемпион в *LoL*) |
| **Гильдмастер (Guildmaster)** | Не сражается напрямую — кастует заклинания в реальном времени по ходу боя; ваш главный рычаг влияния на исход |
| **Читаемый автобой** | Бой идёт автоматически по заранее заданным позициям, приоритетам целей и AI-правилам; детерминированно, без крита и уклонения — паузу можно ставить в любой момент |
| **Рогалик-забег** | Карта в стиле *Slay the Spire*: события, элитные гильдии и босс-бои в конце акта; награды — Реликвии, ресурсы и золото |
| **Кооп на 1–4** | Хост делит «Сосудов» по отряду; каждый играет за своих по Steam (host-authoritative NGO) |

---

## Screenshots

> Coming soon — the project is in early development. / Скоро — проект в ранней стадии разработки.

---

<details id="for-developers">
<summary><b>For developers</b></summary>

## Tech stack

| | |
|---|---|
| **Engine** | Unity 6 (6000.4.8f1) |
| **Language** | C# |
| **Platform** | Windows / PC |
| **CI/CD** | GitHub Actions + [GameCI](https://game.ci) |
| **Docs** | Quartz v4 + Doxygen + GitHub Pages |

### Architecture & packages

| Category | Package | Purpose |
|---|---|---|
| **DI / Events** | [VContainer](https://github.com/hadashiA/VContainer) | DI container — no singletons |
| | [MessagePipe](https://github.com/Cysharp/MessagePipe) | Typed pub/sub EventBus over DI |
| **Async** | [UniTask](https://github.com/Cysharp/UniTask) | Zero-alloc async/await instead of coroutines |
| **UI** | [UI Toolkit](https://docs.unity3d.com/Manual/UIElements.html) + MVVM | Retained-mode UI (UXML/USS) with View↔ViewModel bindings |
| | [LitMotion](https://github.com/annulusgames/LitMotion) | Zero-alloc tweens for UI and VFX |
| **Multiplayer** | NGO 2.11.2 | Netcode for GameObjects — host-authoritative |
| | [Facepunch.Steamworks](https://github.com/Facepunch/Facepunch.Steamworks) | Steam integration and transport for NGO |
| | MPPM 1.3.2 | In-editor co-op testing (up to 4 virtual players) |
| **Data** | Easy Save 3 | Saves (disk + Steam Cloud) |
| | Newtonsoft.Json | JSON serialization of DTOs |
| | Addressables | Content loading by address |
| | Unity Localization | EN + RU localization |
| **Audio** | FMOD | Adaptive music (behind an `IAudioService` interface) |
| **Tooling** | [Odin Inspector](https://odininspector.com) | Extended inspector, `[SerializeReference]` dropdowns |
| | [Feel (More Mountains)](https://assetstore.unity.com/packages/tools/particles-effects/feel-183370) | Game feel: rumble, camera shake, hitstops |
| | [Shapes (Freya Holmer)](https://acegikmo.com/shapes/) | Procedural vector graphics for UI and debug |
| | [Quantum Console](https://assetstore.unity.com/packages/tools/utilities/quantum-console-211046) | In-game dev console |

## Project layout

```
Guildmaster - Autobattler/
├── Assets/
│   └── _Project/             # All game code and content
│       ├── Scripts/
│       │   ├── Core/         # Guildmaster.Core.asmdef
│       │   ├── Units/        # Guildmaster.Units.asmdef
│       │   ├── Combat/       # Guildmaster.Combat.asmdef
│       │   ├── Guild/        # Guildmaster.Guild.asmdef
│       │   └── UI/           # Guildmaster.UI.asmdef
│       ├── ScriptableObjects/
│       ├── Prefabs/
│       ├── Scenes/
│       └── UI/
├── Assets/Tests/
│   ├── EditMode/             # Unit tests
│   └── PlayMode/             # Integration tests
├── guildmaster-wiki/         # GDD and technical docs (Obsidian Vault)
├── quartz-config/            # Quartz v4 config for the docs site
├── doxygen/                  # Doxygen config for the C# API reference
├── .github/workflows/        # CI: tests (ci.yml) and docs deploy (docs.yml)
├── .cursor/rules/            # Code standards and git conventions
└── scripts/
    └── run-tests.ps1         # Local test runner
```

**Code principles:**
- `ScriptableObject` for all game data — no hardcoding
- `ObjectPool<T>` instead of `Instantiate`/`Destroy` on hot paths
- Component references cached in `Awake()`, never in `Update()`
- Unity Input System (not legacy Input)

## CI/CD

| File | Purpose |
|---|---|
| `.github/workflows/ci.yml` | Unity Test Runner (EditMode + PlayMode) on push/PR |
| `.github/workflows/docs.yml` | Build Quartz + Doxygen → deploy to GitHub Pages |

Run tests locally:
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
