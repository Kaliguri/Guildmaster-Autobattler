using Guildmaster.Core.Input;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Перевод указателя из экрана в мир по текущей камере.
    /// </summary>
    /// <remarks>
    /// <b>Камера ищется лениво и каждый раз, пока не найдётся.</b> Скоуп поднимается раньше сцены арены,
    /// и запомненный на старте <c>null</c> означал бы курсор, застрявший в нуле до перезапуска.
    /// <para>Глубина берётся из положения самой камеры: у ортографической камеры <c>ScreenToWorldPoint</c>
    /// без неё возвращает точку в плоскости камеры, а не в плоскости арены — ошибка, которая выглядит как
    /// «курсор чуть-чуть не там» и ловится только замером.</para>
    /// </remarks>
    public sealed class PointerWorld : IPointerWorld
    {
        private readonly IInputService _input;

        private Camera _camera;

        public PointerWorld(IInputService input) => _input = input;

        public bool IsAvailable => Resolve() != null;

        public Vector2 Position
        {
            get
            {
                Camera camera = Resolve();
                if (camera == null || _input == null) return Vector2.zero;

                Vector2 screen = _input.PointerScreenPosition;
                Vector3 world  = camera.ScreenToWorldPoint(
                    new Vector3(screen.x, screen.y, -camera.transform.position.z));

                return new Vector2(world.x, world.y);
            }
        }

        private Camera Resolve()
        {
            if (_camera == null) _camera = Camera.main;
            return _camera;
        }
    }
}
