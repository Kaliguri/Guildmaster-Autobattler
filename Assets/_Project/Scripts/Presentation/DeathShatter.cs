using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Разлёт спрайта юнита на осколки при смерти. Забирает ТЕКУЩИЙ кадр спрайта (текстуру + uv-rect атласа,
    /// тинт, размер, разворот) и рисует его общим шаттер-мешем (<see cref="ShatterMesh"/>) с шейдером
    /// Guildmaster/Sprite/Shatter: сначала вспышка в белый, затем разлёт треугольников (per-instance через
    /// MaterialPropertyBlock — материал общий, без аллокаций). По завершении зовёт callback и самоуничтожается.
    /// Только презентация; сим не трогает.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class DeathShatter : MonoBehaviour
    {
        private static readonly int MainTexId    = Shader.PropertyToID("_MainTex");
        private static readonly int MainTexStId  = Shader.PropertyToID("_MainTex_ST");
        private static readonly int ColorId      = Shader.PropertyToID("_Color");
        private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
        private static readonly int FlashAmtId   = Shader.PropertyToID("_FlashAmount");
        private static readonly int ShatterId    = Shader.PropertyToID("_Shatter");
        private static readonly int ExplodeId    = Shader.PropertyToID("_Explode");
        private static readonly int GravityId    = Shader.PropertyToID("_Gravity");
        private static readonly int SpinId       = Shader.PropertyToID("_Spin");
        private static readonly int SpreadId     = Shader.PropertyToID("_Spread");

        private static Material _sharedMat;

        private MeshRenderer          _mr;
        private MaterialPropertyBlock _mpb;
        private System.Action         _onComplete;
        private float _flashIn, _duration, _elapsed;
        private bool  _running;

        /// <summary>Запустить разлёт из текущего состояния спрайта <paramref name="src"/>.</summary>
        public void Play(SpriteRenderer src, Design.CombatFeelConfig cfg, System.Action onComplete)
        {
            _onComplete = onComplete;
            _flashIn  = cfg != null ? cfg.ShatterFlashIn  : 0.08f;
            _duration = cfg != null ? cfg.ShatterDuration : 0.55f;

            var mf = GetComponent<MeshFilter>();
            _mr = GetComponent<MeshRenderer>();
            mf.sharedMesh      = ShatterMesh.GetShared();
            _mr.sharedMaterial = SharedMaterial();
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows    = false;
            // 2D-сортировка как у исходного спрайта — иначе осколки уедут по глубине.
            _mr.sortingLayerID = src.sortingLayerID;
            _mr.sortingOrder   = src.sortingOrder;

            // Позиция/масштаб под ТЕКУЩИЙ рендер спрайта (учитывает сквош/размер). Меш в [-0.5..0.5],
            // масштабируем в мировой размер AABB; знак X — по флипу (Cull Off, winding не важен).
            Bounds b = src.bounds; // мировой AABB
            transform.position = b.center;
            transform.rotation = Quaternion.identity;
            Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            float sx = Mathf.Approximately(parentScale.x, 0f) ? 1f : parentScale.x;
            float sy = Mathf.Approximately(parentScale.y, 0f) ? 1f : parentScale.y;
            transform.localScale = new Vector3(
                (src.flipX ? -b.size.x : b.size.x) / sx,
                b.size.y / sy,
                1f);

            _mpb = new MaterialPropertyBlock();
            _mr.GetPropertyBlock(_mpb);

            Sprite sprite = src.sprite;
            if (sprite != null && sprite.texture != null)
            {
                _mpb.SetTexture(MainTexId, sprite.texture);
                Rect tr = sprite.textureRect;
                float tw = sprite.texture.width, th = sprite.texture.height;
                // uv-rect кадра в атласе: [0..1] меша → под-прямоугольник текстуры.
                _mpb.SetVector(MainTexStId, new Vector4(tr.width / tw, tr.height / th, tr.x / tw, tr.y / th));
            }

            Color tint = src.color; tint.a = 1f;
            _mpb.SetColor(ColorId, tint);
            _mpb.SetColor(FlashColorId, cfg != null ? cfg.FlashColor : Color.white);
            _mpb.SetFloat(ExplodeId, cfg != null ? cfg.ShatterExplode : 1.6f);
            _mpb.SetFloat(GravityId, cfg != null ? cfg.ShatterGravity : 3f);
            _mpb.SetFloat(SpinId,    cfg != null ? cfg.ShatterSpin    : 6f);
            _mpb.SetFloat(SpreadId,  cfg != null ? cfg.ShatterSpread  : 0.8f);
            _mpb.SetFloat(FlashAmtId, 0f);
            _mpb.SetFloat(ShatterId,  0f);
            _mr.SetPropertyBlock(_mpb);

            _elapsed = 0f;
            _running = true;
        }

        private void Update()
        {
            if (!_running) return;

            // Масштабируем по игровому времени → в финальном slowmo осколки летят медленно (в такт моменту).
            _elapsed += Time.deltaTime;

            float shatter = _duration > 0f ? Mathf.Clamp01((_elapsed - _flashIn) / _duration) : 1f;
            float flash = _elapsed < _flashIn
                ? (_flashIn > 0f ? Mathf.Clamp01(_elapsed / _flashIn) : 1f) // 0→1 вспышка в белый
                : Mathf.Lerp(1f, 0.3f, shatter);                            // держим бело, слегка спадая

            _mr.GetPropertyBlock(_mpb);
            _mpb.SetFloat(FlashAmtId, flash);
            _mpb.SetFloat(ShatterId, shatter);
            _mr.SetPropertyBlock(_mpb);

            if (_elapsed >= _flashIn + _duration)
            {
                _running = false;
                _onComplete?.Invoke();
                Destroy(gameObject);
            }
        }

        // Общий рантайм-материал шаттер-шейдера (один на всех; per-instance данные — через MPB).
        private static Material SharedMaterial()
        {
            if (_sharedMat == null)
            {
                Shader sh = Shader.Find("Guildmaster/Sprite/Shatter");
                _sharedMat = new Material(sh) { name = "MAT_Shatter_Runtime" };
            }
            return _sharedMat;
        }
    }
}
