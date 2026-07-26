using System;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Базовый экран навигатора (см. план UI-реворка II.2). Тип (<see cref="Kind"/>) задаёт поведение —
    /// видимость нижних и глушение ввода вычисляет навигатор, а не сам экран. Все экраны — POCO
    /// (тестируемы в EditMode без сцены: <see cref="Build"/> строит голое дерево <see cref="VisualElement"/>).
    /// </summary>
    public abstract class UiScreen
    {
        /// <summary>Поведенческий тип экрана (Page/Modal/Sheet).</summary>
        public abstract ScreenKind Kind { get; }

        /// <summary>
        /// Тег режима карты акта. Помимо подсветки таба топбара определяет контекст ввода: экран с этим
        /// тегом переводит ввод в <c>InputContext.Map</c> (world-камера карты жива) — см. <c>UiNavigator.SyncInput</c>.
        /// </summary>
        public const string MapModeTag = "map";

        /// <summary>Тег режима для подсветки таба топбара: "map"/"inventory"/"battle"/null.</summary>
        public virtual string ModeTag => null;

        /// <summary>
        /// Экран НЕ рисует собственное затемнение, даже будучи нижним видимым Modal. Нужен там, где
        /// модалка ЗАМЕНЯЕТ панель, а не ложится поверх мира: настройки из главного меню — панель ушла,
        /// затемнять нечего.
        /// <para>
        /// Это свойство ЭКРАНА, а не класс на элементе: класс <c>gm-screen--scrimless</c> целиком
        /// принадлежит <c>SyncVisibility</c>, и поставленный руками он тут же перезаписывался обратно —
        /// затемнение возвращалось (наход. Макса). У одного класса должен быть один владелец.
        /// </para>
        /// </summary>
        public virtual bool SuppressScrim => false;

        /// <summary>Корень экрана: строится в <see cref="Build"/> из UXML/кода. Навигатор добавляет/снимает его со слоя.</summary>
        public VisualElement Root { get; protected set; }

        /// <summary>Построить <see cref="Root"/> (клон UXML + проводка). Вызывается навигатором один раз при Push, если Root ещё пуст.</summary>
        public abstract void Build(UiScreenContext ctx);

        /// <summary>
        /// Элемент, получающий фокус при показе экрана (шов под геймпад/Steam Deck, II.9.1). Навигатор ставит
        /// фокус на Push и когда экран становится верхним. По умолчанию null — фокус не трогаем.
        /// </summary>
        public virtual VisualElement GetInitialFocus() => null;

        /// <summary>Экран добавлен в слой (стал частью стека).</summary>
        public virtual void OnEnter() { }

        /// <summary>Экран снят со слоя — единственная точка отписок.</summary>
        public virtual void OnExit() { }

        /// <summary>Экран стал верхним (был перекрыт, стал видимым/активным).</summary>
        public virtual void OnFocus() { }

        /// <summary>Экран перекрыт другим сверху.</summary>
        public virtual void OnBlur() { }

        /// <summary>Навигатор дорезолвит result-экран его дефолтом при снятии без выбора. База — no-op.</summary>
        internal virtual void ResolveDefaultIfPending() { }
    }

    /// <summary>
    /// Экран, возвращающий результат флоу (награда, карта, магазин, исход…). Ровно один резолв — гарантия
    /// НАВИГАТОРА: снятие без явного выбора (ESC/PopAll/отмена) даёт <see cref="DefaultResult"/>, а не
    /// зависший флоу. Заменяет россыпь <c>resolved</c>-guard'ов и <c>DetachFromPanelEvent</c>-страховок.
    /// </summary>
    public abstract class UiScreen<TResult> : UiScreen
    {
        private bool _resolved;
        private Action<TResult> _resolver;

        /// <summary>Значение при снятии без явного выбора (ESC/PopAll/отмена забега): Skip / -1 / null / … .</summary>
        public abstract TResult DefaultResult { get; }

        /// <summary>Навигатор привязывает обработчик резолва (снять экран → отдать результат). Внутренний контракт.</summary>
        internal void BindResolver(Action<TResult> resolver) => _resolver = resolver;

        /// <summary>Резолвнуть флоу. Первый вызов побеждает; повторные игнорируются.</summary>
        protected void Resolve(TResult result)
        {
            if (_resolved) return;
            _resolved = true;
            _resolver?.Invoke(result);
        }

        internal override void ResolveDefaultIfPending()
        {
            if (!_resolved) Resolve(DefaultResult);
        }
    }
}
