using System;
using UnityEngine;

namespace Guildmaster.Core.Diagnostics
{
    /// <summary>Что именно логируем. Флаги — включать можно несколько сразу.</summary>
    [Flags]
    public enum DiagChannel
    {
        None = 0,

        /// <summary>Сеанс и транспорт: подключения, рукопожатие, разрывы.</summary>
        Session = 1 << 0,

        /// <summary>Лента боя: чанки, догрузка, дыры, кадры покоя.</summary>
        Tape = 1 << 1,

        /// <summary>«Где мы» и следование гостя: мероприятие, фаза, карта, двор.</summary>
        Follow = 1 << 2,

        /// <summary>Команды забега: что ушло хосту и что он применил.</summary>
        Commands = 1 << 3,

        /// <summary>Общее согласие: ключи, счёт, чьё «готов».</summary>
        Ready = 1 << 4,

        /// <summary>Всё сетевое сразу — то, что нужно для разбора кооп-прогона.</summary>
        Net = Session | Tape | Follow | Commands | Ready,
    }

    /// <summary>
    /// Диагностический лог по каналам: включается В ИГРЕ, а не правкой кода.
    /// </summary>
    /// <remarks>
    /// <b>Заведён потому, что кооп нельзя отладить у себя.</b> Он ломается на двух машинах, у живого
    /// Steam и чужого канала; единственное, что оттуда можно принести, — лог. А прежний трейсер
    /// (<c>UiTrace</c>) включался полем в исходнике: чтобы посмотреть прогон, нужно было пересобрать
    /// билд и заново позвать второго человека (просьба Макса 04.08.2026 — «добавить дебаг логи
    /// (отключаемые и включаемые легко) для отладки сетевой части»).
    /// <para><b>Каналы, а не один тумблер:</b> сетевой прогон и без того шумный, а «включить всё»
    /// превращает лог в стену, где нужное не найти. Включённые каналы переживают перезапуск — их
    /// помнит машинно-локальное хранилище, как и разрешение экрана.</para>
    /// <para><b>Строка собирается ТОЛЬКО если канал включён.</b> Поэтому здесь интерполяция запрещена
    /// на входе: <c>Diag.Log(ch, $"...")</c> собрал бы строку даже выключенным. Для дорогих сообщений
    /// есть перегрузка с фабрикой.</para>
    /// </remarks>
    public static class Diag
    {
        /// <summary>Какие каналы сейчас пишут. Меняется командой консоли и хранится между запусками.</summary>
        public static DiagChannel Enabled { get; private set; } = DiagChannel.None;

        /// <summary>Кто-то тронул набор каналов — хранилищу пора записать новое значение.</summary>
        public static event Action<DiagChannel> Changed;

        public static bool IsOn(DiagChannel channel) => (Enabled & channel) != 0;

        /// <summary>Включить или выключить канал (или сразу набор).</summary>
        public static void Set(DiagChannel channel, bool on)
        {
            DiagChannel next = on ? Enabled | channel : Enabled & ~channel;
            if (next == Enabled) return;

            Enabled = next;
            Changed?.Invoke(Enabled);
        }

        /// <summary>Задать набор целиком — так его восстанавливает хранилище на старте.</summary>
        public static void Restore(DiagChannel channels)
        {
            if (channels == Enabled) return;

            Enabled = channels;
            Changed?.Invoke(Enabled);
        }

        public static void Log(DiagChannel channel, string message)
        {
            if (!IsOn(channel)) return;
            Debug.Log($"[{channel}] {message}");
        }

        /// <summary>
        /// Для сообщений, которые дорого собирать. Строка не строится, пока канал выключен.
        /// </summary>
        public static void Log(DiagChannel channel, Func<string> message)
        {
            if (!IsOn(channel) || message == null) return;
            Debug.Log($"[{channel}] {message()}");
        }
    }
}
