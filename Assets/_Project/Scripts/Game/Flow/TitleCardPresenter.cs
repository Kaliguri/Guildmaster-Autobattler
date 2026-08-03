using System;
using Cysharp.Threading.Tasks;
using Guildmaster.Guild;
using MessagePipe;

namespace Guildmaster.Game.Flow
{
    /// <summary>Показ boot title card до главного меню и ожидание закрытия.</summary>
    public interface ITitleCardPresenter
    {
        /// <summary>
        /// Показать бут-экран, прогнать под ним <paramref name="loading"/> и ждать, пока игрок его
        /// закроет.
        /// </summary>
        /// <remarks>
        /// <b>Загрузка идёт ПОД экраном, а не до него</b>, и порядок здесь не косметика. Мир
        /// поднимается с собственной камерой и со следующего кадра рисует свой фон; пока бут-экран
        /// строился после загрузки, между ними успевало мелькнуть несколько кадров пустой арены
        /// (наход. Макса 03.08.2026). Заодно надпись «Загрузка» и знак рядом с ней перестали быть
        /// декорацией: под ними теперь и правда идёт загрузка.
        /// <para>Нажатие во время загрузки не теряется и не обрывает её: ожидание закрытия просто
        /// пройдёт мгновенно, когда работа кончится.</para>
        /// </remarks>
        UniTask ShowAsync(Func<UniTask> loading);
    }

    /// <summary>
    /// Презентер boot title card: публикует <see cref="OpenTitleCardRequest"/> и ждёт dismiss.
    /// <para><b>Слушатель UI обязателен</b> — см. разбор у <see cref="MainMenuPresenter"/>. Это ПЕРВЫЙ await
    /// петли игры, поэтому без подписчика игра встаёт на чёрном экране ещё до главного меню.</para>
    /// </summary>
    public sealed class TitleCardPresenter : ITitleCardPresenter
    {
        private readonly IPublisher<OpenTitleCardRequest> _pub;

        public TitleCardPresenter(IPublisher<OpenTitleCardRequest> pub) => _pub = pub;

        public async UniTask ShowAsync(Func<UniTask> loading)
        {
            var tcs = new UniTaskCompletionSource();
            _pub.Publish(new OpenTitleCardRequest(() => tcs.TrySetResult()));

            if (loading != null) await loading();

            await tcs.Task;
        }
    }
}
