using System.Collections.Generic;
using Guildmaster.Data.Definitions;

namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Индекс способностей по их id — то, чем приёмник чанка находит <see cref="AbilityData"/>.
    /// <para><b>Зачем он вообще нужен.</b> Способность НЕ лежит в реестре контента: это
    /// <c>[Serializable]</c>-класс внутри <see cref="RelicData"/>, а не <c>ScriptableObject</c> со своим
    /// ассетом. Свой id у неё есть, а дома в базе — нет, поэтому <c>IContentDatabase.TryGet</c> её не
    /// найдёт никогда. Единственный способ адресовать её по сети — собрать словарь из всех реликвий.</para>
    /// <para><b>Живёт рядом с единственным потребителем</b> — читателем чанков. Если индекс понадобится
    /// кому-то ещё (реплей с диска, Workshop), его место переедет в дата-слой; заводить общий дом под
    /// одного пользователя — плодить владельца на пустом месте.</para>
    /// </summary>
    public sealed class TapeAbilityIndex
    {
        private readonly IContentDatabase _content;
        private Dictionary<string, AbilityData> _byId;

        public TapeAbilityIndex(IContentDatabase content) => _content = content;

        /// <summary>Сколько способностей в индексе (собирает его при первом обращении).</summary>
        public int Count
        {
            get
            {
                Build();
                return _byId.Count;
            }
        }

        /// <summary>Найти способность по id. <c>false</c> = такой нет ни в одной реликвии.</summary>
        public bool TryGet(string id, out AbilityData ability)
        {
            Build();
            return _byId.TryGetValue(id, out ability);
        }

        /// <summary>Пересобрать индекс (правка контента в редакторе, смена базы).</summary>
        public void Invalidate() => _byId = null;

        private void Build()
        {
            if (_byId != null) return;

            _byId = new Dictionary<string, AbilityData>(64);
            if (_content == null) return;

            IReadOnlyList<UnitData> units = _content.All<UnitData>();
            for (int i = 0; i < units.Count; i++)
            {
                AbilityData[] abilities = units[i] != null ? units[i].Abilities : null;
                if (abilities == null) continue;

                for (int a = 0; a < abilities.Length; a++)
                {
                    AbilityData ability = abilities[a];
                    if (ability == null || string.IsNullOrEmpty(ability.Id)) continue;

                    // Дубль id — не наша забота здесь: первый выигрывает, а ловить такое должен
                    // контентный валидатор. Тихо перезаписывать было бы хуже: чанк резолвился бы в
                    // другую способность в зависимости от порядка юнитов в базе.
                    if (!_byId.ContainsKey(ability.Id)) _byId.Add(ability.Id, ability);
                }
            }
        }
    }
}
