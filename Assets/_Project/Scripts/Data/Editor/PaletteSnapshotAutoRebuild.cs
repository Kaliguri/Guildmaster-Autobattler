using UnityEditor;

namespace Guildmaster.Data.Editor
{
    /// <summary>
    /// Пересобирает снимок палитры САМ, как только тронули ярусы токенов. Ручной пункт меню остаётся,
    /// но нажимать его больше не нужно ни человеку, ни агенту.
    /// </summary>
    /// <remarks>
    /// <b>Зачем.</b> Между USS и миром стоит снимок (<see cref="PaletteSnapshotBuilder"/>): карта, боевые
    /// VFX и перекрасчик спрайтов рисуются мимо UI Toolkit и читать USS не умеют. Шов законный, а вот
    /// РУЧНАЯ пересборка — нет: состояние «снимок отстал от токенов» держалось на памяти того, кто
    /// правил цвет, и ловилось только красным <c>PaletteSnapshotTests</c> постфактум. Это и есть
    /// определение костыля: правило, живущее в голове, при том что его умеет исполнять машина.
    /// Теперь отстать негде — импорт токенов и есть сигнал к пересборке.
    ///
    /// <para><b>Рекурсии нет:</b> следим за <c>.uss</c>, пишем <c>.asset</c>. Импорт результата второй
    /// раз этот обработчик не поднимает.</para>
    ///
    /// <para><b>В play mode не трогаем.</b> Запись ассета посреди чужой игровой сессии — это внезапный
    /// <c>SaveAssets</c> под руками у того, кто в этот момент играет. Правка темы во время игры и так
    /// случай редкий, а после выхода из play mode импорт темы отработает заново.</para>
    /// </remarks>
    internal sealed class PaletteSnapshotAutoRebuild : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!TouchesTokens(imported) && !TouchesTokens(moved)) return;

            PaletteSnapshotBuilder.Rebuild();
        }

        private static bool TouchesTokens(string[] paths)
        {
            if (paths == null) return false;

            for (int i = 0; i < paths.Length; i++)
            {
                string p = paths[i];
                if (p == PaletteSnapshotBuilder.PrimitivesPath || p == PaletteSnapshotBuilder.SemanticPath)
                    return true;
            }

            return false;
        }
    }
}
