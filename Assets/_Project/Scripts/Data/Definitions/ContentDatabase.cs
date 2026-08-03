using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Реестр всего контент-контента проекта (вики «13» §3.6). Единственный экземпляр; регистрируется
    /// в <c>RootLifetimeScope</c> и оборачивается рантайм-индексом <see cref="ContentRegistry"/> при
    /// сборке контейнера.
    /// <para>
    /// РЕШЕНИЕ (отклонение от §3.6): один плоский список <see cref="Entries"/> вместо списка-на-домен.
    /// Рантайм-API (<see cref="IContentDatabase"/>) типо-обобщён (<c>All&lt;T&gt;</c>), полнота проверяется
    /// одним инвариантом «каждый ContentDefinition-ассет ∈ Entries», а число доменов растёт (Фаза 5) без
    /// правки схемы БД. Список наполняется только <c>Alebardium/Data/Sync Content Database</c>.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content Database", fileName = "ContentDatabase")]
    public sealed class ContentDatabase : ScriptableObject
    {
        [Tooltip("Версия контент-схемы (общая с DTO/Workshop-контрактом, вики «8»).")]
        [SerializeField] private int _schemaVersion = 1;

        /// <summary>
        /// Версия контент-схемы. Читателей в игре пока нет, и аудит 2026-07-26 снял геттер как мёртвый —
        /// но это задел под заявленное: тех-док требует её в контракте Workshop-UGC и в заголовке реплея
        /// (контракт Workshop-UGC и заголовок реплея). Возвращён вместе с объяснением, чтобы поле не висело
        /// «присвоено и не используется» и чтобы следующая метла не сносила его снова.
        /// </summary>
        public int SchemaVersion => _schemaVersion;

        [Tooltip("Все контент-ассеты проекта. Наполняется Alebardium/Data/Sync Content Database — не тащить руками.")]
        [SerializeField, ReadOnly, ListDrawerSettings(ShowFoldout = true, IsReadOnly = true)]
        private List<ContentDefinition> _entries = new List<ContentDefinition>();


        /// <summary>Все зарегистрированные определения (Sync-managed; порядок — по id для стабильного диффа).</summary>
        public IReadOnlyList<ContentDefinition> Entries => _entries;

#if UNITY_EDITOR
        /// <summary>Только для editor-Sync: заменить список записей.</summary>
        internal void EditorSetEntries(List<ContentDefinition> entries) => _entries = entries;
#endif
    }
}
