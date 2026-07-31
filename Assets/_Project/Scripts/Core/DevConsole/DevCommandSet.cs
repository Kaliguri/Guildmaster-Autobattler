using System;
using System.Collections.Generic;

namespace Guildmaster.Core.DevConsole
{
    /// <summary>
    /// Набор команд одного модуля: регистрирует их в общем реестре и снимает все разом при
    /// <see cref="Dispose"/>.
    /// </summary>
    /// <remarks>
    /// Существует ради одного сценария, который иначе бьёт каждый раз: модуль живёт короче консоли
    /// (компонент сцены, боевой скоуп), и после перезагрузки домена его <c>Start</c> зовётся снова — а
    /// реестр на дубль имени бросает исключение. Снимать команды по одной руками означало бы держать
    /// список имён в двух местах и разойтись на первой же новой команде.
    /// </remarks>
    public sealed class DevCommandSet : IDisposable
    {
        private readonly DevCommandRegistry _registry;
        private readonly List<string> _names = new List<string>();

        public DevCommandSet(DevCommandRegistry registry)
        {
            _registry = registry;
        }

        /// <summary>Сколько команд в наборе.</summary>
        public int Count => _names.Count;

        /// <summary>Зарегистрировать команду и запомнить её имя за набором.</summary>
        public void Add(string name, string summary, DevCommandHandler handler, params DevParam[] parameters)
        {
            if (_registry == null) return;

            _registry.Register(name, summary, handler, parameters);
            _names.Add(name);
        }

        /// <summary>Снять все команды набора. Идемпотентно.</summary>
        public void Dispose()
        {
            if (_registry == null) return;

            for (int i = 0; i < _names.Count; i++) _registry.Unregister(_names[i]);
            _names.Clear();
        }
    }
}
