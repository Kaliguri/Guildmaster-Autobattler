using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Core.DevConsole
{
    /// <summary>Вид строки вывода — им консоль выбирает цвет левой кромки.</summary>
    public enum DevLogKind
    {
        /// <summary>Эхо того, что ввёл человек.</summary>
        Echo,

        /// <summary>Ответ команды.</summary>
        Reply,

        /// <summary>Обычный лог игры.</summary>
        Info,

        Warn,
        Error,
    }

    /// <summary>Одна строка вывода.</summary>
    public readonly struct DevLogLine
    {
        public readonly DevLogKind Kind;
        public readonly string Text;

        public DevLogLine(DevLogKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }
    }

    /// <summary>
    /// Хвост вывода консоли: кольцевой буфер последних строк плюс подписка на лог Unity, чтобы
    /// <c>Debug.Log</c> из команды был виден там же, где команду набрали.
    /// </summary>
    /// <remarks>
    /// <b>Кольцо, а не список:</b> консоль живёт всю сессию, и лог боя за десять минут — это десятки тысяч
    /// строк; смысла в них ноль (для разбора есть Console редактора и файл лога), а память и время
    /// перерисовки они съедают линейно. Ёмкость — сколько человек реально пролистывает глазами.
    /// <para><b>Подписка не в конструкторе:</b> объект создаётся через DI на старте, а слушать лог нужно
    /// только пока консоль доступна — <see cref="Attach"/>/<see cref="Detach"/> держит хозяин.</para>
    /// </remarks>
    public sealed class DevConsoleLog
    {
        /// <summary>Сколько строк помнит хвост. 200 — примерно два экрана прокрутки на 1080p.</summary>
        public const int Capacity = 200;

        private readonly Queue<DevLogLine> _lines = new Queue<DevLogLine>(Capacity);
        private bool _attached;

        /// <summary>Строка добавлена — экран дописывает её, не перестраивая весь вывод.</summary>
        public event Action<DevLogLine> Appended;

        /// <summary>Буфер очищен целиком.</summary>
        public event Action Cleared;

        public int Count => _lines.Count;

        /// <summary>Строки от старых к новым.</summary>
        public IEnumerable<DevLogLine> Lines => _lines;

        /// <summary>Начать слушать лог Unity. Повторный вызов ничего не делает.</summary>
        public void Attach()
        {
            if (_attached) return;
            Application.logMessageReceived += OnUnityLog;
            _attached = true;
        }

        /// <summary>Перестать слушать лог Unity.</summary>
        public void Detach()
        {
            if (!_attached) return;
            Application.logMessageReceived -= OnUnityLog;
            _attached = false;
        }

        public void Append(DevLogKind kind, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            while (_lines.Count >= Capacity) _lines.Dequeue();

            var line = new DevLogLine(kind, text);
            _lines.Enqueue(line);
            Appended?.Invoke(line);
        }

        public void Clear()
        {
            _lines.Clear();
            Cleared?.Invoke();
        }

        private void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            // Стек не берём: в консоли он занимает экран и всё равно читается в Console редактора,
            // куда та же запись уже уехала.
            switch (type)
            {
                case LogType.Warning:
                    Append(DevLogKind.Warn, condition);
                    break;
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    Append(DevLogKind.Error, condition);
                    break;
                default:
                    Append(DevLogKind.Info, condition);
                    break;
            }
        }
    }
}
