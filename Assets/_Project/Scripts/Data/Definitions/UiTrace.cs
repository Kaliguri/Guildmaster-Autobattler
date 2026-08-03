using UnityEngine;

namespace Guildmaster.Diagnostics
{
    /// <summary>
    /// ВРЕМЕННЫЙ трейсер переключений UI-режимов (карта/бой/инвентарь/тест-зона) — для отладки рассинхрона
    /// тумблеров со стеком навигатора (Макс собирает череду логов, по ним ищем корень). Живёт в Data-сборке,
    /// поэтому доступен из Game/UI/Presentation одинаково.
    /// <para><b>ОТКЛЮЧИТЬ после отладки:</b> <see cref="Enabled"/> = false (логи НЕ удалять — могут ещё
    /// понадобиться при следующем разборе переключений).</para>
    /// </summary>
    public static class UiTrace
    {
        /// <summary>Мастер-тумблер трейса. true = логировать переключения режимов (для разбора рассинхрона).
        /// Выключен по умолчанию — включать точечно при отладке табов/стека, не удаляя вызовы UiTrace.Log.</summary>
        public static bool Enabled = false;

        public static void Log(string message)
        {
            if (Enabled) Debug.Log("[UITRACE] " + message);
        }
    }
}
