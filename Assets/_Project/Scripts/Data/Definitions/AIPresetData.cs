using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Пресет поведения AI (roadmap «2–3 пресета на архетип», вики «13» §3.2). Обёртка над существующим
    /// plain-классом <see cref="AIProfile"/>: дефолт юнита — <c>UnitData.AiPreset</c>, альтернативы игрока —
    /// <c>RelicData.AltAiPresets</c>. Кастомные профили игрока (post-festival) — та же структура в RunState.
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/AI Preset", fileName = "AiPreset")]
    public sealed class AIPresetData : ContentDefinition
    {
        [Tooltip("Параметризованный профиль поведения (Filter/Score/Override/Retreat/Kite).")]
        [SerializeField] private AIProfile _profile = new AIProfile();

        public AIProfile Profile => _profile;

        // Списка «каким мементо подходит» здесь больше нет: он был пуст во ВСЕХ пресетах и не читался
        // ни пикером, ни валидацией. Теги юнита живут в UnitData.InfoTags и резолвятся UnitTagResolver —
        // второй список тегов рядом с ним только разъехался бы (аудит 2026-07-26, волна 2).
    }
}
