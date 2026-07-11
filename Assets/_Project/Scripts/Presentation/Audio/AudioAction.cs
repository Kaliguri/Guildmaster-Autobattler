namespace Guildmaster.Presentation.Audio
{
    /// <summary>
    /// Канонический список звуковых действий (вики «13» §7). Ключ звука — <c>{contentId}.{action}</c>
    /// (напр. <c>relic.defender.attack</c>); дефолт действия покрывает контент без своего сэмпла.
    /// Строковый вид действия — <see cref="AudioResolver.ActionKey"/>.
    /// </summary>
    public enum AudioAction
    {
        Attack,
        Hit,
        Death,
        Cast,
        Ui,
    }
}
