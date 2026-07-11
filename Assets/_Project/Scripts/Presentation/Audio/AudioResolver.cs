using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Presentation.Audio
{
    /// <summary>
    /// Резолвит звуковой ключ (вики «13» §7): <c>{contentId}.{action}</c> → точная запись → дефолт действия
    /// → тишина. На тишину — один лог на ключ за сессию (не спамим). Чистая логика над
    /// <see cref="IAudioCatalog"/>, поэтому покрывается юнит-тестом с фейк-каталогом.
    /// </summary>
    public sealed class AudioResolver
    {
        private readonly IAudioCatalog _catalog;
        private readonly HashSet<string> _loggedMisses = new HashSet<string>();

        public AudioResolver(IAudioCatalog catalog) => _catalog = catalog;

        /// <summary>Строковый вид действия для ключа (<c>attack</c>/<c>hit</c>/<c>death</c>/<c>cast</c>/<c>ui</c>).</summary>
        public static string ActionKey(AudioAction action) => action switch
        {
            AudioAction.Attack => "attack",
            AudioAction.Hit    => "hit",
            AudioAction.Death  => "death",
            AudioAction.Cast   => "cast",
            AudioAction.Ui     => "ui",
            _                  => action.ToString().ToLowerInvariant(),
        };

        /// <summary>Ключ для проигрывания, либо <c>null</c> = тишина (нет ни точной записи, ни дефолта).</summary>
        public string Resolve(string contentId, AudioAction action)
        {
            if (_catalog == null) return null;

            string actionKey = ActionKey(action);
            if (!string.IsNullOrEmpty(contentId))
            {
                string exact = contentId + "." + actionKey;
                if (_catalog.Contains(exact)) return exact;
            }

            if (_catalog.HasDefault(action)) return actionKey;

            string missKey = (string.IsNullOrEmpty(contentId) ? "?" : contentId) + "." + actionKey;
            if (_loggedMisses.Add(missKey))
                Debug.Log($"[Audio] Нет звука для '{missKey}' (ни записи, ни дефолта) — тишина.");
            return null;
        }
    }
}
