using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>Тир энкаунтера — метка сложности/роли боя (вики «13» §3.3).</summary>
    public enum EncounterTier
    {
        /// <summary>Рядовой бой на карте.</summary>
        Common,
        /// <summary>Элитный бой (усиленный состав/награда).</summary>
        Elite,
        /// <summary>Бой-финалист акта (босс гильдии).</summary>
        Finalist,
        /// <summary>Только из ивентов; на карте сам не спавнится (валидация §8.9).</summary>
        Special,
    }

    /// <summary>
    /// Один вражеский «пакет» энкаунтера: какого врага (по строковому id), сколько и где (вики «13» §3.3).
    /// Ссылка на врага — по <c>enemy.*</c> id (не прямой SO-ref): состав переживает сериализацию в
    /// RunState/DTO/Workshop и валидируется §8.7. Позиция — явный якорь в enemy-зоне (dev-срез); при
    /// <see cref="Count"/> &gt; 1 копии выстраиваются вертикальным кластером с шагом <see cref="Spacing"/>.
    /// </summary>
    /// <remarks>
    /// Процедурная расстановка «по зоне» (ZoneHint из эскиза §3.3) — задел Фазы 5: тогда добавится
    /// поле-подсказка зоны, а <see cref="Position"/> станет явным override. Пока — только явные позиции
    /// (нужны, чтобы точно воспроизвести перенесённые хардкод-бои).
    /// </remarks>
    [System.Serializable]
    public struct EncounterUnit
    {
        [Tooltip("Content id врага (enemy.*). Резолвится через IContentDatabase на старте боя (валидация §8.7).")]
        [SerializeField] private string _enemyId;

        [Tooltip("Якорь позиции в enemy-зоне (мировые единицы). При Count>1 — центр вертикального кластера.")]
        [SerializeField] private Vector2 _position;

        [Tooltip("Сколько копий врага заспавнить в этой точке (>=1).")]
        [SerializeField] private int _count;

        [Tooltip("Вертикальный шаг между копиями при Count>1 (мировые единицы). <=0 → дефолт 0.8.")]
        [SerializeField] private float _spacing;

        public string EnemyId => _enemyId;
        public Vector2 Position => _position;
        public int Count => Mathf.Max(1, _count);
        public float Spacing => _spacing <= 0f ? 0.8f : _spacing;
    }

    /// <summary>
    /// Состав боя (вражеская сторона): кто и где стоит из врагов (вики «13» §3.3, «10» §3). Игрок сюда
    /// НЕ входит — player-ростер приходит отдельным слоем в <c>BattlePresetData</c> (шаг 3 плана): энкаунтер
    /// = только team 1. Загрузчик <c>EncounterLoader</c> строит из этого бой поверх <c>ResetBattle</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Encounter", fileName = "Encounter")]
    public sealed class EncounterData : ContentDefinition
    {
        [Header("Encounter")]
        [Tooltip("Метка сложности/роли боя. Special — только из ивентов (валидация §8.9).")]
        [SerializeField] private EncounterTier _tier = EncounterTier.Common;

        [Tooltip("Вражеский состав: пакеты {враг + позиция + количество}. Player-сторона — вне энкаунтера (BattlePreset, шаг 3).")]
        [SerializeField] private EncounterUnit[] _units;

        [Header("Arena (seam)")]
        [Tooltip("Ключ арены боя (prefab-per-arena Addressables, вики «10» §4-5). ЗАДЕЛ: пока не читается — " +
                 "арена берётся из сцены через FindFirstObjectByType. Свап на загрузку по ключу — будущий шаг.")]
        [SerializeField] private string _arenaId;

        public EncounterTier Tier => _tier;
        public IReadOnlyList<EncounterUnit> Units => _units;
        public string ArenaId => _arenaId;
    }
}
