# Guildmaster — Autobattler

> Пошаговый автобатлер-рогалик с кооперативным управлением гильдией. В разработке.

![Unity](https://img.shields.io/badge/Unity-6000.4.8f1-black?logo=unity) ![Platform](https://img.shields.io/badge/Platform-Windows%20%2F%20PC-blue) ![Status](https://img.shields.io/badge/Status-In%20Development-yellow) ![License](https://img.shields.io/badge/License-All%20Rights%20Reserved-red) [![CI](https://github.com/Kaliguri/Guildmaster-Autobattler/actions/workflows/ci.yml/badge.svg)](https://github.com/Kaliguri/Guildmaster-Autobattler/actions/workflows/ci.yml) [![Docs](https://img.shields.io/badge/Docs-GitHub%20Pages-blueviolet?logo=github)](https://kaliguri.github.io/Guildmaster-Autobattler/)

<p align="center">
  <a href="https://kaliguri.github.io/Guildmaster-Autobattler/"><b>🌐 Wiki и документация</b></a>
  &nbsp;&nbsp;·&nbsp;&nbsp;
  <a href="https://kaliguri.github.io/Guildmaster-Autobattler/api/"><b>🔧 C# API Reference</b></a>
</p>

---

## Об игре

**Guildmaster** — пошаговый автобатлер с элементами рогалика, вдохновлённый *Slay the Spire*, *Across the Obelisk* и *Teamfight Manager*.

Вы возглавляете гильдию авантюристов, готовящихся к великому чемпионату. Перед каждой битвой вы распределяете осколки героев между бойцами, расставляете их на поле и настраиваете тактику — а дальше смотрите, как они сражаются сами. Управление исходом боя осуществляется через заклинания Гильдмастера.

Игра поддерживает **1–4 игроков**: соло — и вы управляете всей гильдией, в кооперативе — каждый отвечает за своих бойцов.

### Ключевые механики

- **Осколки героев** — экипируемые артефакты, превращающие обычного гильдийца в бойца с уникальными способностями
- **Гильдмастер** — не сражается, но кастует заклинания во время боя; его слоты ограничены и перезаряжаются между сражениями
- **Автобой** — бой идёт сам, без микроменеджмента атак
- **Карта в стиле STS** — ивенты, магазины, тренировки, боссы в конце акта
- **Кооператив** — от 1 до 4 игроков делят гильдийцев между собой

---

## Screenshots

> Coming soon — проект в ранней стадии разработки.

---

<details>
<summary>Для разработчиков</summary>

## Технический стек

| | |
|---|---|
| **Движок** | Unity 6 (6000.4.8f1) |
| **Язык** | C# |
| **Платформа** | Windows / PC |
| **CI/CD** | GitHub Actions + [GameCI](https://game.ci) |
| **Docs** | Quartz v4 + Doxygen + GitHub Pages |

**Пакеты и плагины:**

| Пакет | Назначение |
|---|---|
| [VContainer](https://github.com/hadashiA/VContainer) | DI-контейнер — замена синглтонам |
| [UniTask](https://github.com/Cysharp/UniTask) | Zero-alloc async/await |
| [MessagePipe](https://github.com/Cysharp/MessagePipe) | Типизированный pub/sub EventBus через DI |
| [LitMotion](https://github.com/annulusgames/LitMotion) | Zero-alloc твины для UI и VFX |
| [Odin Inspector](https://odininspector.com) | Расширенный инспектор, `[SerializeReference]`-дропдауны |
| [Easy Save 3](https://assetstore.unity.com/packages/tools/utilities/easy-save-the-complete-save-data-serializer-system-768) | Сохранения (диск + Steam Cloud) |
| Newtonsoft.Json | JSON-сериализация DTO сейвов |
| Addressables | Загрузка контента по адресу |
| Unity Localization | Локализация EN + RU |
| MPPM | Тест кооп в редакторе (до 4 виртуальных игроков) |
| NGO (Netcode for GameObjects) | Сетевой слой (host-authoritative) |
| [Facepunch.Steamworks](https://github.com/Facepunch/Facepunch.Steamworks) | Steam-интеграция и транспорт для NGO |
| FMOD | Адаптивная музыка (за интерфейсом `IAudioService`) |
| [Feel (More Mountains)](https://assetstore.unity.com/packages/tools/particles-effects/feel-183370) | Game feel: вибрация, тряска камеры, эффекты |
| [Shapes (Freya Holmer)](https://acegikmo.com/shapes/) | Процедурная векторная графика для UI и дебага |
| [Quantum Console](https://assetstore.unity.com/packages/tools/utilities/quantum-console-211046) | In-game dev-консоль |

**AI-инструменты (контент-пайплайн):**

| Инструмент | Назначение | Лицензия |
|---|---|---|
| [Suno](https://suno.com) | Генерация музыки (платный тир) | Коммерческое использование разрешено |
| [PixelLab](https://www.pixellab.ai) | Пиксельные спрайты, анимация юнитов | Уточнить тир |
| [ElevenLabs](https://elevenlabs.io) | Генерация SFX и голоса для трейлера | Уточнить тир |
| [DeepL](https://deepl.com) | Финальная вычитка локализации EN ↔ RU | — |

> ⚠️ **Steam Disclosure:** проект использует AI-контент. При публикации в Steam обязательно заполнить раздел AI-контента в анкете (требование платформы с 2024 года).

## Архитектура проекта

```
Guildmaster - Autobattler/
├── Assets/
│   └── _Project/             # Весь игровой код и контент
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
│   ├── EditMode/             # Юнит-тесты
│   └── PlayMode/             # Интеграционные тесты
├── guildmaster-wiki/         # GDD и техническая документация (Obsidian Vault)
├── quartz-config/            # Конфиг Quartz v4 для docs сайта
├── doxygen/                  # Конфиг Doxygen для C# API Reference
├── .github/workflows/        # CI: тесты (ci.yml) и docs деплой (docs.yml)
├── .cursor/rules/            # Стандарты кода и git-конвенции для AI-агента
└── scripts/
    └── run-tests.ps1         # Локальный запуск тестов
```

**Принципы кода:**
- `ScriptableObject` для всех игровых данных — никакого хардкода
- `ObjectPool<T>` вместо `Instantiate`/`Destroy` в горячих путях
- Компонентные ссылки кэшируются в `Awake()`, никогда в `Update()`
- Unity Input System (не Legacy Input)

## CI/CD

| Файл | Назначение |
|---|---|
| `.github/workflows/ci.yml` | Unity Test Runner (EditMode + PlayMode) при push/PR |
| `.github/workflows/docs.yml` | Сборка Quartz + Doxygen → деплой на GitHub Pages |

Локальный запуск тестов:
```powershell
./scripts/run-tests.ps1
```

</details>

---

## Лицензия

**© 2026 Max Gaida. Все права защищены.**

Проект опубликован в открытом доступе исключительно в целях демонстрации в портфолио.  
Использование, копирование, распространение или создание производных работ **без письменного разрешения автора запрещено**.
