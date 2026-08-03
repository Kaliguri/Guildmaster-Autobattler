using System.Runtime.CompilerServices;

// Мутаторы забега internal: снаружи в RunState пишут только через шину команд (IRunCommands), и барьер
// сборки держит это вместо комментария. Тестам доступ нужен на оба уровня — и на шину, и на сам мутатор
// (валидация слота, границы вместимости), поэтому они видят internal напрямую.
[assembly: InternalsVisibleTo("Guildmaster.Tests.EditMode")]
