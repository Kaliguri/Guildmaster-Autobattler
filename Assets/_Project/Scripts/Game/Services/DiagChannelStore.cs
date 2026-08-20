using System;
using Guildmaster.Core.Diagnostics;
using Guildmaster.Core.Persistence;
using Guildmaster.Core.Settings;
using VContainer.Unity;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Помнит, какие каналы диагностики включены, между запусками игры.
    /// </summary>
    /// <remarks>
    /// <b>Обещание было, исполнения не было.</b> Докстринг <see cref="Diag"/> с самого начала говорил
    /// «включённые каналы переживают перезапуск», но у события <c>Changed</c> не оказалось ни одного
    /// подписчика — то есть каждый запуск начинался с тишины, и включать каналы приходилось заново на
    /// обеих машинах. Класс закрывает ровно этот разрыв.
    /// <para><b>Пишем на каждое изменение, а не на выходе.</b> Отладочный прогон нередко кончается
    /// падением или закрытием окна — сохранение «на выходе» терялось бы именно тогда, когда нужнее
    /// всего. Запись мелкая и редкая: её вызывает человек, тыкая тумблер.</para>
    /// </remarks>
    public sealed class DiagChannelStore : IStartable, IDisposable
    {
        private const string SaveKey = "diag";

        private readonly ILocalSaveService _save;

        public DiagChannelStore(ILocalSaveService save) => _save = save;

        public void Start()
        {
            SaveLoadResult<DiagSettings> loaded = _save.TryLoad<DiagSettings>(SaveKey);
            if (loaded.Status == SaveLoadStatus.Ok && loaded.Value != null)
                Diag.Restore((DiagChannel)loaded.Value.Channels);

            // Подписываемся ПОСЛЕ восстановления: иначе первое же событие записало бы обратно то, что
            // мы только что прочитали, — безвредно, но лишняя запись на каждом старте.
            Diag.Changed += Persist;
        }

        public void Dispose() => Diag.Changed -= Persist;

        private void Persist(DiagChannel channels) =>
            _save.Save(SaveKey, new DiagSettings { Channels = (int)channels });
    }
}
