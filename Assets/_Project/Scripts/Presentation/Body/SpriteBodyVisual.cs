using UnityEngine;

namespace Guildmaster.Presentation.Body
{
    /// <summary>
    /// Тело из ОДНОГО спрайта — покадровые юниты (17 существующих префабов). Обёртка вокруг того самого
    /// <see cref="SpriteRenderer"/>, который <c>UnitView</c> и раньше держал полем, поэтому префабы под шов
    /// не переразводятся: нет компонента тела — вид собирает эту реализацию сам.
    /// </summary>
    public sealed class SpriteBodyVisual : IUnitBodyVisual
    {
        private readonly SpriteRenderer _sprite;
        private MaterialPropertyBlock _mpb;
        private BodyVisualState _lastState;
        private bool _effectApplied;   // держим ли сейчас блок с эффектом (чтобы вернуть в 0 РОВНО один раз)

        public SpriteBodyVisual(SpriteRenderer sprite) => _sprite = sprite;

        public bool HasContent => _sprite != null && _sprite.sprite != null;

        /// <summary>
        /// Родитель спрайта — узел «Sprite Visual» префаба. Именно он выше <see cref="Animator"/>, поэтому
        /// сплющивание живёт на нём: на самом спрайте клип затирал бы масштаб каждым кадром.
        /// </summary>
        public Transform Root => _sprite == null
            ? null
            : (_sprite.transform.parent != null ? _sprite.transform.parent : _sprite.transform);

        public int SortingLayerId => _sprite != null ? _sprite.sortingLayerID : 0;
        public int SortingOrder   => _sprite != null ? _sprite.sortingOrder   : 0;
        public bool IsFlippedX    => _sprite != null && _sprite.flipX;

        public void Prime(Color flashColor)
        {
            if (_sprite == null) return;
            _mpb ??= new MaterialPropertyBlock();
            _sprite.GetPropertyBlock(_mpb);
            _mpb.SetFloat(BodyShaderIds.FlashAmount, 0f);
            _mpb.SetColor(BodyShaderIds.FlashColor, flashColor);
            _mpb.SetFloat(BodyShaderIds.Holo, 0f);      // вид могли переиспользовать — голограмма не должна дожить
            _mpb.SetFloat(BodyShaderIds.Outline, 0f);
            _mpb.SetFloat(BodyShaderIds.GlowAmount, 0f);
            _sprite.SetPropertyBlock(_mpb);
            _effectApplied = false;
            _lastState     = default;
        }

        public void Apply(in BodyVisualState state)
        {
            if (_sprite == null) return;

            _sprite.color = state.Tint;

            // Блок пишем, только пока эффект активен, плюс один раз чтобы вернуть всё в ноль: лишняя запись
            // выбивает спрайт из SRP-батчинга без всякой пользы.
            bool active = state.HasEffect;
            if (!active && !_effectApplied) return;
            if (active && _effectApplied && state.Equals(_lastState)) return;

            _mpb ??= new MaterialPropertyBlock();
            _sprite.GetPropertyBlock(_mpb);
            // Покадровое тело — один спрайт без ролей частей: у него нет отдельного оружия, поэтому на касте
            // светится целиком (partGlows = true; сама сила приходит из state.HasGlow внутри Write).
            BodyShaderIds.Write(_mpb, state, _sprite.sprite, partGlows: true);
            _sprite.SetPropertyBlock(_mpb);

            _effectApplied = active;
            _lastState     = state;
        }

        public void SetSortingOrder(int order)
        {
            if (_sprite != null) _sprite.sortingOrder = order;
        }

        public void SetFlipX(bool flip)
        {
            if (_sprite != null) _sprite.flipX = flip;
        }

        public bool TryGetBounds(out Bounds bounds)
        {
            if (HasContent) { bounds = _sprite.bounds; return true; }
            bounds = default;
            return false;
        }

        public UnitSilhouette CaptureSilhouette(Vector2 feet)
        {
            if (!HasContent) return UnitSilhouette.None;
            Transform t = _sprite.transform;
            Vector3 offset = t.position - new Vector3(feet.x, feet.y, t.position.z);
            var local = Matrix4x4.TRS(offset, t.rotation, t.lossyScale);
            return new UnitSilhouette(new[]
            {
                new SilhouettePart(_sprite.sprite, local, _sprite.flipX, 0),
            });
        }

        public void SetVisible(bool visible)
        {
            if (_sprite != null) _sprite.enabled = visible;
        }

        public void PlayShatter(Design.CombatFeelConfig feel, Gradient palette, System.Action onComplete)
        {
            if (!HasContent) { onComplete?.Invoke(); return; }

            var go = new GameObject("DeathShatter");
            // Сиблинг спрайта (тот же родитель), а не корень вида: иначе трансформ-пространство не совпадает
            // со спрайтом и осколки спавнятся со смещением.
            go.transform.SetParent(Root, worldPositionStays: false);
            var shatter = go.AddComponent<DeathShatter>();
            shatter.Play(_sprite, feel, palette, onComplete);

            _sprite.enabled = false;   // дальше показывают осколки
        }
    }
}
