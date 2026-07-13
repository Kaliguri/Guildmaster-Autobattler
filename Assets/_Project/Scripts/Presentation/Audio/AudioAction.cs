namespace Guildmaster.Presentation.Audio
{
    /// <summary>
    /// Канонический список звуковых действий (вики «13» §7). Ключ звука — <c>{contentId}.{action}</c>
    /// (напр. <c>relic.defender.attack</c>); дефолт действия покрывает контент без своего сэмпла.
    /// Строковый вид действия — <see cref="AudioResolver.ActionKey"/>.
    /// </summary>
    public enum AudioAction
    {
        // Значения-ординалы сериализуются в AudioCatalog.asset (_defaults[].Action) — только ДОБАВЛЯТЬ в конец,
        // не переставлять (иначе дефолты в ассете съедут). Строковый вид — AudioResolver.ActionKey.
        Attack,  // 0 — замах/свинг ближнего боя
        Hit,     // 1 — импакт по цели
        Death,   // 2 — смерть юнита
        Cast,    // 3 — активация способности
        Ui,      // 4 — общий UI-клик
        Fire,    // 5 — выстрел снаряда / muzzle
        Evade,   // 6 — уворот/промах
        Shield,  // 7 — поглощение щитом
        Heal,    // 8 — восстановление HP
        Apply,   // 9 — эффект наложен
        Expire,  // 10 — эффект истёк
        Tick,    // 11 — периодика DoT/HoT
        Stinger, // 12 — событийный акцент (килл/победа/старт боя)
    }
}
