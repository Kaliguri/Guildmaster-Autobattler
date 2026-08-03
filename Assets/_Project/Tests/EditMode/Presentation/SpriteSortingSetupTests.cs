using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Сортировка спрайтов на арене работает НА ДВУХ УРОВНЯХ, и оба обязательны — иначе одинаковые
    /// юниты начинают перекрывать друг друга произвольно.
    /// <para>
    /// Верхний уровень — код: слой и <c>sortingOrder</c> раздаёт презентация, у скелетного тела их
    /// получает <c>SortingGroup</c> целиком (см. <c>SkeletalBodyVisual</c>). Он разводит РАЗНЫЕ
    /// сущности: тело против VFX, оверлей против декора.
    /// </para>
    /// <para>
    /// Нижний уровень — ось: у одинаковых групп с одинаковыми настройками слоя (два одинаковых юнита
    /// рядом) ордер совпадает, и разложить их может только глубина. Поэтому
    /// <c>Transparency Sort Mode</c> = <c>CustomAxis</c> по Y: кто ниже по экрану, тот ближе.
    /// Без оси порядок между равными не определён и «мигает» при движении.
    /// </para>
    /// <para>
    /// Инвариант живёт МЕЖДУ файлами — настройка проекта и код сортировки — поэтому он здесь, а не в
    /// комментарии: комментарий видит только одна сторона шва. Обоснование —
    /// <c>docs/wiki/tech/00-meta/journal/2026-07-30-sprite-sorting-has-two-levels.md</c>.
    /// </para>
    /// </summary>
    public sealed class SpriteSortingSetupTests
    {
        [Test]
        public void TransparencySort_IsCustomAxis_SoEqualOrdersFallBackToDepth()
        {
            Assert.AreEqual(TransparencySortMode.CustomAxis, GraphicsSettings.transparencySortMode,
                "Ось — второй уровень сортировки: у одинаковых юнитов ордер совпадает, и развести их " +
                "может только глубина. В Default порядок между равными не определён.");
        }

        [Test]
        public void TransparencySortAxis_IsScreenY_NotDepthOrX()
        {
            var axis = GraphicsSettings.transparencySortAxis;

            Assert.AreEqual(0f, axis.x, 0.001f, "Ось сортировки — экранная Y, вклада X быть не должно");
            Assert.AreEqual(1f, axis.y, 0.001f, "Кто ниже по экрану, тот ближе к зрителю");
            Assert.AreEqual(0f, axis.z, 0.001f,
                "Z не участвует: камера ортографическая, глубину несёт именно Y");
        }

        /// <summary>
        /// 2D-рендерер URP имеет СВОЙ Transparency Sort Mode, и он перекрывает настройку проекта.
        /// Пока он в Default, ось из Graphics Settings доходит до арены; стоит его тронуть — и два
        /// теста выше станут зелёными впустую.
        /// <para>
        /// Проверяются только рендереры АКТИВНОГО пайплайна, а не все в проекте: вендорные паки носят
        /// с собой свои URP-ассеты и рендереры (например `Assets/Cainos/...` — с `CustomAxis`), они в
        /// игре не участвуют, и падение на них было бы ложным.
        /// </para>
        /// </summary>
        [Test]
        public void ActiveRenderers_LeaveSortModeDefault_SoProjectAxisReachesTheArena()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            Assert.IsNotNull(pipeline, "Активного URP-ассета нет — рендерить арену нечем");

            var list = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
            Assert.IsNotNull(list, "У активного пайплайна нет списка рендереров — изменился контракт URP");
            Assert.Greater(list.arraySize, 0, "Активный пайплайн не содержит ни одного рендерера");

            for (int i = 0; i < list.arraySize; i++)
            {
                var data = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (data == null) continue;

                var prop = new SerializedObject(data).FindProperty("m_TransparencySortMode");
                if (prop == null) continue;   // не 2D-рендерер: у 3D-рендерера такого поля нет

                Assert.AreEqual((int)TransparencySortMode.Default, prop.intValue,
                    $"{AssetDatabase.GetAssetPath(data)}: рендерер перекрыл сортировку своим режимом, " +
                    "и ось из Graphics Settings до арены не дойдёт. Владелец оси один — настройки проекта.");
            }
        }

        /// <summary>
        /// САМЫЙ уязвимый уровень: без <see cref="SortingGroup"/> ордера частей перестают быть
        /// локальными и начинают сравниваться с частями ЧУЖИХ юнитов напрямую. Тогда меч одного
        /// (ордер 2) уходит поверх торса другого (ордер 0) независимо от того, кто ближе к зрителю, —
        /// потому что ордер сравнивается РАНЬШЕ глубины, и никакая ось этого не исправит.
        /// <para>
        /// Группа нужна на префабе ВИДА, а не на самом риге: у `BoneUnit_Standart` её нет и быть не
        /// должно — он вкладывается внутрь вида, и группа стоит выше него.
        /// </para>
        /// </summary>
        [Test]
        public void EverySkeletalUnitPrefab_HasSortingGroup_SoPartsStayLocal()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs" });
            int checkedPrefabs = 0;

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                var body = go.GetComponentInChildren<Guildmaster.Presentation.Body.SkeletalBodyVisual>(true);
                if (body == null) continue;                      // покадровые юниты: тело — один спрайт

                var group = body.GetComponentInParent<SortingGroup>(true);
                if (group == null && !IsBareRigWithoutView(go))
                    Assert.Fail($"{path}: составное тело без SortingGroup. Ордера частей станут " +
                                "глобальными, и части разных юнитов перемешаются между собой.");

                if (group != null) checkedPrefabs++;
            }

            Assert.Greater(checkedPrefabs, 0,
                "Ни одного скелетного префаба вида не найдено — тест перестал что-либо проверять");
        }

        /// <summary>
        /// Голый риг без вида: стенд анимаций пользуется им напрямую, и группы там нет по замыслу —
        /// она стоит на префабе вида, который риг в себя вкладывает. Признак — отсутствие
        /// <c>UnitView</c>: на арену вид выходит только через него.
        /// </summary>
        private static bool IsBareRigWithoutView(GameObject root)
            => root.GetComponentInChildren<Guildmaster.Presentation.UnitView>(true) == null;

        /// <summary>
        /// Части обязаны лежать в ОДНОМ слое с группой. Слой сравнивается раньше ордера даже внутри
        /// группы, поэтому часть, забытая в чужом слое, всплывает над всем телом — и ордер, который ей
        /// выставили, ничего не решит.
        /// </summary>
        [Test]
        public void SkeletalParts_ShareOneSortingLayer_WithTheirGroup()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs" });

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                var body = go.GetComponentInChildren<Guildmaster.Presentation.Body.SkeletalBodyVisual>(true);
                if (body == null) continue;

                var group = body.GetComponentInParent<SortingGroup>(true);
                if (group == null) continue;                     // это ловит тест выше

                foreach (var part in body.GetComponentsInChildren<SpriteRenderer>(true))
                    Assert.AreEqual(group.sortingLayerID, part.sortingLayerID,
                        $"{path}: часть «{part.name}» в слое «{part.sortingLayerName}», а группа — в " +
                        $"«{group.sortingLayerName}». Слой сильнее ордера, часть всплывёт над телом.");
            }
        }
    }
}
