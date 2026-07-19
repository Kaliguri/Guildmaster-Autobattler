using System;
using UnityEngine;

namespace Guildmaster.Core.Input
{
    /// <summary>
    /// Единая точка ввода игрока (вики «16» §2). Весь геймплейный ввод сходится сюда, а не
    /// разбросан по компонентам: реализация оборачивает Input System, потребители (камера,
    /// боевой слой) зависят только от этого интерфейса и Input System не видят.
    /// <para>Соглашение о раздаче: непрерывный ввод (пан/зум) читается значением каждый кадр;
    /// дискретный (нажатия) приходит событиями. Активность карт задаётся <see cref="Context"/>.</para>
    /// </summary>
    public interface IInputService
    {
        /// <summary>Текущий контекст — какие карты действий активны.</summary>
        InputContext Context { get; }

        /// <summary>Переключить контекст: гасит старые карты действий и включает нужные.</summary>
        void SetContext(InputContext context);

        /// <summary>
        /// Глушитель геймплейного ввода поверх активных карт (ортогонально <see cref="Context"/>):
        /// пока <c>true</c> — пан/зум читаются как ноль, а дискретные события (пауза, смена вида) не
        /// поднимаются. Ставит внешний модальный слой, забирающий клавиатуру: dev-консоль QFSW сейчас,
        /// модальное меню — в будущем. Нужен, чтобы набор текста в консоли не протекал в геймплей.
        /// </summary>
        bool GameplaySuppressed { get; set; }

        // --- Камера: непрерывный ввод (поллинг каждый кадр) ---

        /// <summary>Панорамирование камеры: WASD + стрелки, нормализовано в [-1..1] по осям.</summary>
        Vector2 CameraPan { get; }

        /// <summary>Дельта зума за кадр (колесо мыши): &gt;0 — приблизить, &lt;0 — отдалить.</summary>
        float CameraZoomDelta { get; }

        /// <summary>
        /// Пан камеры перетаскиванием СРЕДНЕЙ кнопкой мыши (MMB): дельта мыши за кадр в пикселях, пока MMB
        /// зажата; иначе <see cref="Vector2.zero"/>. Ноль при <see cref="GameplaySuppressed"/>. Потребитель
        /// (камера) сам переводит пиксели в мир по текущему зуму.
        /// </summary>
        Vector2 CameraPanDrag { get; }

        /// <summary>
        /// Курсор над непрозрачной UITK-панелью (hit-тест <c>panel.Pick</c>). Шов развязки UI↔мир: над
        /// панелью (карточки/детали инвентаря) ввод идёт в UI, над «дыркой» (прозрачная боевая зона) — в мир.
        /// Потребители геймплейного пикинга/пана проверяют этот флаг, чтобы клик по панели не протекал в игру.
        /// </summary>
        bool PointerOverUI { get; }

        // --- Камера: дискретные события ---

        /// <summary>Циклическое переключение вида камеры (Tab): Action → Overview → [Dev].</summary>
        event Action CycleViewRequested;

        // --- Расстановка: указатель (мышь) — активен в контексте Deployment ---

        /// <summary>Позиция указателя на экране (пиксели), для screen→world при пикинге/drag. Поллинг каждый кадр.</summary>
        Vector2 PointerScreenPosition { get; }

        /// <summary>Указатель зажат (ЛКМ) — для протяжки. Поллинг. Ноль/false при <see cref="GameplaySuppressed"/>.</summary>
        bool PointerHeld { get; }

        /// <summary>ЛКМ нажата в этом кадре (начало клика/drag/дабл-клика). Не поднимается при <see cref="GameplaySuppressed"/>.</summary>
        event Action PointerPressed;

        /// <summary>ЛКМ отпущена в этом кадре (drop/конец клика).</summary>
        event Action PointerReleased;

        // --- Бой: дискретные события ---

        /// <summary>Переключить паузу боя (Space): пауза ↔ продолжить.</summary>
        event Action PauseToggleRequested;

        /// <summary>Циклически сменить скорость боя (.): 1x → 2x → 3x → 1x.</summary>
        event Action GameSpeedCycleRequested;

        // --- Системное меню: дискретное событие ---

        /// <summary>
        /// Открыть/закрыть системное меню (Escape). Это ОВЕРЛЕЙ, НЕ пауза (в хост-авторитативном
        /// коопе мир одному игроку не остановить; боевая пауза — отдельно на Space). Доступно из любого
        /// контекста и НЕ глушится <see cref="GameplaySuppressed"/> — иначе открытое меню нельзя закрыть.
        /// </summary>
        event Action MenuToggleRequested;
    }
}
