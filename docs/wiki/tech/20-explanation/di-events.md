---
title: "Explanation - DI & Events"
order: 10
status: needs_review
updated: 2026-07-26
---

**Статус:** needs_review — отражает код на 2026-06-19; пример DI-регистрации и контракт ссылок скоупа (`ScopeWiring`) актуализированы 2026-07-26

---

> Как куски кода находят друг друга. Ты просил разобрать DI особенно подробно — поэтому здесь от самых азов («что это и зачем») до наших конкретных скоупов и приёмов VContainer. Во второй половине — события (MessagePipe vs C#-`event`), потому что это вторая половина «проводки».
>
> Связано с [[tech/20-explanation/index|Explanation - Code Map]], [[tech/20-explanation/simulation|Explanation - Simulation & Tick]].

---

# Часть 1. Dependency Injection (VContainer)

## 1.1 Простыми словами

**Проблема.** Классу `CombatSimulation` нужны генератор случайностей, пространственный хэш и десяток систем. Откуда он их берёт? Три варианта:

- **Плохо:** создаёт сам внутри (`new XorShiftRng(...)`). Тогда его не подменить в тесте, и он намертво привязан к конкретным реализациям.
- **Плохо:** берёт из глобального синглтона (`RngService.Instance`). Тогда везде скрытые связи, тестировать ад, порядок инициализации — лотерея.
- **Хорошо (DI):** кто-то снаружи **передаёт** ему готовые зависимости в конструктор. Класс просто объявляет «мне нужно вот это», а сборкой занимается отдельный «сборщик».

**Dependency Injection** = «не создавай свои зависимости сам, попроси их в конструкторе, пусть их даст контейнер». «Контейнер» — это и есть сборщик, который знает, как построить весь граф объектов.

Аналогия: ты не добываешь электричество сам — ты втыкаешь вилку в розетку. DI-контейнер — это электросеть дома: он знает, что куда подключено.

> **Почему мы вообще отказались от синглтонов** (явное правило проекта, [[tech/10-reference/tech-stack|Reference - Tech Stack]]): синглтоны — это глобальные переменные в костюме. Они прячут зависимости (по сигнатуре класса не видно, что он трогает пол-проекта), ломают тесты (нельзя подменить) и создают хаос порядка инициализации. На прошлом проекте это больно укусило — поэтому здесь **зависимости только через инъекцию**.

## 1.2 Наш инструмент — VContainer

[VContainer](https://vcontainer.hadashikick.jp/) — быстрый DI-контейнер для Unity. Ключевые понятия:

- **`LifetimeScope`** — `MonoBehaviour`, который описывает «какие сервисы существуют и сколько живут». Вешается на объект в сцене.
- **`Configure(IContainerBuilder builder)`** — здесь регистрируешь сервисы.
- **`builder.Register<T>()`** — «контейнер умеет делать T». При запросе он сам разрешит конструктор T, подставив зарегистрированные зависимости (**constructor injection**).
- **Lifetime** — время жизни: `Singleton` (один на контейнер), `Scoped` (один на скоуп), `Transient` (новый каждый раз).
- **Дочерние скоупы** — один scope может быть родителем другого. Дочерний видит сервисы родителя, но не наоборот.

## 1.3 Наши два скоупа

Проект использует **три уровня жизни**, и это решение завязано на структуру сцен: стартовая `CoreScene` + две аддитивные persist-сцены, `WorldScene` и `CombatSystemsScene` (карта сцен — [[tech/10-reference/scenes|Scenes]]). Цепочка родителей: `RootLifetimeScope` → `WorldLifetimeScope` → `CombatLifetimeScope`.

### `RootLifetimeScope` — живёт всю сессию

`Assets/_Project/Scripts/Game/RootLifetimeScope.cs`

```csharp
protected override void Configure(IContainerBuilder builder)
{
    builder.Register<IRngService>(_ => new XorShiftRng(GenerateRootSeed()), Lifetime.Singleton);
    builder.Register<FmodAudioService>(Lifetime.Singleton).As<IAudioService>();
    builder.Register<SceneLoader>(Lifetime.Singleton).As<ISceneLoader>().AsSelf();
    builder.Register<GameFlow>(Lifetime.Singleton);

    var options = builder.RegisterMessagePipe();
    builder.RegisterBuildCallback(c => GlobalMessagePipe.SetProvider(c.AsServiceProvider()));
}
```

Здесь сервисы, которые живут **всю игровую сессию**: сессионный RNG, аудио, загрузчик сцен, макро-флоу, шина сообщений. Они переживают вход/выход из боёв. (Фрагмент сокращён — реальный `Configure` регистрирует ещё контент, настройки, UI-слой, персистентность и презентеры потока.)

Обрати внимание на `.As<IAudioService>()`: класс регистрируется, но **выдаётся по интерфейсу**. Игровая логика просит `IAudioService`, не зная, что внутри FMOD. Это прямое следствие правила «FMOD всегда за интерфейсом» из CLAUDE.md. Реализация при этом **одна**: заглушка `UnityAudioService` удалена 2026-07-26 — она никогда не регистрировалась, но читалась как «FMOD в билде, Unity Audio без банков», то есть обещала переключатель, которого нет.

### Ссылки скоупа на ассеты сцены — `ScopeWiring`

`Assets/_Project/Scripts/Game/ScopeWiring.cs`. Каждый скоуп получает конфиги полями из сцены, и на незаполненное поле есть ровно два законных ответа:

| Помощник | Когда | Поведение |
|---|---|---|
| `ScopeWiring.Require(asset, scope, field)` | без ассета контейнер бессмыслен (`_contentDatabase`, `_gameConfig`, `_statsConfig`, `_classBalanceConfig`, `_simTuningConfig`) | исключение с именем скоупа и поля |
| `ScopeWiring.Optional(asset, scope, field, consequence)` | подсистема выключается целиком (`_audioCatalog`, `_actConfig`, `_feelConfig`) | пустой инстанс + `LogError` с последствием («звука не будет вообще») |

До этого половина полей молча падала на пустой инстанс, а половина разыменовывалась в лоб и роняла весь `Configure` голым `NullReferenceException` — одна и та же ошибка автора давала то невидимую деградацию, то падение без диагноза. Настоящий гейт стоит раньше рантайма: `SceneWiringTests` открывает каждую сцену билда и требует эти поля заполненными.

### `WorldLifetimeScope` — живёт всю сессию (персистентный мир)

`Assets/_Project/Scripts/Game/WorldLifetimeScope.cs` — **дочерний** от Root, живёт в `WorldScene`. Держит то, что переживает бои и переиспользуется между ними: единую камеру-риг (`CameraModeController`, `CombatFocusTarget`), снапшот арены (`ArenaLayoutData`), world-слой карты акта и стол за меню.

### `CombatLifetimeScope` — боевые системы

`Assets/_Project/Scripts/Game/CombatLifetimeScope.cs` — **дочерний от World** (`parentReference` в сцене), живёт в `CombatSystemsScene`. Держит RNG боя, боевые системы, симуляцию и презентацию.

> **Осторожно, ловушка чтения.** Скоуп называется «боевым», но по одному бою он НЕ пересоздаётся: сцена грузится один раз на буте и не выгружается, а бой начинается командой в живую симуляцию (`IBattleSession.RequestLaunch`). Значит боевое состояние между узлами **не** очищается само собой сносом скоупа — за сброс отвечает `BattleBootstrap.ResetToWorld`. Прежняя модель «скоуп рождается и умирает вместе с боем» снята вместе с legacy-загрузкой сцены (2026-07-26).

```csharp
protected override void Configure(IContainerBuilder builder)
{
    RegisterRng(builder);              // свой XorShiftRng на бой (суб-сид)
    RegisterCombatSystems(builder);    // SpatialHash + все *System
    RegisterSimulation(builder);       // CombatSimulation, фабрика, loop
    RegisterPresentation(builder);     // презентеры из иерархии сцены
}
```

> **Зачем два скоупа, а не один.** Боевое состояние должно рождаться и умирать **вместе с боем**. Если бы `CombatSimulation` жил в Root как синглтон, второй бой унаследовал бы мусор первого, и пришлось бы вручную всё ресетить. Дочерний скоуп решает это структурно: новый бой = новый скоуп = чистые сервисы. А `IRngService` боя, зарегистрированный в дочернем скоупе, **перекрывает** родительский для всех, кто его просит внутри боя.

## 1.4 Три способа регистрации (и почему каждый)

В коде встречаются три формы — это не случайность, у каждой своя причина:

**(а) По типу — `builder.Register<T>(Lifetime.Scoped)`** — самый чистый. Контейнер сам найдёт конструктор и подставит зависимости.
```csharp
builder.Register<BrainSystem>(Lifetime.Scoped);   // нет хитрых аргументов
```

**(б) С `WithParameter` — для не-инъектируемых аргументов.** `CombatSimulation` берёт `float armorK` — примитив, который контейнер не умеет разрешать. Но остальные 10 зависимостей — умеет. Поэтому:
```csharp
builder.Register<CombatSimulation>(Lifetime.Scoped)
       .WithParameter("armorK", _armorK);   // armorK задаём явно, остальное — авто
```
> 🔧 **Это недавнее упрощение (правка ⑨).** Раньше тут была ручная лямбда `new CombatSimulation(r.Resolve<…>(), …)` на все 10 зависимостей. Минус: добавил систему в конструктор — изволь не забыть дописать `Resolve` в скоупе. `WithParameter` снимает этот бойлерплейт: VContainer сам разрешает граф, а руками задаём только примитив. Именно поэтому добавление `RegenSystem` в конструктор не потребовало правок в скоупе — достаточно было его зарегистрировать.

**(в) Фабричная лямбда — когда нужен контроль над созданием.**
```csharp
builder.Register<SpatialHash>(_ => new SpatialHash(cellSize), Lifetime.Scoped);
```
`SpatialHash` берёт `float cellSize` из инспектора скоупа — лямбда захватывает его в замыкание. Здесь лямбда оправдана (примитив в конструкторе). Сравни с пунктом (б): `RuntimeUnitFactory` пока тоже на лямбде, потому что берёт `StatsConfig` (ассет из `[SerializeField]`).

## 1.5 Entry Points — автозапуск без `MonoBehaviour`

`CombatLoopService` — чистый C#-класс, но он должен **сам стартовать**, когда собрался бой. VContainer даёт `IAsyncStartable`:
```csharp
builder.RegisterEntryPoint<CombatLoopService>(Lifetime.Scoped).AsSelf();
```
Контейнер сам вызовет `StartAsync(CancellationToken)` после построения скоупа, а `CancellationToken` отменится при уничтожении скоупа (конце боя). Так пульс боя живёт без отдельного `MonoBehaviour.Update`. Подробно про сам цикл — [[tech/20-explanation/simulation|Explanation - Simulation & Tick]].

## 1.6 Инъекция в объекты сцены

Презентеры — это `MonoBehaviour` на объектах `CombatSystemsScene`, их нельзя «создать» контейнером, они уже в сцене. Для них:
```csharp
builder.RegisterComponentInHierarchy<CombatPresenter>();
```
Контейнер найдёт компонент в иерархии и **впрыснет** в него зависимости через метод с `[Inject]`:
```csharp
[Inject]
public void Construct(CombatSimulation simulation, IPublisher<DamageDealtEvent> publisher, …) { … }
```
Это **method injection** (в отличие от constructor injection у чистых классов) — потому что Unity создаёт `MonoBehaviour` сама, и конструктор недоступен.

---

# Часть 2. События и развязка

## 2.1 Простыми словами

Когда юнит получает урон, об этом должны узнать многие: HP-бар, спаунер цифр урона, аудио, тряска камеры, может — статистика. Если бы `CombatSimulation` сам дёргал их всех, он бы знал про UI, звук и камеру — то есть «мозг» зависел бы от «тела». Это ровно то, чего мы избегаем.

Решение — **публикация событий**. Мозг кричит в пустоту: «нанесён урон 30 юниту №5». Кто хочет — подписан и реагирует. Мозг не знает, кто слушает и слушает ли вообще.

## 2.2 Два канала — и почему так

В проекте **два механизма событий**, и это не дублирование, а слоёная развязка:

```
CombatSimulation ──C#-event──► CombatPresenter ──MessagePipe──► Audio / VFX / UI
   (мозг, Combat)               (мост, Presentation)            (подписчики)
```

**1) C#-`event Action` внутри `CombatSimulation`** (`OnDamageDealt`, `OnUnitDied`, `OnUnitSpawned`, `OnBattleEnded`).
Почему просто `event`, а не MessagePipe? Потому что `Combat` — нижний слой, он **не должен** зависеть от пакета MessagePipe. Голый `event` — это нулевая зависимость, чистый C#. Мозг остаётся переносимым и тестируемым.

**2) MessagePipe** — pub/sub поверх VContainer (`IPublisher<T>`/`ISubscriber<T>`).
Только **один** класс — `CombatPresenter` — подписан на C#-события сима и **ретранслирует** их в MessagePipe как типизированные сообщения (`DamageDealtEvent`, `UnitDiedEvent`…). Дальше Audio/VFX/UI подписываются на MessagePipe и живут полностью независимо от симуляции.

```csharp
private void HandleDamageDealt(RuntimeUnit source, RuntimeUnit target, DamageResult result)
{
    if (_views.TryGetValue(target.Id, out var view))
        view.OnDamageReceived(result.TotalDamage);     // прямой визуал
    _damageNumbers?.Spawn(target.Position, result.TotalDamage);
    _damageDealtPublisher.Publish(new DamageDealtEvent(source, target, result));  // в шину
}
```

> **Почему «один мост», а не «MessagePipe везде».** Можно было бы публиковать в MessagePipe прямо из симуляции — но тогда `Combat` тянет MessagePipe, и «мозг» перестаёт быть автономным. Один мост в `Presentation` — это шов: всё, что выше моста, развязано через шину; всё, что ниже (сам сим) — чисто. CLAUDE.md говорит «MessagePipe для развязки Combat → UI/Audio/VFX» — на практике развязка реализована именно этим мостом, и это правильно.

## 2.3 Не путать с внутренними боевыми событиями

Есть третий, **отдельный** механизм — `CombatEvent`/`CombatEventData` внутри симуляции (вампиризм, шипы реагируют на «нанесён/получен урон»). Это **не** презентационные события и **не** MessagePipe — это внутренняя FIFO-очередь самой симуляции, с защитой от бесконечной реентрантности. Она разобрана в [[tech/20-explanation/effects-abilities|Explanation - Effects & Abilities]], потому что относится к мозгу, а не к проводке наружу.

| Канал | Где | Для кого | Зачем отдельно |
|---|---|---|---|
| C#-`event` | `CombatSimulation` | `CombatPresenter` | Нулевая зависимость, мозг чист |
| MessagePipe | `Presentation`+ | Audio/VFX/UI | Развязка многих подписчиков |
| `CombatEvent`-очередь | внутри сима | реактивные компоненты эффектов | Детерминизм + кап реентрантности |

---

## Итог документа

- DI = «не создавай зависимости сам, проси в конструкторе»; контейнер собирает граф. Никаких синглтонов.
- Два скоупа: **Root** (сессия) и **Combat** (бой, дочерний) — боевое состояние рождается и умирает с боем.
- Регистрация: по типу (чисто) / `WithParameter` (примитивы) / лямбда (полный контроль). Недавно упростили `CombatSimulation` до `WithParameter`.
- `IAsyncStartable` — автозапуск чистых классов; `RegisterComponentInHierarchy` + `[Inject]`-метод — инъекция в объекты сцены.
- События в два канала: голый `event` держит мозг чистым, MessagePipe развязывает подписчиков; мост между ними — `CombatPresenter`.
