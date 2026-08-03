using Guildmaster.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Сегментированный бар ресурса (мана/ярость) на том же шейдере, что и HP-бар
    /// (<c>Guildmaster/UI/SegmentedHealthBar</c>), но БЕЗ щита: заливка ресурса + насечки фиксированного
    /// значения (<see cref="_tickValue"/> ресурса на минорную насечку, жирная каждые <see cref="_majorTickValue"/>).
    /// Нормировка простая — <c>scale = MaxResource</c> (ресурс не выходит за макс), поэтому частота насечек
    /// постоянна для юнита. Chip-дельта показывает свежую трату/добор.
    ///
    /// <para>Скрывается целиком для безресурсных юнитов (<c>MaxResource ≤ 0</c> — болванчики, безресурсные реликвии).
    /// Динамика гонится в per-instance материал по рендер-времени (НЕ по сим-тику).</para>
    /// </summary>
    public sealed class ManaBarView : MonoBehaviour
    {
        [Header("Рендер")]
        [Tooltip("Единственный Image бара (тип Simple, на всю ширину, белый vertex-цвет).")]
        [SerializeField] private Image _fillImage;

        [Tooltip("Шаблон материала (шейдер SegmentedHealthBar) — задаёт статичный вид (цвета/толщину насечек). " +
                 "В рантайме клонируется. ОБЯЗАТЕЛЕН.")]
        [SerializeField] private Material _barMaterial;

        [Header("Насечки")]
        [Tooltip("Сколько ресурса на одну (минорную) насечку. По умолчанию 5.")]
        [SerializeField] private float _tickValue = 5f;
        [Tooltip("Через сколько ресурса идёт ЖИРНАЯ насечка. По умолчанию 20. Кратно tickValue.")]
        [SerializeField] private float _majorTickValue = 20f;

        // Цвет ресурса задаёт материал бара — своей копии вью не держит. Прежнее поле-фолбэк было третьим
        // владельцем цвета (после материала и префаба) и работало только в паре с мёртвой веткой Shader.Find
        // (аудит фолбэков 2026-07-26, п.6 — близнец HealthBarView, отставший от его правки).

        [Header("Анимация chip-дельты")]
        [SerializeField] private float _trailDelay = 0.15f;
        [SerializeField] private float _trailSpeed = 1.2f;

        // Абсолютное состояние ресурса.
        private float _max = 1f;
        private float _current;
        private float _trail;          // догоняющий current, в абсолюте
        private float _delayRemaining;

        private Material _mat;

        private static readonly int IdHpFrac       = Shader.PropertyToID("_HpFrac");
        private static readonly int IdCombinedFrac = Shader.PropertyToID("_CombinedFrac");
        private static readonly int IdTrailFrac    = Shader.PropertyToID("_TrailFrac");
        private static readonly int IdSegments     = Shader.PropertyToID("_Segments");
        private static readonly int IdMajorEvery   = Shader.PropertyToID("_MajorEvery");

        private void Awake() => EnsureMaterial();

        private void EnsureMaterial()
        {
            if (_mat != null) return;

            // Материал ОБЯЗАТЕЛЕН — см. тот же разбор в HealthBarView.EnsureMaterial.
            if (_barMaterial == null)
            {
                Debug.LogError($"[ManaBarView] - {name}: не назначен _barMaterial → полоса ресурса не будет отрисована");
                return;
            }

            _mat = new Material(_barMaterial);
            if (_fillImage != null) _fillImage.material = _mat;

            _mat.SetFloat(IdMajorEvery, Mathf.Max(1f, _majorTickValue / Mathf.Max(0.0001f, _tickValue)));
        }

        /// <summary>Привязать к юниту: скрыть для безресурсных, иначе — на текущую долю мгновенно.</summary>
        public void Bind(in Combat.Tape.UnitSnapshot unit)
        {
            float max = unit.MaxResource;
            bool hasResource = max > 0f;
            gameObject.SetActive(hasResource);
            if (!hasResource) return;

            EnsureMaterial();
            _max     = Mathf.Max(1f, max);
            _current = Mathf.Clamp(unit.CurrentResource, 0f, _max);
            _trail   = _current;
            _delayRemaining = 0f;
            PushDynamicProps();
        }

        /// <summary>Обновить ресурс (поллинг из UnitView каждый кадр).</summary>
        public void UpdateBar(float current, float max)
        {
            if (max <= 0f) return;
            float newMax = Mathf.Max(1f, max);
            float newCur = Mathf.Clamp(current, 0f, newMax);
            if (!Mathf.Approximately(newCur, _current))
                _delayRemaining = _trailDelay;
            _max     = newMax;
            _current = newCur;
        }

        private void Update()
        {
            if (!Mathf.Approximately(_trail, _current))
            {
                if (_delayRemaining > 0f)
                    _delayRemaining -= Time.deltaTime;
                else
                    _trail = Mathf.MoveTowards(_trail, _current, _trailSpeed * _max * Time.deltaTime);
            }

            PushDynamicProps();
        }

        private void PushDynamicProps()
        {
            if (_mat == null) return;

            float scale = Mathf.Max(_max, 1f);
            float frac = _current / scale;
            _mat.SetFloat(IdHpFrac,       frac);
            _mat.SetFloat(IdCombinedFrac, frac);          // ресурс без щита: combined = fill
            _mat.SetFloat(IdTrailFrac,    _trail / scale);
            _mat.SetFloat(IdSegments,     Mathf.Max(1f, scale / Mathf.Max(0.0001f, _tickValue)));
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
