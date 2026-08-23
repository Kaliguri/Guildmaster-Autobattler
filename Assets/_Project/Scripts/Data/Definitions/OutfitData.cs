using System;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>Одна строка облачения: какую часть рига чем накрыть.</summary>
    [Serializable]
    public struct OutfitPiece
    {
        [Tooltip("Имя узла-рисунка в риге, ровно как на префабе: Weapon_L_Shield_Art, Torso_Art, Head_Hair_Art.")]
        public string Part;

        [Tooltip("Чем рисовать. ПУСТО = часть не показывать вовсе (так снимается щит у тех, кто его не носит).")]
        public Sprite Sprite;
    }

    /// <summary>
    /// ОБЛАЧЕНИЕ: что на юните надето — броня, предметы в руках, позже головной убор. Список
    /// «часть рига → спрайт» поверх того, что лежит на префабе.
    /// <para>
    /// <b>Почему это не «визуал» и не часть архетипа.</b> Архетип
    /// (<see cref="AnimationArchetypeData"/>) отвечает за ХОРЕОГРАФИЮ — какие клипы играют; он
    /// повторяется у многих Мементо, поэтому спрайты обязаны быть отвязаны от него. Но и отдельной
    /// сущности им не нужно: владелец у брони и у оружия один — Мементо (у врага —
    /// <see cref="EnemyData"/>), поэтому они едут вместе. Решение Макса 06.08.2026, см.
    /// <c>tech/40-planning/weapon-system</c> §2.
    /// </para>
    /// <para>
    /// <b>Два разных «ничего», и путать их нельзя:</b>
    /// <list type="bullet">
    /// <item><b>записи нет</b> — часть не трогаем, играет то, что положено на префабе;</item>
    /// <item><b>запись есть, спрайт пуст</b> — часть ПРЯЧЕМ. Так «меч без щита» получается из
    /// данных, а не отдельным префабом и не отдельным архетипом: хореография «меча и щита» не
    /// меняется от того, есть ли щит в кадре.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Часть адресуется ИМЕНЕМ УЗЛА по конвенции рига (<c>RigNaming</c>) — тем же способом, каким
    /// данные уже адресуют части для свечения каста. Имя, которого в риге нет, — ошибка авторинга, а
    /// не молча пропущенная строка: её ловит <c>OutfitCoverageTests</c>.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Guildmaster/Content/Outfit", fileName = "Outfit")]
    public sealed class OutfitData : ContentDefinition
    {
        [Tooltip("Части, которые это облачение переопределяет. Часть, которой здесь нет, остаётся как на префабе.")]
        [SerializeField] private OutfitPiece[] _pieces = Array.Empty<OutfitPiece>();

        /// <summary>Строки облачения — как заданы в ассете.</summary>
        public OutfitPiece[] Pieces => _pieces ?? Array.Empty<OutfitPiece>();

        /// <summary>
        /// Что делать с частью <paramref name="part"/>: <c>true</c> — облачение о ней говорит, и
        /// <paramref name="sprite"/> несёт ответ (<c>null</c> = спрятать). <c>false</c> — не наша часть,
        /// оставить как на префабе.
        /// </summary>
        public bool TryResolve(string part, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrEmpty(part) || _pieces == null) return false;

            for (int i = 0; i < _pieces.Length; i++)
            {
                if (!string.Equals(_pieces[i].Part, part, StringComparison.Ordinal)) continue;
                sprite = _pieces[i].Sprite;
                return true;
            }
            return false;
        }
    }
}
