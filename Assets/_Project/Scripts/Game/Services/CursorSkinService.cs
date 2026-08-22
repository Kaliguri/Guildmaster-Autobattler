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
    /// <para><b>Выбор игрока приезжает из профиля</b> — там живёт идентичность (ник, цвет, курсор).
    /// На старте надевается сохранённый скин, дальше экран профиля зовёт <see cref="Apply"/> сам, чтобы
    /// игрок увидел выбор сразу, а не после перезахода.</para>
    /// </remarks>
    public sealed class CursorSkinService : Guildmaster.Core.Players.ICursorSkinControl, IStartable
    {
        private readonly CursorSkinCatalog _catalog;
        private readonly Guildmaster.Core.Persistence.IProfileService _profiles;
        // Палитра — единственный владелец цвета: сервис спрашивает у неё оттенок по имени токена и
        // литералов у себя не держит.
        private readonly GuildmasterPalette _palette;

        // Покрашенные копии по ключу «скин + место в наборе». Красим один раз: тонирование идёт по
        // пикселям, а курсор переставляют кадром выбора — без кэша каждый шаг по образцам заводил бы
        // новую текстуру и оставлял прежнюю мусором.
        private readonly System.Collections.Generic.Dictionary<string, Texture2D> _tinted =
            new System.Collections.Generic.Dictionary<string, Texture2D>();

        private CursorSkinData _current;

        public CursorSkinService(CursorSkinCatalog catalog,
                                 Guildmaster.Core.Persistence.IProfileService profiles,
                                 GuildmasterPalette palette)
        {
            _catalog  = catalog;
            _profiles = profiles;
            _palette  = palette;
        }

        /// <summary>Чем играем сейчас. <c>null</c> — набор пуст, и курсор остаётся системным.</summary>
        public CursorSkinData Current => _current;

        public void Start()
        {
            Guildmaster.Core.Persistence.ProfileIdentity identity =
                _profiles?.Identity ?? default;
            Apply(identity.CursorSkinId, identity.ColorIndex);
        }

        /// <summary>
        /// Надеть скин по id. Неизвестный id даёт умолчание набора: id приходит из профиля и по сети,
        /// то есть снаружи, а игрок без курсора остаться не может.
        /// </summary>
        public void Apply(string skinId, int colorIndex)
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
            UnityEngine.Cursor.SetCursor(Dressed(skin, colorIndex), skin.HotspotPixels, CursorMode.Auto);
        }

        /// <summary>
        /// Текстура скина, покрашенная мейн-цветом игрока. Красить нечем — отдаём исходную белую: без
        /// курсора игрок остаться не может, а белый читается на любом фоне.
        /// </summary>
        /// <remarks>
        /// Умножение, ровно как у чужих курсоров в UI-слое: белая заливка становится мейн-цветом, а
        /// чёрная обводка остаётся чёрной и держит фигуру читаемой. Красить цветной скин было бы
        /// нечем — на это и рассчитан контракт <see cref="CursorSkinData"/>.
        /// </remarks>
        private Texture2D Dressed(CursorSkinData skin, int colorIndex)
        {
            if (_palette == null) return skin.Texture;

            string token = Guildmaster.Core.Players.PlayerColors.TokenOf(colorIndex);
            if (!_palette.TryGet(token, out Color tint)) return skin.Texture;

            string key = skin.Id + "#" + token;
            if (_tinted.TryGetValue(key, out Texture2D cached) && cached != null) return cached;

            Texture2D painted = Paint(skin.Texture, tint);
            _tinted[key] = painted;
            return painted != null ? painted : skin.Texture;
        }

        /// <summary>Умножить пиксели на цвет, сохранив прозрачность.</summary>
        /// <remarks>
        /// Текстуры курсоров импортированы читаемыми ровно ради этого: без Read/Write копия снималась
        /// бы через RenderTexture, а это лишний проход по гамме — и цвет курсора разошёлся бы с тем же
        /// цветом в UI, где его накладывает шейдер.
        /// </remarks>
        private static Texture2D Paint(Texture2D source, Color tint)
        {
            if (!source.isReadable)
            {
                Debug.LogWarning($"[CursorSkinService] текстура '{source.name}' импортирована без " +
                                 "Read/Write — покрасить её нечем, курсор останется белым. " +
                                 "Включи Read/Write у скинов курсора в импортере.");
                return null;
            }

            var painted = new Texture2D(source.width, source.height, TextureFormat.RGBA32, mipChain: false)
            {
                name       = source.name + " (tinted)",
                filterMode = source.filterMode,
                wrapMode   = TextureWrapMode.Clamp,
                hideFlags  = HideFlags.HideAndDontSave,
            };

            Color[] pixels = source.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color p = pixels[i];
                pixels[i] = new Color(p.r * tint.r, p.g * tint.g, p.b * tint.b, p.a);
            }

            painted.SetPixels(pixels);
            painted.Apply(updateMipmaps: false);
            return painted;
        }

        /// <summary>Изображение скина по id — им рисуются курсоры остальных игроков.</summary>
        public Texture2D TextureOf(string skinId) => _catalog != null ? _catalog.Resolve(skinId)?.Texture : null;
    }
}
