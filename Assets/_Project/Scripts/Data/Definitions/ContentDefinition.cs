using Sirenix.OdinInspector;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Базовый класс всех контент-SO: несёт канонический строковый id (<c>domain.name</c>), по которому
    /// связываются слои данных, работают реестр (<see cref="ContentDatabase"/>), валидация и производные
    /// лок/аудио-ключи (вики «13» §2). Наследники — <see cref="RelicData"/>, <see cref="EffectData"/>,
    /// <see cref="VesselData"/> и мета-типы Фазы 4.
    /// </summary>
    public abstract class ContentDefinition : ScriptableObject
    {
        [Tooltip("Канонический content id (domain.name). Автозаполняется из имени ассета при создании; " +
                 "после выхода контента «в мир» (сейвы/реплеи/Workshop) НЕ меняется. Правка — через кнопку Edit Id.")]
        [SerializeField, LabelText("Content Id"), EnableIf(nameof(_idUnlocked))]
        private string _id;

        // Editor-разблокировка правки id (не сериализуется). Кнопка Edit Id переключает; см. ниже.
        [System.NonSerialized] private bool _idUnlocked;

        /// <summary>Канонический content id (<c>domain.name</c>, вики «13» §2.2).</summary>
        public string Id => _id;

        /// <summary>
        /// Присвоить id из движка (рантайм-создание def'ов через <c>CreateRuntime</c>, editor-миграции).
        /// Авторинг-id сериализуется в ассете; этот сеттер — НЕ для геймплейной логики.
        /// </summary>
        protected void SetId(string id) => _id = id;

#if UNITY_EDITOR
        /// <summary>
        /// Автозаполнение пустого id из имени ассета (жизненный цикл id, вики «13» §2.3 п.1). После
        /// заполнения id с именем НЕ синхронизируется (п.2) — иначе rename ломал бы сейвы/реплеи/Workshop.
        /// </summary>
        protected virtual void OnValidate()
        {
            if (string.IsNullOrEmpty(_id) && !string.IsNullOrEmpty(name)
                && ContentDomains.TryGetDomain(GetType(), out _))
            {
                _id = ContentDomains.MakeId(GetType(), name);
            }
        }

        [PropertyOrder(-1), Button("Edit Id"), PropertyTooltip(
            "Разблокировать правку id. ОСТОРОЖНО: id попадает в сейвы, реплеи и Workshop-контент — " +
            "менять безопасно только до выхода контента «в мир».")]
        private void ToggleEditId()
        {
            if (_idUnlocked) { _idUnlocked = false; return; }
            _idUnlocked = UnityEditor.EditorUtility.DisplayDialog(
                "Edit content id?",
                "Id попадает в сейвы, реплеи и Workshop-контент. Менять безопасно только до выхода контента " +
                "«в мир». Разблокировать правку?",
                "Разблокировать", "Отмена");
        }
#endif
    }
}
