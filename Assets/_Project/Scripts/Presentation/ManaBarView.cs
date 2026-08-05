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
    /// постоянна для юнита. Chip-дельты у ресурса нет: трату показывает ВСПЫШКА полосы, а не догоняющий
    /// хвост (см. <see cref="PushResourceColor"/>).
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

        [Header("Вспышка траты")]
        [Tooltip("На сколько ярче вспыхивает полоса в момент списания ресурса. 0 = не вспыхивать.")]
        [SerializeField] private float _spendFlashAmount = 0.9f;
        [Tooltip("Сколько держится вспышка траты, сек.")]
        [SerializeField] private float _spendFlashDuration = 0.18f;

        // Абсолютное состояние ресурса.
        private float _max = 1f;
        private float _current;

        private float _flashRemaining;
        private Color _resourceColor = Color.white;   // эталон из материала — вспышка множит его, а не задаёт свой
        private bool  _hasResourceColor;

        private Material _mat;

        private static readonly int IdHpFrac       = Shader.PropertyToID("_HpFrac");
        private static readonly int IdCombinedFrac = Shader.PropertyToID("_CombinedFrac");
        private static readonly int IdTrailFrac    = Shader.PropertyToID("_TrailFrac");
        private static readonly int IdSegments     = Shader.PropertyToID("_Segments");
        private static readonly int IdMajorEvery   = Shader.PropertyToID("_MajorEvery");
        private static readonly int IdHpColor      = Shader.PropertyToID("_HpColor");

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

            // Эталон цвета берётся у ЕДИНСТВЕННОГО владельца — материала. Вспышка траты его временно
            // множит, поэтому без снимка первая же вспышка стала бы новой нормой.
            _resourceColor    = _mat.GetColor(IdHpColor);
            _hasResourceColor = true;
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
            _flashRemaining = 0f;
            PushDynamicProps();
        }

        /// <summary>Обновить ресурс (поллинг из UnitView каждый кадр).</summary>
        public void UpdateBar(float current, float max)
        {
            if (max <= 0f) return;
            float newMax = Mathf.Max(1f, max);
            float newCur = Mathf.Clamp(current, 0f, newMax);

            // Списание — это КАСТ: пул ресурса равен цене каста, поэтому падение вниз здесь и есть момент
            // применения способности. Добор регеном идёт непрерывно и вспышки не заслуживает — иначе полоса
            // мигала бы весь бой и перестала что-либо сообщать.
            if (newCur < _current - SpendEpsilon) _flashRemaining = _spendFlashDuration;

            _max     = newMax;
            _current = newCur;
        }

        /// <summary>Порог, ниже которого убыль ресурса считается дрожанием счёта, а не тратой.</summary>
        private const float SpendEpsilon = 0.01f;

        private void Update()
        {
            if (_flashRemaining > 0f)
            {
                // Время unscaled: трата случается в тот же миг, что и hitstop с замедлением, и застывшая
                // вспышка растянулась бы на весь slowmo — то есть перестала бы читаться как момент.
                _flashRemaining -= Time.unscaledDeltaTime;
                if (_flashRemaining < 0f) _flashRemaining = 0f;
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
            // Догоняющей линии у ресурса НЕТ (решение Макса 05.08.2026: «Мана бар убрать 2 догоняющую
            // линию. Лишнее»). Chip-дельта рассказывает историю потери, а трата маны — не потеря, а
            // применение: у неё есть свой знак, вспышка. Trail держим равным заливке, иначе шейдер
            // нарисует хвост от прежнего значения.
            _mat.SetFloat(IdTrailFrac,    frac);
            _mat.SetFloat(IdSegments,     Mathf.Max(1f, scale / Mathf.Max(0.0001f, _tickValue)));

            PushResourceColor();
        }

        /// <summary>
        /// Вспышка в момент траты: полоса коротко светлеет и гаснет. Яркостью, а не масштабом — бар
        /// ресурса живёт вплотную к HP-бару, и дёрганье размера читалось бы как удар по юниту.
        /// </summary>
        private void PushResourceColor()
        {
            if (!_hasResourceColor) return;

            if (_flashRemaining <= 0f || _spendFlashAmount <= 0f)
            {
                _mat.SetColor(IdHpColor, _resourceColor);
                return;
            }

            // Пик в момент списания, дальше спад: вспышка сообщает «вот сейчас», а не «недавно было».
            float k = Mathf.Clamp01(_flashRemaining / Mathf.Max(0.01f, _spendFlashDuration));
            Color flashed = _resourceColor * (1f + _spendFlashAmount * k);
            flashed.a = _resourceColor.a;
            _mat.SetColor(IdHpColor, flashed);
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
