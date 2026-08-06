using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Стек слоёв скелетного юнита — шов между КОДОМ и АССЕТОМ, и живёт он на строках: <c>UnitView</c>
    /// ищет слои по именам («Action», «ActionHips», «Block»), а стейты — по именам стейтов. Обе стороны
    /// правятся порознь, и расхождение здесь молчит худшим из возможных способов: слой просто не находится,
    /// показ честно откатывается на путь покадрового бестиария, и удар снова начинает вытеснять бег вместо
    /// того, чтобы лечь поверх него. Ищется это только глазами и только на арене.
    /// </summary>
    public sealed class BoneUnitLayerStackTests
    {
        // Контроллер один — тот, что играет в бою у всего ростера. Их было два, «боевой» и
        // «лабораторный», и держались они в одном стеке намеренно: приёмка снимала кадры того же стека,
        // что играет в бою. Имена при этом врали наоборот — играл ЛАБОРАТОРНЫЙ, а «боевой» сидел на
        // дев-дуэлянте; 06.08.2026 дуэлянт и его контроллер удалены, оставшийся назван честно.
        private const string ControllerPath = "Assets/_Project/Prefabs/Bones/BoneUnit_SwordShield.controller";

        private const string ActionLayer     = "Action";
        private const string ActionHipsLayer = "ActionHips";
        private const string BlockLayer      = "Block";

        // Стейты, которые UnitView просит на слое действия по имени (см. SwingHash).
        private static readonly string[] RequiredSwings = { "Attack", "AttackCharge" };

        [TestCase(ControllerPath)]
        public void ActionLayer_OverridesArmsAboveTheBase(string path)
        {
            AnimatorController controller = Load(path);
            int index = IndexOf(controller, ActionLayer);

            Assert.Greater(index, 0, $"{ActionLayer} обязан лежать ПОВЕРХ базы: база — это ноги.");

            AnimatorControllerLayer layer = controller.layers[index];
            Assert.AreEqual(AnimatorLayerBlendingMode.Override, layer.blendingMode,
                $"{ActionLayer} перекрывает руки позой удара — это Override, не аддитив.");
            Assert.NotNull(layer.avatarMask,
                $"У {ActionLayer} нет маски: без неё свинг занимает всё тело и ноги перестают бежать.");

            string[] states = layer.stateMachine.states.Select(s => s.state.name).ToArray();
            foreach (string swing in RequiredSwings)
                Assert.Contains(swing, states,
                    $"UnitView просит на слое {ActionLayer} стейт '{swing}' по имени.");
        }

        /// <summary>
        /// Таз идёт АДДИТИВОМ и синхронизирован со слоем рук. Override поставил бы таз в позу клипа удара и
        /// стёр качание бега (замер 30.07: −0.022 против 0.038 − 0.037 = 0.001 у аддитива), а рассинхрон со
        /// слоем рук развёл бы дельту таза и сам удар по разным моментам.
        /// </summary>
        [TestCase(ControllerPath)]
        public void ActionHips_AddsDeltaOnTopOfWhateverTheLegsAreDoing(string path)
        {
            AnimatorController controller = Load(path);
            int hips   = IndexOf(controller, ActionHipsLayer);
            int action = IndexOf(controller, ActionLayer);

            Assert.GreaterOrEqual(hips, 0, $"Нет слоя {ActionHipsLayer}.");

            AnimatorControllerLayer layer = controller.layers[hips];
            Assert.AreEqual(AnimatorLayerBlendingMode.Additive, layer.blendingMode,
                $"{ActionHipsLayer} кладёт ДЕЛЬТУ поверх качания бега — Override стёр бы его.");
            Assert.AreEqual(action, layer.syncedLayerIndex,
                $"{ActionHipsLayer} обязан быть синхронизирован именно с {ActionLayer}.");
        }

        /// <summary>
        /// Синхронизированный слой НЕ наследует клипы исходного — он получает свои через
        /// <c>SetOverrideMotion</c>, и без них молча играет пустоту (первый замер аддитивного таза дал
        /// дельту ровно 0.000 именно поэтому). Стейт без override-моушена здесь — не «пока не дошли руки»,
        /// а неработающий слой.
        /// </summary>
        [TestCase(ControllerPath)]
        public void ActionHips_CarriesItsOwnMotionForEverySwing(string path)
        {
            AnimatorController controller = Load(path);
            AnimatorControllerLayer hips  = controller.layers[IndexOf(controller, ActionHipsLayer)];
            AnimatorStateMachine   arms   = controller.layers[IndexOf(controller, ActionLayer)].stateMachine;

            foreach (ChildAnimatorState child in arms.states)
                Assert.NotNull(hips.GetOverrideMotion(child.state),
                    $"У стейта '{child.state.name}' нет override-моушена на {ActionHipsLayer} — " +
                    "синхронизированный слой сыграет пустоту, и дельта таза будет ровно нулевой.");
        }

        [TestCase(ControllerPath)]
        public void GuardLayer_StaysItsOwnLane(string path)
        {
            AnimatorController controller = Load(path);
            int index = IndexOf(controller, BlockLayer);

            Assert.Greater(index, 0, $"{BlockLayer} — надстройка над телом, а не база.");
            Assert.NotNull(controller.layers[index].avatarMask,
                $"У {BlockLayer} нет маски: щит занял бы собой всё тело.");
        }

        /// <summary>
        /// Ни один слой-надстройка не имеет права стартовать с весом: вес им выдаёт показ по состоянию сима.
        /// Слой, приехавший с единицей из ассета, держит позу удара на юните, который ещё ничего не делал.
        /// </summary>
        [TestCase(ControllerPath)]
        public void OverlayLayers_StartSilent(string path)
        {
            AnimatorController controller = Load(path);

            foreach (string name in new[] { ActionLayer, ActionHipsLayer, BlockLayer })
                Assert.AreEqual(0f, controller.layers[IndexOf(controller, name)].defaultWeight, 1e-4f,
                    $"Слой {name} обязан молчать, пока показ не даст ему вес.");
        }

        private static AnimatorController Load(string path)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            Assert.NotNull(controller, $"Не найден контроллер {path}.");
            return controller;
        }

        private static int IndexOf(AnimatorController controller, string layerName)
        {
            for (int i = 0; i < controller.layers.Length; i++)
                if (controller.layers[i].name == layerName) return i;

            Assert.Fail($"В {controller.name} нет слоя '{layerName}' — UnitView ищет его по этому имени.");
            return -1;
        }
    }
}
