using System.Collections.Generic;

namespace Guildmaster.UI.Tooltips
{
    /// <summary>
    /// История переходов закреплённого окна (план §II.10.5, слой 3): «назад / вперёд», как в браузере.
    /// </summary>
    /// <remarks>
    /// Отдельный класс, потому что это ЕДИНСТВЕННАЯ часть закрепления, которую можно проверить без
    /// панели и курсора, — а ошибка в ней (застрявшая кнопка «назад», не обрезанная ветка «вперёд»)
    /// выглядит как «подсказка сломалась» и ловится только руками.
    /// <para>История, а не стек окон: граф терминов циклический (Броня → Урон → Броня), и по истории
    /// такой цикл проходится естественно, а стопка окон уходила бы в бесконечность.</para>
    /// </remarks>
    public sealed class TooltipHistory
    {
        private readonly List<TooltipRequest> _entries = new();
        private int _index = -1;

        public int Count => _entries.Count;

        /// <summary>Текущая запись; пустой запрос, если истории нет.</summary>
        public TooltipRequest Current => _index >= 0 && _index < _entries.Count ? _entries[_index] : default;

        public bool CanGoBack => _index > 0;

        public bool CanGoForward => _index >= 0 && _index < _entries.Count - 1;

        /// <summary>Начать историю с этого содержимого (момент закрепления).</summary>
        public void Reset(TooltipRequest request)
        {
            _entries.Clear();
            _index = -1;
            if (request.IsEmpty) return;
            _entries.Add(request);
            _index = 0;
        }

        public void Clear()
        {
            _entries.Clear();
            _index = -1;
        }

        /// <summary>
        /// Перейти к новому содержимому. Возвращает <c>false</c>, если переход не нужен (то же самое).
        /// Ветка «вперёд» обрезается: ушёл в сторону — «вперёд» больше не про то место.
        /// </summary>
        public bool Push(TooltipRequest request)
        {
            if (request.IsEmpty || request.SameAs(Current)) return false;

            if (CanGoForward) _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
            _entries.Add(request);
            _index = _entries.Count - 1;
            return true;
        }

        public bool GoBack()
        {
            if (!CanGoBack) return false;
            _index--;
            return true;
        }

        public bool GoForward()
        {
            if (!CanGoForward) return false;
            _index++;
            return true;
        }
    }
}
