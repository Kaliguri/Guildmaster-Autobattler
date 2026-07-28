using UnityEngine;

namespace Guildmaster.Game
{
    /// <summary>
    /// Один ответ на «поле скоупа не назначено в сцене».
    /// <para>До этого половина полей имела тихий фолбэк на пустой инстанс, а половина разыменовывалась в лоб
    /// и роняла весь <c>Configure</c> голым NRE — то есть одна и та же ошибка автора давала то невидимую
    /// деградацию, то падение без диагноза (аудит фолбэков 2026-07-26, п.4). Теперь оба случая названы
    /// вслух и различаются по СМЫСЛУ, а не по тому, кто как написал строчку:</para>
    /// <list type="bullet">
    /// <item><see cref="Require{T}"/> — без ассета контейнер бессмыслен: падаем с названной причиной.</item>
    /// <item><see cref="Optional{T}"/> — подсистема деградирует целиком и заметно (тишина, нет джуса):
    /// пустой инстанс, но с красной ошибкой, а не молча.</item>
    /// </list>
    /// <para>Настоящий гейт — <c>SceneWiringTests</c>: он открывает каждую сцену билда и требует эти поля
    /// заполненными ДО запуска. Здесь — последняя линия для сцен вне билда и для собранных на лету.</para>
    /// </summary>
    internal static class ScopeWiring
    {
        /// <summary>Обязательная ссылка. Пусто → исключение с именем скоупа и поля вместо NRE где-то внутри.</summary>
        public static T Require<T>(T asset, string scope, string field) where T : Object
        {
            if (asset != null) return asset;
            throw new System.InvalidOperationException(
                $"[{scope}] - поле {field} не назначено в сцене '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}'. " +
                "Без него контейнер собрать нельзя. Разводку сцен стережёт SceneWiringTests.");
        }

        /// <summary>
        /// Опциональная ссылка: подсистема выключается целиком, а не подменяется похожими значениями.
        /// </summary>
        /// <param name="consequence">Что игрок недосчитается — пишется в лог, чтобы дефект был узнаваем.</param>
        public static T Optional<T>(T asset, string scope, string field, string consequence) where T : ScriptableObject
        {
            if (asset != null) return asset;
            Debug.LogError($"[{scope}] - поле {field} не назначено в сцене → {consequence}");
            return ScriptableObject.CreateInstance<T>();
        }
    }
}
