namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Якорь полиморфного поведения эффекта для сериализации. <see cref="EffectData"/> хранит
    /// <c>[SerializeReference] IEffectComponent[]</c>, так что реликвии/эффекты ссылаются на
    /// поведение, НЕ создавая зависимость <c>Data → Combat</c> вверх (вики «10» §2.2).
    /// </summary>
    /// <remarks>
    /// СКЕЛЕТ Фазы 1 — это маркер. Реальные lifecycle-хуки (<c>OnApply/OnTick/OnExpire</c>)
    /// оперируют рантайм-состоянием боя (<c>RuntimeUnit</c>, контекст), поэтому объявляются в
    /// Фазе 2 производным интерфейсом на стороне Combat (<c>IRuntimeEffectComponent : IEffectComponent</c>).
    /// Так контекст-типы остаются в Combat, а сериализационный якорь — внизу, в Data.
    /// </remarks>
    public interface IEffectComponent
    {
    }
}
