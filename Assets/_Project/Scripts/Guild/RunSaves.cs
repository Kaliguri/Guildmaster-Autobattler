using Guildmaster.Core.Persistence;

namespace Guildmaster.Guild
{
    /// <summary>
    /// Где лежит забег и есть ли он. Единственный владелец этого знания — его спрашивают двое, из
    /// разных жизней, и разойтись им нельзя.
    /// </summary>
    /// <remarks>
    /// Главное меню живёт ВНЕ сессии (сессия рождается при входе в игру, вместе с ролью), а держатель
    /// состояния — внутри. Значит вопрос «показывать ли Продолжить» задаётся тогда, когда спрашивать
    /// уже некого. Ответ на него — про диск и активную гильдию, а не про состояние в памяти, поэтому
    /// он и живёт отдельно: <see cref="RunStateService"/> зовёт то же самое.
    /// </remarks>
    public static class RunSaves
    {
        /// <summary>
        /// Ключ автосейва забега активной гильдии. Пустая строка — активной гильдии нет, писать некуда
        /// и читать нечего (ТЗ [[save-system]] §3: гильдия и есть слот сохранения).
        /// </summary>
        public static string KeyFor(IProfileService profiles) => profiles != null ? profiles.RunKey : string.Empty;

        /// <summary>Есть ли автосейв забега на диске — вопрос меню про «Продолжить».</summary>
        public static bool Exists(ISaveService save, IProfileService profiles)
        {
            if (save == null) return false;

            string key = KeyFor(profiles);
            return !string.IsNullOrEmpty(key) && save.Exists(key);
        }
    }
}
