using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Переиспользуемый drag-and-drop для UITK (один инстанс на экран). Доменно-нейтрален: источник несёт
    /// <c>payload</c> (object) + иконку-ghost, цель принимает payload предикатом и обрабатывает дроп. Ghost едет
    /// за курсором на общем drag-слое поверх экрана; дроп решается по <c>worldBound</c> зарегистрированных целей.
    /// Порог клик-vs-драг: ghost не поднимается, пока курсор не сдвинулся (чистый клик — no-op, без мигания).
    /// <para>
    /// Работает ТОЛЬКО на UITK pointer-событиях с pointer-capture — НЕ на Input System. Поэтому не конфликтует
    /// с world-драгами (напр. расстановка юнитов): у них другой канал ввода, а UITK-оверлей ещё и глушит
    /// геймплейный ввод, пока открыт. Один активный драг за раз (состояние в полях).
    /// </para>
    /// Первый потребитель — лоадаут-хаб (реликвии). Предметы/баннеры/лавка переиспользуют этот же примитив.
    /// </summary>
    public sealed class UiDragDrop
    {
        private const float GhostSize     = 48f;
        private const float DragThreshold = 6f; // панельных ед.: меньше сдвиг = клик, больше = драг

        private readonly VisualElement _root; // слой, на который кладётся ghost (и база координат)
        private readonly List<Target> _targets = new List<Target>();

        // Состояние активного драга (один за раз).
        private VisualElement _source;
        private VisualElement _ghost;
        private object        _payload;
        private Sprite        _icon;
        private int           _pointerId;
        private Vector2       _startPos;
        private bool          _armed;    // pointerdown прошёл — ждём порог
        private bool          _dragging; // порог пройден — ghost поднят, тащим

        private readonly struct Target
        {
            public readonly VisualElement     Element;
            public readonly Func<object, bool> Accepts;
            public readonly Action<object>     OnDrop;
            public Target(VisualElement el, Func<object, bool> accepts, Action<object> onDrop)
            { Element = el; Accepts = accepts; OnDrop = onDrop; }
        }

        public UiDragDrop(VisualElement root) => _root = root;

        /// <summary>Сделать элемент источником драга: тащит <paramref name="payload"/> с иконкой-ghost.</summary>
        public void AddSource(VisualElement source, object payload, Sprite ghostIcon)
        {
            if (source == null || payload == null) return;
            source.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || _armed || _dragging) return;
                _source    = source;
                _pointerId = evt.pointerId;
                _startPos  = evt.position;
                _payload   = payload;
                _icon      = ghostIcon;
                _armed     = true;
                source.CapturePointer(evt.pointerId);
                source.RegisterCallback<PointerMoveEvent>(OnMove);
                source.RegisterCallback<PointerUpEvent>(OnUp);
                evt.StopPropagation();
            });
        }

        /// <summary>Зарегистрировать drop-цель: принимает payload по <paramref name="accepts"/>, обрабатывает дроп.</summary>
        public void AddTarget(VisualElement target, Func<object, bool> accepts, Action<object> onDrop)
        {
            if (target != null && accepts != null && onDrop != null)
                _targets.Add(new Target(target, accepts, onDrop));
        }

        private void OnMove(PointerMoveEvent evt)
        {
            if (!_armed) return;
            Vector2 pos = evt.position;

            if (!_dragging)
            {
                if ((pos - _startPos).sqrMagnitude < DragThreshold * DragThreshold) return; // ещё клик
                SpawnGhost();
                _dragging = true;
            }
            MoveGhost(pos);
        }

        private void OnUp(PointerUpEvent evt)
        {
            Vector2 pos      = evt.position;
            object  payload  = _payload;
            bool    dropping = _dragging;
            Cleanup();
            if (dropping) ResolveDrop(pos, payload);
        }

        private void ResolveDrop(Vector2 pos, object payload)
        {
            if (payload == null) return;
            for (int i = 0; i < _targets.Count; i++)
            {
                Target t = _targets[i];
                if (t.Element != null && t.Element.worldBound.Contains(pos) && t.Accepts(payload))
                {
                    t.OnDrop(payload);
                    return;
                }
            }
        }

        private void SpawnGhost()
        {
            _ghost = new VisualElement { pickingMode = PickingMode.Ignore };
            _ghost.AddToClassList("gm-drag-ghost");
            if (_icon != null) _ghost.style.backgroundImage = new StyleBackground(_icon);
            _root.Add(_ghost);
            MoveGhost(_startPos);
        }

        private void MoveGhost(Vector2 panelPos)
        {
            if (_ghost == null) return;
            Vector2 local = _root.WorldToLocal(panelPos);
            _ghost.style.left = local.x - GhostSize * 0.5f;
            _ghost.style.top  = local.y - GhostSize * 0.5f;
        }

        private void Cleanup()
        {
            if (_source != null)
            {
                if (_source.HasPointerCapture(_pointerId)) _source.ReleasePointer(_pointerId);
                _source.UnregisterCallback<PointerMoveEvent>(OnMove);
                _source.UnregisterCallback<PointerUpEvent>(OnUp);
            }
            if (_ghost != null) { _ghost.RemoveFromHierarchy(); _ghost = null; }
            _source  = null;
            _payload = null;
            _icon    = null;
            _armed   = false;
            _dragging = false;
        }
    }
}
