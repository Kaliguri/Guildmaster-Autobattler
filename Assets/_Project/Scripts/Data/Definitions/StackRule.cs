namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Поведение при повторном наложении того же эффекта на цель (вики «6» §5.3).
    /// </summary>
    public enum StackRule
    {
        /// <summary>Повтор игнорируется — существующий экземпляр не трогаем.</summary>
        None = 0,

        /// <summary>Добавить стак (до <c>MaxStacks</c>); длительность не обновлять.</summary>
        Stack = 1,

        /// <summary>Обновить длительность до полной; стаки не трогать.</summary>
        Refresh = 2,

        /// <summary>Добавить стак И обновить длительность.</summary>
        StackAndRefresh = 3,
    }
}
