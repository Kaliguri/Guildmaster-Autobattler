using Cysharp.Threading.Tasks;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Шов загрузки/выгрузки боевой сцены (план 11 §2, вики «10» §8). Соло-тело — простая аддитивная
    /// загрузка (<see cref="SceneLoader"/>); NGO Scene Management встанет за этим же интерфейсом в Фазе 6.
    /// Введён, чтобы <c>BattleFlow</c> оркестрировал бой поверх абстракции и тестировался без реальной сцены.
    /// </summary>
    public interface ISceneLoader
    {
        /// <summary>Аддитивно загрузить BattleScene (no-op, если уже загружена).</summary>
        UniTask LoadBattleAsync();

        /// <summary>Выгрузить BattleScene (no-op, если не загружена).</summary>
        UniTask UnloadBattleAsync();
    }
}
