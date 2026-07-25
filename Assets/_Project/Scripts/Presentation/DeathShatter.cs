using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Разлёт спрайта юнита на осколки при смерти. Берёт ТЕКУЩИЙ кадр спрайта и строит меш строго под его
    /// ВИДИМЫЕ границы (из собственного меша спрайта — <c>sprite.vertices</c>/<c>sprite.uv</c>), поэтому осколки
    /// размером с персонажа, а не с прозрачной спрайт-ячейки. Рисует шейдером Guildmaster/Sprite/Shatter:
    /// вспышка в белый → осколки дрейфуют наружу во все стороны, медленно вращаясь, и плавно тают. Разлёт нормирован
    /// на высоту спрайта. Per-instance данные через MaterialPropertyBlock (материал общий, без аллокаций).
    /// По завершении зовёт callback и самоуничтожается. Только презентация; сим не трогает.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class DeathShatter : MonoBehaviour
    {
        private static readonly int MainTexId    = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId      = Shader.PropertyToID("_Color");
        private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
        private static readonly int FlashAmtId   = Shader.PropertyToID("_FlashAmount");
        private static readonly int ShatterId    = Shader.PropertyToID("_Shatter");
        private static readonly int ExplodeId    = Shader.PropertyToID("_Explode");
        private static readonly int GravityId    = Shader.PropertyToID("_Gravity");
        private static readonly int SpinId       = Shader.PropertyToID("_Spin");
        private static readonly int SpreadId     = Shader.PropertyToID("_Spread");
        private static readonly int TumbleId     = Shader.PropertyToID("_Tumble");
        private static readonly int UpBiasId     = Shader.PropertyToID("_UpBias");
        private static readonly int EmberColorId = Shader.PropertyToID("_EmberColor");
        private static readonly int EmberBoostId = Shader.PropertyToID("_EmberBoost");
        private static readonly int EmberStartId = Shader.PropertyToID("_EmberStart");

        private static Material _sharedMat;

        private MeshRenderer          _mr;
        private MeshFilter            _mf;
        private Mesh                  _mesh;   // per-instance — уничтожаем вместе с эффектом
        private MaterialPropertyBlock _mpb;
        private System.Action         _onComplete;
        private float _flashIn, _flashOut, _duration, _hold, _elapsed;
        private bool  _running;

        /// <summary>Запустить разлёт из текущего состояния спрайта <paramref name="src"/>.</summary>
        public void Play(SpriteRenderer src, Design.CombatFeelConfig cfg, System.Action onComplete)
        {
            _onComplete = onComplete;
            _flashIn  = cfg != null ? cfg.ShatterFlashIn  : 0.08f;
            _flashOut = cfg != null ? cfg.ShatterFlashOut : 0.12f;
            _duration = cfg != null ? cfg.ShatterDuration : 0.75f;
            _hold     = cfg != null ? Mathf.Max(0f, cfg.ShatterHold) : 0.05f;

            Sprite sprite = src.sprite;

            // Тесные видимые границы кадра из собственного меша спрайта (в лок. ед. спрайта = пространстве Body).
            // UV берём из его же uv — 1:1, без размазывания. Фолбэк на полный rect, если меш пуст.
            Vector2 sizeLocal, centerLocal, uvMin, uvSize;
            ComputeTightRect(sprite, out sizeLocal, out centerLocal, out uvMin, out uvSize);

            // Размер видимой области в ИСХОДНЫХ пикселях — для снапа границ чанков на пиксель-сетку.
            Vector2 regionPixels = Vector2.one * 16f;
            if (sprite != null && sprite.texture != null)
                regionPixels = new Vector2(uvSize.x * sprite.texture.width, uvSize.y * sprite.texture.height);

            int blockPx = cfg != null ? cfg.ShatterBlockPixels : 6;
            _mesh = ShatterMesh.Build(sizeLocal, new Rect(uvMin.x, uvMin.y, uvSize.x, uvSize.y), regionPixels, blockPx);

            _mf = GetComponent<MeshFilter>();
            _mr = GetComponent<MeshRenderer>();
            _mf.sharedMesh     = _mesh;
            _mr.sharedMaterial = SharedMaterial();
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows    = false;
            _mr.sortingLayerID = src.sortingLayerID; // 2D-сортировка как у спрайта
            _mr.sortingOrder   = src.sortingOrder;

            // Осколки спавнятся СИБЛИНГОМ спрайта (тот же родитель — Visual Sprite), поэтому local-масштаб берём
            // прямо у спрайта. Якорь — ВИДИМЫЙ центр кадра; при flipX он зеркалится вокруг пивота по X, т.к.
            // SpriteRenderer.flipX отражает МЕШ, а не трансформ — без этого для отражённых (враги) осколки уезжали вбок.
            Transform bt = src.transform;
            float signX = src.flipX ? -1f : 1f;
            transform.position   = bt.TransformPoint(new Vector3(signX * centerLocal.x, centerLocal.y, 0f));
            transform.rotation   = bt.rotation;
            Vector3 ls = bt.localScale;
            transform.localScale = new Vector3(signX * ls.x, ls.y, 1f); // знак X — флип меша под flipX

            _mpb = new MaterialPropertyBlock();
            _mr.GetPropertyBlock(_mpb);
            if (sprite != null && sprite.texture != null) _mpb.SetTexture(MainTexId, sprite.texture);

            Color tint = src.color; tint.a = 1f;
            _mpb.SetColor(ColorId, tint);
            _mpb.SetColor(FlashColorId, cfg != null ? cfg.FlashColor : Color.white);

            // Разлёт/гравитация нормированы на ВЫСОТУ спрайта (лок. ед.) — одинаково ощущается на любом размере.
            float h = Mathf.Max(0.0001f, sizeLocal.y);
            _mpb.SetFloat(ExplodeId, (cfg != null ? cfg.ShatterExplode : 1.2f) * h);
            _mpb.SetFloat(GravityId, (cfg != null ? cfg.ShatterGravity : 0f)  * h);
            _mpb.SetFloat(SpinId,    cfg != null ? cfg.ShatterSpin   : 3f);
            _mpb.SetFloat(SpreadId,  cfg != null ? cfg.ShatterSpread : 1.2f);
            _mpb.SetFloat(TumbleId,  cfg != null ? cfg.ShatterTumble : 9f);
            _mpb.SetFloat(UpBiasId,  cfg != null ? cfg.ShatterUpBias : 0.6f);
            _mpb.SetColor(EmberColorId, cfg != null ? cfg.ShatterEmberColor : new Color(0.25f, 0.9f, 1f, 1f));
            _mpb.SetFloat(EmberBoostId, cfg != null ? cfg.ShatterEmberBoost : 2f);
            _mpb.SetFloat(EmberStartId, cfg != null ? cfg.ShatterEmberStart : 0.4f);
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

            float postHold = _elapsed - _hold;
            float shatter;
            float flash;
            if (postHold < 0f)
            {
                // Микро-hold: осколки стоят, полная вспышка («кристалл»).
                shatter = 0f;
                flash = 1f;
            }
            else
            {
                shatter = _duration > 0f ? Mathf.Clamp01((postHold - _flashIn) / _duration) : 1f;
                if (_flashIn <= 0f)
                    flash = Mathf.Clamp01(1f - postHold / Mathf.Max(0.0001f, _flashOut));
                else if (postHold < _flashIn)
                    flash = postHold / _flashIn;
                else
                    flash = Mathf.Clamp01(1f - (postHold - _flashIn) / Mathf.Max(0.0001f, _flashOut));
            }

            _mr.GetPropertyBlock(_mpb);
            _mpb.SetFloat(FlashAmtId, flash);
            _mpb.SetFloat(ShatterId, shatter);
            _mr.SetPropertyBlock(_mpb);

            if (_elapsed >= _hold + _flashIn + _duration)
            {
                _running = false;
                _onComplete?.Invoke();
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh); // per-instance меш — чистим за собой
        }

        // Тесные видимые границы кадра + соответствующий UV-прямоугольник (из меша спрайта). Фолбэк — полный rect.
        private static void ComputeTightRect(Sprite sprite, out Vector2 size, out Vector2 center, out Vector2 uvMin, out Vector2 uvSize)
        {
            if (sprite != null && sprite.vertices != null && sprite.vertices.Length >= 3)
            {
                Vector2[] v = sprite.vertices;
                Vector2[] uv = sprite.uv;
                Vector2 vMin = v[0], vMax = v[0], uMin = uv[0], uMax = uv[0];
                for (int i = 1; i < v.Length; i++)
                {
                    vMin = Vector2.Min(vMin, v[i]); vMax = Vector2.Max(vMax, v[i]);
                    uMin = Vector2.Min(uMin, uv[i]); uMax = Vector2.Max(uMax, uv[i]);
                }
                size   = vMax - vMin;
                center = (vMin + vMax) * 0.5f;
                uvMin  = uMin;
                uvSize = uMax - uMin;
                return;
            }

            // Фолбэк: полный прямоугольник спрайта.
            if (sprite != null)
            {
                size   = sprite.bounds.size;
                center = sprite.bounds.center;
            }
            else { size = Vector2.one; center = Vector2.zero; }
            uvMin  = Vector2.zero;
            uvSize = Vector2.one;
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
