using System;
using System.Collections.Generic;
using Guildmaster.Core.Flow;
using UnityEngine.UIElements;

namespace Guildmaster.UI
{
    /// <summary>
    /// Лента: сообщения, которые ничего не спрашивают. Стопка в правом нижнем углу, над журналом.
    /// </summary>
    /// <remarks>
    /// <b>Второй облик одной модели</b> (решение Макса 20.08.2026). Заказ у ленты и у окна ОДИН —
    /// <see cref="NoticeRequest"/>, — а облик выбирается по наличию ответов: спрашивать не о чем —
    /// лента, есть варианты — окно со scrim. Выбор сделан моделью, а не вызывающим кодом, иначе
    /// «нет слота под мементо» рано или поздно приедет модалкой: важность каждый заказчик оценивает
    /// по-своему.
    ///
    /// <para><b>Место — правый нижний угол, над журналом событий</b> (прямое решение Макса при моём
    /// возражении, что лента сядет на свежие строки журнала): «Обычно лента НЕ будет появляться в
    /// бою. А если и будет — все ок, так задумано». Уведомления приходят в основном вне боя, где
    /// журнал молчит.</para>
    ///
    /// <para><b>Уходит по времени, а не по кнопке.</b> Правило «окно закрывается только кнопкой»
    /// сужено 20.08.2026 до окон с решением: кнопка «Понятно» у каждого «Сохранено» была бы пыткой.
    /// Клик по ленте закрывает её досрочно.</para>
    /// </remarks>
    public sealed class ToastLayerView
    {
        /// <summary>Сколько лент живёт на экране разом. Четвёртая вытесняет самую старую.</summary>
        private const int MaxVisible = 3;

        /// <summary>База жизни строки и добавка на символ: длинный текст нужно успеть прочитать.</summary>
        private const long BaseMs = 3000;
        private const long PerCharMs = 40;
        private const long MaxMs = 8000;

        private readonly List<Entry> _live = new();
        private VisualElement _root;

        /// <summary>Одна живая лента: её элемент, текст-ключ для склейки и счётчик повторов.</summary>
        private sealed class Entry
        {
            public VisualElement Element;
            public Label Text;
            public string Key;
            public int Count;
            public IVisualElementScheduledItem Timer;
        }

        /// <summary>Взять место в слое системных наложений. До этого показывать некуда.</summary>
        public void Attach(VisualElement layer)
        {
            if (layer == null) return;

            _root = new VisualElement { name = "toasts", pickingMode = PickingMode.Ignore };
            _root.AddToClassList("gm-toasts");
            layer.Add(_root);
        }

        /// <summary>Подходит ли заказ ленте, или его должно показать окно.</summary>
        /// <remarks>
        /// Два условия, и оба механические. Есть ответы — игра ждёт решения, значит окно. Вид
        /// <see cref="NoticeKind.Error"/> — окно даже без ответов: «сейв не записался» не имеет права
        /// промелькнуть лентой, пока игрок смотрел в другую сторону.
        /// </remarks>
        public static bool Suits(in NoticeRequest request)
            => request.Kind != NoticeKind.Error
               && (request.Options == null || request.Options.Count == 0);

        /// <summary>Показать ленту. Повтор того же текста поднимает счётчик, а не плодит строки.</summary>
        public void Show(in NoticeRequest request, Func<string, string> localize)
        {
            if (_root == null) return;

            string text = Text(localize, request.BodyKey, request.BodyFallback);
            if (string.IsNullOrWhiteSpace(text)) text = Text(localize, request.TitleKey, request.TitleFallback);
            if (string.IsNullOrWhiteSpace(text)) return;

            Entry same = _live.Find(e => e.Key == text);
            if (same != null)
            {
                same.Count++;
                same.Text.text = $"{text}  ×{same.Count}";
                Restart(same, text);
                return;
            }

            // Самая старая уходит СРАЗУ, без угасания: место нужно новой, а очередь из четырёх лент
            // читается как заваленный угол, а не как поток новостей.
            while (_live.Count >= MaxVisible) Remove(_live[0]);

            var entry = new Entry { Key = text, Count = 1 };

            entry.Element = new VisualElement { pickingMode = PickingMode.Position };
            entry.Element.AddToClassList("gm-toast");
            entry.Element.AddToClassList(ModifierFor(request.Kind));

            entry.Text = new Label(text);
            entry.Text.AddToClassList("gm-text-body");
            entry.Text.AddToClassList("gm-toast__text");
            entry.Element.Add(entry.Text);

            entry.Element.RegisterCallback<PointerDownEvent>(_ => Remove(entry));

            _root.Add(entry.Element);
            _live.Add(entry);
            Restart(entry, text);
        }

        /// <summary>Перезавести таймер: у повторившейся ленты жизнь начинается заново.</summary>
        private void Restart(Entry entry, string text)
        {
            entry.Timer?.Pause();
            long life = BaseMs + text.Length * PerCharMs;
            if (life > MaxMs) life = MaxMs;
            entry.Timer = entry.Element.schedule.Execute(() => Remove(entry)).StartingIn(life);
        }

        private void Remove(Entry entry)
        {
            if (entry == null) return;

            entry.Timer?.Pause();
            entry.Element?.RemoveFromHierarchy();
            _live.Remove(entry);
        }

        /// <summary>Снять всё разом — при смене сцены прошлые новости уже не про эту игру.</summary>
        public void Clear()
        {
            for (int i = _live.Count - 1; i >= 0; i--) Remove(_live[i]);
        }

        private static string ModifierFor(NoticeKind kind) => kind switch
        {
            NoticeKind.Warning => "gm-toast--warning",
            NoticeKind.Error   => "gm-toast--error",
            _                  => "gm-toast--info",
        };

        private static string Text(Func<string, string> localize, string key, string fallback)
        {
            if (!string.IsNullOrEmpty(key))
            {
                string translated = localize?.Invoke(key);
                if (!string.IsNullOrEmpty(translated)) return translated;
            }

            return fallback;
        }
    }
}
