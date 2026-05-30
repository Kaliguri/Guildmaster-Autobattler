# Guildmaster — Autobattler

> Кооперативный автобатлер-рогалик в реальном времени (с паузой). В разработке.

<p align="center">
  <img src="https://img.shields.io/badge/Unity-6000.4.8f1-black?logo=unity" alt="Unity"/>
  <img src="https://img.shields.io/badge/Platform-Windows%20%2F%20PC-blue" alt="Platform"/>
  <img src="https://img.shields.io/badge/Status-In%20Development-yellow" alt="Status"/>
  <img src="https://img.shields.io/badge/License-All%20Rights%20Reserved-red" alt="License"/>
  <a href="https://github.com/Kaliguri/Guildmaster-Autobattler/actions/workflows/ci.yml"><img src="https://github.com/Kaliguri/Guildmaster-Autobattler/actions/workflows/ci.yml/badge.svg" alt="CI"/></a>
  <a href="https://kaliguri.github.io/Guildmaster-Autobattler/"><img src="https://img.shields.io/badge/Docs-GitHub%20Pages-blueviolet?logo=github" alt="Docs"/></a>
</p>

<p align="center">
  <a href="https://kaliguri.github.io/Guildmaster-Autobattler/"><b>🌐 Wiki и документация</b></a>
  &nbsp;·&nbsp;
  <a href="https://kaliguri.github.io/Guildmaster-Autobattler/api/"><b>🔧 C# API Reference</b></a>
</p>

---

## Об игре

**Guildmaster** — автобатлер-рогалик с паузой, вдохновлённый *Slay the Spire*, *Across the Obelisk* и *Teamfight Manager*.

Вы возглавляете гильдию авантюристов, готовящихся к великому чемпионату. Перед каждой битвой вы распределяете **осколки героев** между бойцами, расставляете их на поле и настраиваете тактику — а дальше наблюдаете, как они сражаются сами. Влиять на ход боя можно через заклинания Гильдмастера.

Игра поддерживает **1–4 игроков**: в соло вы управляете всей гильдией, в кооперативе — каждый отвечает за своих бойцов.

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

> Coming soon — проект в ранней стадии разработки.

---

<details>
<summary><b>Для разработчиков</b></summary>

## Технический стек

| | |
|---|---|
| **Движок** | Unity 6 (6000.4.8f1) |
| **Язык** | C# |
| **Платформа** | Windows / PC |
| **CI/CD** | GitHub Actions + [GameCI](https://game.ci) |
| **Документация** | Quartz v4 + Doxygen + GitHub Pages |

### Архитектура и пакеты

| Категория | Пакет | Назначение |
|---|---|---|
| **DI / Events** | [VContainer](https://github.com/hadashiA/VContainer) | DI-контейнер — никаких синглтонов |
| | [MessagePipe](https://github.com/Cysharp/MessagePipe) | Типизированный pub/sub EventBus через DI |
| **Async / UI** | [UniTask](https://github.com/Cysharp/UniTask) | Zero-alloc async/await вместо корутин |
| | [LitMotion](https://github.com/annulusgames/LitMotion) | Zero-alloc твины для UI и VFX |
| **Мультиплеер** | NGO 2.11.2 | Netcode for GameObjects — host-authoritative |
| | [Facepunch.Steamworks](https://github.com/Facepunch/Facepunch.Steamworks) | Steam-интеграция и транспорт для NGO |
| | MPPM 1.3.2 | Тест кооп в редакторе (до 4 виртуальных игроков) |
| **Данные** | Easy Save 3 | Сохранения (диск + Steam Cloud) |
| | Newtonsoft.Json | JSON-сериализация DTO |
| | Addressables | Загрузка контента по адресу |
| | Unity Localization | Локализация EN + RU |
| **Аудио** | FMOD | Адаптивная музыка (за интерфейсом `IAudioService`) |
| **Инструменты** | [Odin Inspector](https://odininspector.com) | Расширенный инспектор, `[SerializeReference]`-дропдауны |
| | [Feel (More Mountains)](https://assetstore.unity.com/packages/tools/particles-effects/feel-183370) | Game feel: вибрация, тряска камеры, хитстопы |
| | [Shapes (Freya Holmer)](https://acegikmo.com/shapes/) | Процедурная векторная графика для UI и дебага |
| | [Quantum Console](https://assetstore.unity.com/packages/tools/utilities/quantum-console-211046) | In-game dev-консоль |

### AI-контент (пайплайн)

| Инструмент | Назначение |
|---|---|
| [Suno](https://suno.com) | Генерация музыки |
| [PixelLab](https://www.pixellab.ai) | Пиксельные спрайты и анимация юнитов |
| [ElevenLabs](https://elevenlabs.io) | SFX и голос для трейлера |
| [DeepL](https://deepl.com) | Финальная вычитка локализации EN ↔ RU |

> ⚠️ **Steam Disclosure:** проект использует AI-контент. При публикации обязательно заполнить раздел AI в анкете Steam (требование с 2024 года).

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
