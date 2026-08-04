namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Заказ на воспроизведение повтора: байты файла, которые реплей-скоуп отдаст
    /// <see cref="ReplayFilePlayer"/>. Регистрируется создателем скоупа (директор фона меню) через
    /// установщик, как <c>BattleScopeParams</c> — и так же резолвится лениво, уже после установки.
    /// </summary>
    /// <remarks>
    /// Байты, а не путь: источник может быть и файлом на диске, и <c>StreamingAssets</c> (который на
    /// части платформ читается только через <c>UnityWebRequest</c>), и памятью. Скоупу всё равно,
    /// откуда они — читать умеет тот, кто заказывает.
    /// </remarks>
    public sealed class ReplayPlaybackRequest
    {
        public readonly byte[] FileBytes;

        public ReplayPlaybackRequest(byte[] fileBytes) => FileBytes = fileBytes;
    }
}
