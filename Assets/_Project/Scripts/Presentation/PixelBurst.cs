using Guildmaster.Presentation.Design;
using UnityEngine;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Один пиксельный «брызг» частиц (hit-spark / muzzle / dust / heal). Рисует общий меш
    /// (<see cref="PixelBurstMesh"/>) шейдером Guildmaster/VFX/PixelBurst; форму задаёт <see cref="PixelBurstPreset"/>
    /// через MaterialPropertyBlock (материал общий, без аллокаций). Гонит прогресс жизни на игровом времени
    /// (в slowmo замедляется), по завершении зовёт callback (возврат в пул). Только презентация; сим не трогает.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PixelBurst : MonoBehaviour
    {
        private static readonly int ColorId     = Shader.PropertyToID("_Color");
        private static readonly int LifeId      = Shader.PropertyToID("_Life");
        private static readonly int SpeedId     = Shader.PropertyToID("_Speed");
        private static readonly int GravityId   = Shader.PropertyToID("_Gravity");
        private static readonly int ConeHalfId  = Shader.PropertyToID("_ConeHalf");
        private static readonly int BaseDirId   = Shader.PropertyToID("_BaseDir");
        private static readonly int CountFracId = Shader.PropertyToID("_CountFrac");
        private static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");

        private static Material _sharedMat;

        private MeshRenderer          _mr;
        private MaterialPropertyBlock _mpb;
        private System.Action<PixelBurst> _onComplete;
        private float _life, _elapsed;
        private bool  _running;

        private void Awake()
        {
            var mf = GetComponent<MeshFilter>();
            _mr = GetComponent<MeshRenderer>();
            mf.sharedMesh      = PixelBurstMesh.GetShared();
            _mr.sharedMaterial = SharedMaterial();
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows    = false;
            _mpb = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Проиграть брызг в точке <paramref name="worldPos"/>, конус направлен по <paramref name="baseDirDeg"/>
        /// (для 360° неважен). <paramref name="intensity"/> ∈ [0..1] режет кол-во частиц (лёгкий удар — меньше).
        /// </summary>
        public void Play(Vector3 worldPos, float baseDirDeg, PixelBurstPreset p, float intensity,
                         int sortingLayerId, int sortingOrder, System.Action<PixelBurst> onComplete)
        {
            _onComplete = onComplete;
            _life = Mathf.Max(0.01f, p.Life);
            _elapsed = 0f;
            _running = true;

            transform.position = worldPos;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            _mr.sortingLayerID = sortingLayerId;
            _mr.sortingOrder   = sortingOrder;

            float rad = baseDirDeg * Mathf.Deg2Rad;
            int count = Mathf.Clamp(Mathf.RoundToInt(p.Count * Mathf.Clamp01(intensity)), 1, PixelBurstMesh.Count);

            _mr.GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorId, p.Color);
            _mpb.SetVector(BaseDirId, new Vector4(Mathf.Cos(rad), Mathf.Sin(rad), 0f, 0f));
            _mpb.SetFloat(SpeedId, p.Speed);
            _mpb.SetFloat(GravityId, p.Gravity);
            _mpb.SetFloat(ConeHalfId, p.SpreadDeg * 0.5f * Mathf.Deg2Rad);
            _mpb.SetFloat(CountFracId, count / (float)PixelBurstMesh.Count);
            _mpb.SetFloat(PixelSizeId, p.Size);
            _mpb.SetFloat(LifeId, 0f);
            _mr.SetPropertyBlock(_mpb);
        }

        private void Update()
        {
            if (!_running) return;

            _elapsed += Time.deltaTime; // игровое время → в slowmo частицы летят медленнее (в такт моменту)
            float life = Mathf.Clamp01(_elapsed / _life);

            _mr.GetPropertyBlock(_mpb);
            _mpb.SetFloat(LifeId, life);
            _mr.SetPropertyBlock(_mpb);

            if (_elapsed >= _life)
            {
                _running = false;
                _onComplete?.Invoke(this);
            }
        }

        private static Material SharedMaterial()
        {
            if (_sharedMat == null)
            {
                Shader sh = Shader.Find("Guildmaster/VFX/PixelBurst");
                _sharedMat = new Material(sh) { name = "MAT_PixelBurst_Runtime" };
            }
            return _sharedMat;
        }
    }
}
