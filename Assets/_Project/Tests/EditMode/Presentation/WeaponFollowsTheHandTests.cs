using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// ОРУЖИЕ НЕ КРУТИТСЯ САМО: ни один клип не имеет права анимировать узел предмета в руке.
    ///
    /// <b>Зачем этот тест существует.</b> Меч в руке — жёсткая связь: куда повернулась кисть, туда и
    /// клинок. Дав узлу предмета собственный поворот, мы завели второго владельца ориентации оружия — и
    /// он немедленно разошёлся с первым. Расплата пришла с той стороны, откуда не ждали: дуга за клинком
    /// строится от плеча к острию, а «где остриё» стало зависеть от двух независимых поворотов сразу, и
    /// след лёг мимо меча.
    ///
    /// Требование Макса дословно (2026-08-07): «надо поставить клинок в одно положение с рукой вдоль
    /// одной прямой и ЗАПРЕТИТЬ крутить оружие без руки... И чтобы на анимации любой это тоже НЕ
    /// сломалось. Всегда соблюдалось.»
    ///
    /// <b>Почему тест, а не договорённость.</b> «Всегда соблюдалось» договорённостью не держится: клип
    /// авторят руками, ключ ставится одним движением, и увидеть лишнюю кривую можно только специально
    /// её поискав. Девять клипов уже успели её завести.
    /// </summary>
    public sealed class WeaponFollowsTheHandTests
    {
        /// <summary>Где лежат клипы рига. Расширять список — по мере появления новых скелетов.</summary>
        static readonly string[] ClipFolders = { "Assets/_Project/Prefabs" };

        [Test]
        public void NoClip_RotatesTheHeldItemNode()
        {
            var offenders = new List<string>();
            int clips = 0;

            foreach (string path in ClipPaths())
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null) continue;
                clips++;

                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.path == null || !IsHeldItemNode(binding.path)) continue;

                    offenders.Add($"{clip.name}: {binding.path} → {binding.propertyName}");
                }
            }

            Assert.That(clips, Is.GreaterThan(0), "не найдено ни одного клипа — тест проверял пустоту");
            Assert.That(offenders, Is.Empty,
                "Клип крутит (или двигает) предмет в руке отдельно от кисти. Поворот обязан жить на " +
                "кисти: она держит оружие жёстко, и второго владельца у направления клинка быть не " +
                "должно. Перенеси кривую на Hand_* и удали эту:\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>
        /// Узел предмета — последнее звено пути (<c>.../Hand_R/Weapon_R</c>), а не любое вхождение
        /// «Weapon»: арт предмета лежит ПОД ним (<c>Weapon_R_Sword_Art</c>), и его собственные кривые —
        /// отдельный разговор, этот тест про них не спорит.
        /// </summary>
        static bool IsHeldItemNode(string path)
        {
            int slash = path.LastIndexOf('/');
            string leaf = slash >= 0 ? path.Substring(slash + 1) : path;
            return leaf == "Weapon_R" || leaf == "Weapon_L";
        }

        static IEnumerable<string> ClipPaths() =>
            AssetDatabase.FindAssets("t:AnimationClip", ClipFolders)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct();
    }
}
