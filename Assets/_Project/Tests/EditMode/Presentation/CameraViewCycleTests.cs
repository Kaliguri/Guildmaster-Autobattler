using Guildmaster.Core.Settings;
using Guildmaster.Presentation;
using NUnit.Framework;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Контракт видов камеры: <b>сценарная камера существует только для идущего боя</b>, а выбор игрока
    /// между слежением и свободой переживает бой и запуск игры. Оба факта живут между файлами (камера,
    /// фаза боя, настройки) — комментарий увидела бы только одна сторона шва.
    /// </summary>
    public sealed class CameraViewCycleTests
    {
        [Test]
        public void Tab_вне_боя_не_приводит_в_сценарный_вид()
        {
            // Единственный вид вне боя — свободный: следить сценарной камере там не за кем.
            Assert.That(CameraModeController.NextMode(CameraMode.Overview, devAccess: false, inCombat: false),
                        Is.EqualTo(CameraMode.Overview), "Tab вне боя увёл в слежение за пустой ареной");
            Assert.That(CameraModeController.NextMode(CameraMode.Dev, devAccess: true, inCombat: false),
                        Is.EqualTo(CameraMode.Overview));
        }

        [Test]
        public void Tab_в_бою_циклит_слежение_и_свободу()
        {
            Assert.That(CameraModeController.NextMode(CameraMode.Action, devAccess: false, inCombat: true),
                        Is.EqualTo(CameraMode.Overview));
            Assert.That(CameraModeController.NextMode(CameraMode.Overview, devAccess: false, inCombat: true),
                        Is.EqualTo(CameraMode.Action));
        }

        [Test]
        public void Dev_камера_вклинивается_в_цикл_только_с_доступом()
        {
            Assert.That(CameraModeController.NextMode(CameraMode.Overview, devAccess: true, inCombat: true),
                        Is.EqualTo(CameraMode.Dev), "с dev-доступом Tab обязан заводить в dev-камеру");
            Assert.That(CameraModeController.NextMode(CameraMode.Dev, devAccess: true, inCombat: true),
                        Is.EqualTo(CameraMode.Action), "из dev в бою возвращаемся к слежению");
        }

        [Test]
        public void Сценарный_вид_из_цикла_всегда_ведёт_в_свободный()
        {
            // Action в цикле — единственный вид, чей следующий шаг не зависит ни от боя, ни от dev-доступа.
            foreach (bool dev in new[] { false, true })
            foreach (bool fight in new[] { false, true })
                Assert.That(CameraModeController.NextMode(CameraMode.Action, dev, fight),
                            Is.EqualTo(CameraMode.Overview));
        }

        [Test]
        public void Первый_запуск_смотрит_бой_сценарной_камерой()
        {
            // Дефолт держит GameplaySettings, а не поле камеры: выбор игрока персистится там же.
            Assert.That(GameplaySettings.Defaults().FreeCombatCamera, Is.False);
        }
    }
}
