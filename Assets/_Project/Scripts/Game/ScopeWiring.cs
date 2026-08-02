using UnityEngine;
using VContainer;

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

        /// <summary>
        /// Зарегистрировать компонент, лежащий в ЛЮБОЙ из персист-сцен, а не только в сцене скоупа.
        /// </summary>
        /// <remarks>
        /// <b>Почему не <c>RegisterComponentInHierarchy</c>.</b> Тот ищет строго в сцене своего скоупа
        /// (<c>FindComponentProvider</c> запоминает её при регистрации), а персист-сцен у нас две:
        /// <c>WorldScene</c> с камерой и ареной и <c>CombatSystemsScene</c>, где исторически лежат
        /// презентеры. Мировой скоуп поднимается раньше второй сцены и не нашёл бы там ничего.
        /// <para><b>Это временно и признано долгом:</b> объекты показа мировые по смыслу и должны
        /// переехать в <c>WorldScene</c> вместе с расформированием <c>CombatSystemsScene</c> (шаг 1в
        /// разделения скоупов). Пока они не переехали, поиск идёт по загруженным сценам — лениво, один
        /// раз, в момент первого резолва.</para>
        /// </remarks>
        public static RegistrationBuilder RegisterPersistComponent<T>(
            IContainerBuilder builder) where T : Component
        {
            return builder.Register(resolver =>
            {
                var component = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
                if (component == null)
                    throw new System.InvalidOperationException(
                        $"[ScopeWiring] - компонент {typeof(T).Name} не найден ни в одной загруженной сцене. " +
                        "Он должен лежать в персист-сцене (WorldScene или CombatSystemsScene).");

                resolver.Inject(component);
                return component;
            }, Lifetime.Singleton);
        }
    }
}
