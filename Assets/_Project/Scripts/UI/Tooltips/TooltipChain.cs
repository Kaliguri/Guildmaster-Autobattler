using System.Collections.Generic;

namespace Guildmaster.UI.Tooltips
{
    /// <summary>
    /// Цепочка открытых подсказок (план §II.10.5, sticky-режим): переходя по терминам, игрок
    /// открывает окна одно за другим, но на экране их живёт не больше <see cref="Limit"/>.
    /// </summary>
    /// <remarks>
    /// Ограничение — не техническое, а читательское: четвёртое окно уже не «контекст», а стена.
    /// Самое старое уходит первым (FIFO), потому что интерес игрока движется вперёд по цепочке,
    /// а не назад к первому термину.
    /// <para>Отдельный класс: это единственная часть sticky-режима, проверяемая без панели и курсора,
    /// а «окна не закрываются» или «одно и то же открылось дважды» — ровно тот сорт ошибок, что
    /// иначе находится руками.</para>
    /// </remarks>
    public sealed class TooltipChain<T>
    {
        /// <summary>Сколько окон живёт на экране одновременно (решение Макса, 2026-07-26).</summary>
        public const int Limit = 3;

        private readonly List<T> _items = new();

        public int Count => _items.Count;

        public IReadOnlyList<T> Items => _items;

        /// <summary>Верхнее (самое свежее) окно; <c>default</c>, если цепочка пуста.</summary>
        public T Top => _items.Count > 0 ? _items[_items.Count - 1] : default;

        /// <summary>Самое старое окно — оно уйдёт первым при переполнении.</summary>
        public T Oldest => _items.Count > 0 ? _items[0] : default;

        /// <summary>
        /// Добавить окно. Возвращает вытесненное (самое старое), если цепочка была полна, иначе
        /// <c>default</c> — вызывающий снимает его с панели.
        /// </summary>
        public T Add(T item, out bool evicted)
        {
            evicted = false;
            T removed = default;
            if (_items.Count >= Limit)
            {
                removed = _items[0];
                _items.RemoveAt(0);
                evicted = true;
            }
            _items.Add(item);
            return removed;
        }

        public bool Contains(T item) => _items.Contains(item);

        public bool Remove(T item) => _items.Remove(item);

        /// <summary>Снять всё; вызывающий получает список, чтобы убрать окна с панели.</summary>
        public List<T> DrainAll()
        {
            var copy = new List<T>(_items);
            _items.Clear();
            return copy;
        }
    }
}
