namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Слот визуала каста способности (вики «13» §3.1). Индекс совпадает с <c>AnimationArchetypeData.SkillClip(int)</c>:
    /// каст проигрывает клип соответствующего слота. Порядок стабилен.
    /// </summary>
    public enum SkillSlot
    {
        Skill1 = 0,
        Skill2 = 1,
        Skill3 = 2,
        Skill4 = 3,
    }
}
