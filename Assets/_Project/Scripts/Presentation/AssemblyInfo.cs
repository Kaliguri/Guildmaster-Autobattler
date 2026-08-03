using System.Runtime.CompilerServices;

// Тесты видят internal-члены Presentation: правила вида камеры (CameraModeController.NextMode) —
// инвариант между камерой и фазой боя, и держать его должен тест, а не публичный API для чужих.
[assembly: InternalsVisibleTo("Guildmaster.Tests.EditMode")]
