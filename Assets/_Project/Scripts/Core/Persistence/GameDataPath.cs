using System.IO;
using UnityEngine;

namespace Guildmaster.Core.Persistence
{
    /// <summary>
    /// Корень пользовательских данных игры — <b>намеренно не <see cref="Application.persistentDataPath"/></b>
    /// (ТЗ [[save-system]] §4).
    /// <para><b>Зачем.</b> Unity строит <c>persistentDataPath</c> как
    /// <c>…/LocalLow/{companyName}/{productName}</c>, то есть путь к сохранениям игрока растёт из
    /// <i>маркетингового</i> имени игры. Переименование после релиза (а имя ещё проходит проверку) увело
    /// бы игру на пустой каталог: сейвы «пропали», маска Steam Auto-Cloud указывает в никуда, откатить
    /// нельзя. Поэтому корень собирается из <b>кодовых</b> имён, которые меняться не будут, а имя игры
    /// остаётся свободным.</para>
    /// <para><b>Как.</b> Родительский каталог <c>persistentDataPath</c> через два уровня — это платформенная
    /// папка данных (<c>LocalLow</c> на Windows), она от company/product не зависит. К ней и приписываются
    /// кодовые имена. Если структура окажется другой (не-Windows платформа), берём
    /// <c>persistentDataPath</c> как есть: это не отказ, а вторая нормальная ветка — там имя каталога
    /// платформозависимо и наша схема всё равно не применима.</para>
    /// </summary>
    public static class GameDataPath
    {
        /// <summary>Кодовое имя студии. Маркетинговое название игры на него не влияет — в этом весь смысл.</summary>
        public const string CompanyFolder = "Alebardium";

        /// <summary>Кодовое имя проекта (совпадает с неймспейсом сборок). Не переименовывать НИКОГДА.</summary>
        public const string ProductFolder = "Guildmaster";

        private static string _root;

        /// <summary>
        /// Корень данных игрока. Внутри него живут <c>Saves/</c> (едет в Steam Cloud) и <c>Local/</c>
        /// (данные компьютера, не едет).
        /// </summary>
        public static string Root
        {
            get
            {
                if (!string.IsNullOrEmpty(_root)) return _root;

                // .../LocalLow/{company}/{product} → поднимаемся к .../LocalLow
                DirectoryInfo platformRoot = Directory.GetParent(Application.persistentDataPath)?.Parent;

                _root = platformRoot != null
                    ? Path.Combine(platformRoot.FullName, CompanyFolder, ProductFolder)
                    : Application.persistentDataPath;

                return _root;
            }
        }
    }
}
