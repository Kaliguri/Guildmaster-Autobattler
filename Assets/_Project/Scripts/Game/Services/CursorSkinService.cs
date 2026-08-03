using Guildmaster.Data.Definitions;
using UnityEngine;
using VContainer.Unity;

namespace Guildmaster.Game.Services
{
    /// <summary>
    /// Владелец «каким курсором мы играем»: ставит системный курсор и отдаёт изображение тем, кто
    /// рисует чужие.
    /// </summary>
    /// <remarks>
    /// <b>Свой курсор — системный, а не нарисованный в UI.</b> Нарисованный отстаёт от мыши на кадр
    /// показа, и это чувствуется как «мышь залипает», сколько ни оптимизируй. Чужие курсоры такой
    /// проблемы не имеют: они и так приходят с задержкой сети, и рисовать их в UI единственно верно.
    /// <para><b>Один владелец на оба применения.</b> Своё изображение и чужое обязаны совпадать — иначе
    /// игрок видит у себя одну стрелку, а напарник у него другую, и разговор «наведи на лучника»
    /// перестаёт работать.</para>
    /// <para><b>Выбор игрока приезжает из профиля</b> — но экран профиля ещё не написан, поэтому пока
    /// применяется умолчание набора. Место подключения одно (<see cref="Apply"/>), и подключается оно
    /// вместе с экраном, а не раньше: пустой сеттер «на будущее» вводил бы в заблуждение.</para>
    /// </remarks>
    public sealed class CursorSkinService : IStartable
    {
        private readonly CursorSkinCatalog _catalog;

        private CursorSkinData _current;

        public CursorSkinService(CursorSkinCatalog catalog) => _catalog = catalog;

        /// <summary>Чем играем сейчас. <c>null</c> — набор пуст, и курсор остаётся системным.</summary>
        public CursorSkinData Current => _current;

        public void Start() => Apply(null);

        /// <summary>
        /// Надеть скин по id. Неизвестный id даёт умолчание набора: id приходит из профиля и по сети,
        /// то есть снаружи, а игрок без курсора остаться не может.
        /// </summary>
        public void Apply(string skinId)
        {
            if (_catalog == null) return;

            CursorSkinData skin = _catalog.Resolve(skinId);
            _current = skin;

            if (skin == null || skin.Texture == null)
            {
                Debug.LogWarning("[CursorSkinService] в наборе нет ни одного скина с текстурой — " +
                                 "курсор остаётся системным");
                return;
            }

            // Auto, а не ForceSoftware: аппаратный курсор не зависит от частоты кадров, и на просадке
            // он единственный продолжает двигаться за рукой.
            UnityEngine.Cursor.SetCursor(skin.Texture, skin.HotspotPixels, CursorMode.Auto);
        }

        /// <summary>Изображение скина по id — им рисуются курсоры остальных игроков.</summary>
        public Texture2D TextureOf(string skinId) => _catalog != null ? _catalog.Resolve(skinId)?.Texture : null;
    }
}
