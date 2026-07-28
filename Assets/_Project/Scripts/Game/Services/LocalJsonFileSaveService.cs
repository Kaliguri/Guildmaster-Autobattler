using Guildmaster.Core.Persistence;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Бэкенд <see cref="ILocalSaveService"/> — данные КОМПЬЮТЕРА, а не игрока: каталог
    /// <c>persistentDataPath/Local</c>, лежащий <b>вне</b> <c>Saves/</c> и потому вне маски Steam Cloud
    /// (ТЗ [[save-system]] §3–4).
    /// <para>Здесь живут разрешение, режим окна и частота обновления. Синхронизировать их между машинами
    /// нельзя: на втором ПК чужое разрешение в лучшем случае неудобно, в худшем — чёрный экран на режиме,
    /// которого этот монитор не умеет.</para>
    /// </summary>
    public sealed class LocalJsonFileSaveService : JsonFileSaveServiceBase, ILocalSaveService
    {
        /// <summary>Корень машинно-локальных данных. В облачную маску не входит — так и задумано.</summary>
        public const string LocalFolder = "Local";

        public LocalJsonFileSaveService() : base(LocalFolder) { }
    }
}
