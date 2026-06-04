<p>
  <a href="#english"><img src="https://img.shields.io/badge/English-30363d?style=for-the-badge" alt="English"></a>
  &nbsp;
  <a href="#русский"><img src="https://img.shields.io/badge/Русский-57606a?style=for-the-badge" alt="Русский"></a>
</p>

# Guildmaster — Autobattler

> Co-op real-time-with-pause autobattler roguelike. In development.

<p>
  <img src="https://img.shields.io/badge/Unity-6000.4.8f1-black?logo=unity" alt="Unity"/>
  <img src="https://img.shields.io/badge/Platform-Windows%20%2F%20PC-blue" alt="Platform"/>
  <img src="https://img.shields.io/badge/Status-In%20Development-yellow" alt="Status"/>
  <img src="https://img.shields.io/badge/License-All%20Rights%20Reserved-red" alt="License"/>
  <a href="https://github.com/Kaliguri/Guildmaster-Autobattler/actions/workflows/ci.yml"><img src="https://github.com/Kaliguri/Guildmaster-Autobattler/actions/workflows/ci.yml/badge.svg" alt="CI"/></a>
  <a href="https://kaliguri.github.io/Guildmaster-Autobattler/"><img src="https://img.shields.io/badge/Docs-GitHub%20Pages-blueviolet?logo=github" alt="Docs"/></a>
</p>

<p><sub><b>Architecture</b></sub><br>
  <img alt="DI" src="https://img.shields.io/badge/DI-8957e5?style=flat-square"/>
  <img alt="MVVM" src="https://img.shields.io/badge/MVVM-8957e5?style=flat-square"/>
  <img alt="EventBus" src="https://img.shields.io/badge/EventBus-8957e5?style=flat-square"/>
  <a href="https://github.com/hadashiA/VContainer"><img alt="VContainer" src="https://img.shields.io/badge/VContainer-1f6feb?style=flat-square"/></a>
  <a href="https://github.com/Cysharp/MessagePipe"><img alt="MessagePipe" src="https://img.shields.io/badge/MessagePipe-1f6feb?style=flat-square"/></a>
</p>
<p><sub><b>Async / UI</b></sub><br>
  <a href="https://github.com/Cysharp/UniTask"><img alt="UniTask" src="https://img.shields.io/badge/UniTask-1f6feb?style=flat-square"/></a>
  <a href="https://github.com/annulusgames/LitMotion"><img alt="LitMotion" src="https://img.shields.io/badge/LitMotion-1f6feb?style=flat-square"/></a>
  <a href="https://docs.unity3d.com/Manual/UIElements.html"><img alt="UI Toolkit" src="https://img.shields.io/badge/UI_Toolkit-222222?style=flat-square&logo=unity&logoColor=white"/></a>
</p>
<p><sub><b>Multiplayer</b></sub><br>
  <a href="https://docs-multiplayer.unity3d.com/"><img alt="NGO" src="https://img.shields.io/badge/NGO-222222?style=flat-square&logo=unity&logoColor=white"/></a>
  <a href="https://github.com/Facepunch/Facepunch.Steamworks"><img alt="Facepunch.Steamworks" src="https://img.shields.io/badge/Facepunch.Steamworks-171a21?style=flat-square&logo=steam&logoColor=white"/></a>
</p>
<p><sub><b>Content</b></sub><br>
  <a href="https://docs.unity3d.com/Packages/com.unity.addressables@latest"><img alt="Addressables" src="https://img.shields.io/badge/Addressables-222222?style=flat-square&logo=unity&logoColor=white"/></a>
  <a href="https://docs.unity3d.com/Packages/com.unity.localization@latest"><img alt="Unity Localization" src="https://img.shields.io/badge/Unity_Localization-222222?style=flat-square&logo=unity&logoColor=white"/></a>
</p>
<p><sub><b>Audio</b></sub><br>
  <a href="https://www.fmod.com"><img alt="FMOD" src="https://img.shields.io/badge/FMOD-000000?style=flat-square&logo=fmod&logoColor=white"/></a>
</p>

<p>
  <a href="https://kaliguri.github.io/Guildmaster-Autobattler/"><b>🌐 Wiki & docs</b></a>
  &nbsp;·&nbsp;
  <a href="https://kaliguri.github.io/Guildmaster-Autobattler/api/"><b>🔧 C# API reference</b></a>
</p>

---

<!-- Trailer / gameplay GIF goes here — the visual hook. -->
<p><i>🎬 Trailer &amp; gameplay GIF — coming soon.</i></p>

---

## English

**Guildmaster** is a real-time-with-pause autobattler roguelike inspired by *Slay the Spire*, *Across the Obelisk* and *Teamfight Manager*.

You lead a guild of adventurers preparing for a grand championship. Before each battle you slot **hero shards** into your fighters, position them on the field and tune their tactics — then watch them fight on their own, swaying the outcome with the Guildmaster's spells.

Built for **1–4 players**: solo, you command the whole guild; in co-op, each player runs their own fighters.

> **What this project demonstrates:** a deterministic real-time combat simulation, a DI-driven architecture (VContainer + MessagePipe) with no singletons, host-authoritative co-op netcode (NGO + Steam), and a fully data-driven content pipeline (ScriptableObjects + Addressables) — covered by an EditMode/PlayMode test suite in CI.

### Key features

| Feature | Description |
|---|---|
| **Hero shards** | Equippable artifacts that turn a rank-and-file guild member into a fighter with unique abilities |
| **Guildmaster** | Doesn't fight directly — casts spells mid-battle; spell slots are limited and refresh between fights |
| **Real-time autobattle** | Combat runs in real time with no attack micro; tactical pause is available at any moment |
| **Roguelike map** | Events, shops, training and end-of-act bosses, *Slay the Spire*-style |
| **Co-op** | 1–4 players split the guild over Steam via host-authoritative NGO |

---

## Русский

> Кооперативный автобатлер-рогалик в реальном времени (с паузой). В разработке.

**Guildmaster** — автобатлер-рогалик с паузой, вдохновлённый *Slay the Spire*, *Across the Obelisk* и *Teamfight Manager*.

Вы возглавляете гильдию авантюристов, готовящихся к великому чемпионату. Перед каждой битвой вы распределяете **осколки героев** между бойцами, расставляете их на поле и настраиваете тактику — а дальше наблюдаете, как они сражаются сами. Влиять на ход боя можно через заклинания Гильдмастера.

Игра поддерживает **1–4 игроков**: в соло вы управляете всей гильдией, в кооперативе — каждый отвечает за своих бойцов.

> **Что демонстрирует проект:** детерминированную симуляцию боя в реальном времени, DI-архитектуру (VContainer + MessagePipe) без синглтонов, host-authoritative кооп-нетворкинг (NGO + Steam) и полностью data-driven контент-пайплайн (ScriptableObjects + Addressables) — с покрытием EditMode/PlayMode-тестами в CI.

### Ключевые механики

| Механика | Описание |
|---|---|
| **Осколки героев** | Экипируемые артефакты, превращающие рядового гильдийца в бойца с уникальными способностями |
| **Гильдмастер** | Не сражается напрямую — кастует заклинания во время боя; слоты ограничены и восстанавливаются между схватками |
| **Автобой** | Бой идёт в реальном времени без микроменеджмента атак; тактическая пауза доступна в любой момент |
| **Карта рогалика** | Ивенты, магазины, тренировки и боссы в конце акта — в стиле *Slay the Spire* |
| **Кооператив** | От 1 до 4 игроков делят гильдийцев по Steam через host-authoritative NGO |

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
