using System.Collections.Generic;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Рантайм-доступ к контенту по строковому id (вики «13» §3.6). Единственный шов между кодом и
    /// контент-ассетами; регистрируется в DI из <see cref="ContentDatabase"/>. Никакого
    /// <c>Resources.Load</c> и статик-доступа.
    /// </summary>
    public interface IContentDatabase
    {
        // Бросающего Get&lt;T&gt; здесь больше нет: за всю историю его не позвал никто — весь код
        // резолвит контент через TryGet и сам решает, что делать с промахом (аудит 2026-07-26).

        /// <summary>Попытаться получить определение по id нужного типа.</summary>
        bool TryGet<T>(string id, out T def) where T : ContentDefinition;

        /// <summary>Все зарегистрированные определения типа <typeparamref name="T"/>.</summary>
        IReadOnlyList<T> All<T>() where T : ContentDefinition;
    }
}
