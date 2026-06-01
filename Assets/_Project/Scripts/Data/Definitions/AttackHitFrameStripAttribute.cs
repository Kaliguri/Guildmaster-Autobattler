using System;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Маркер для поля индексов кадров контакта (<c>UnitVisual._attackHitFrames</c>): редактор-drawer
    /// рисует полоску превью спрайтов атаки с кликом по кадру (вики «14»). Без зависимости от Odin в рантайме —
    /// сам drawer живёт в <c>Guildmaster.Data.Editor</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class AttackHitFrameStripAttribute : Attribute
    {
    }
}
