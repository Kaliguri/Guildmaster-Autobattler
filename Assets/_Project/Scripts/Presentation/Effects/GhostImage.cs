using System.Collections.Generic;
using Guildmaster.Presentation.Body;
using UnityEngine;

namespace Guildmaster.Presentation.Effects
{
    /// <summary>
    /// Призрачная копия тела: поза юнита, замороженная в один момент, которая гаснет на месте. Кирпич сразу
    /// двух эффектов — <b>шлейфа</b> за рывком (цепочка копий по следу) и <b>иллюзии</b> уклонения (одна
    /// копия в точке, из которой тело успело выпасть).
    /// </summary>
    /// <remarks>
    /// <b>Почему не префаб, вопреки правилу «все VFX — префабы».</b> Копия повторяет КОНКРЕТНОЕ тело в
    /// КОНКРЕТНОЙ позе — префабом такое не подменить, ровно как разлёт на осколки
    /// (<see cref="DeathShatter"/>), который режет меш из спрайта конкретного юнита. Художественная часть
    /// при этом всё равно живёт снаружи: материал берётся с самого тела, цвет — с юнита, а числа жизни — из
    /// feel-конфига.
    /// <para><b>Поза снимается силуэтом</b> (<see cref="UnitSilhouette"/>) — тем же, которым рисуется
    /// drag-призрак расстановки. Копировать иерархию костей не нужно: части раскладываются плоско, каждая со
    /// своей мировой позой, поэтому копия не зависит от устройства рига.</para>
    /// <para><b>Время — своё, кадровое</b> (<c>Time.deltaTime</c>): копия уже отвязана от юнита и от тика,
    /// а замедление боя обязано растягивать её вместе со всем остальным показом.</para>
    /// </remarks>
    public sealed class GhostImage : MonoBehaviour
    {
        private readonly List<SpriteRenderer> _parts = new List<SpriteRenderer>(16);

        private MaterialPropertyBlock _mpb;
        private System.Action<GhostImage> _onDone;

        private Color _color;
        private float _life, _elapsed, _startAlpha, _fadePower, _holo;
        private float _growTo = 1f, _delay;
        private Vector3 _feet;
        private bool  _running;

        /// <summary>
        /// Оставить копию тела в точке <paramref name="feet"/> (мировые координаты ног).
        /// </summary>
        /// <param name="silhouette">Поза тела на момент снимка.</param>
        /// <param name="color">Цвет копии — свет самого юнита; альфа берётся из <paramref name="startAlpha"/>.</param>
        /// <param name="material">Материал тела: копия светится и сканируется тем же шейдером, что и оригинал.</param>
        /// <param name="life">Сколько секунд копия живёт.</param>
        /// <param name="startAlpha">Непрозрачность в момент снимка.</param>
        /// <param name="fadePower">Степень затухания: 1 — линейно, больше — копия дольше держится и резко гаснет.</param>
        /// <param name="holo">Сила голограммы 0..1 (<c>_Holo</c>): 0 — просто прозрачный силуэт.</param>
        /// <param name="growTo">
        /// До какого масштаба копия вырастает к концу жизни. 1 — стоит на месте (шлейф); больше —
        /// расходится наружу, и из таких копий собирается РЯБЬ иллюзии. Второго канала под кольца нет
        /// намеренно: рябь и шлейф — одна магия, и строятся одним приёмом.
        /// </param>
        /// <param name="delay">Пауза перед появлением, сек: ею кольца ряби разводятся во времени.</param>
        public void Play(in UnitSilhouette silhouette, Vector3 feet, Color color, Material material,
                         int sortingLayerId, int sortingOrder, float life, float startAlpha, float fadePower,
                         float holo, System.Action<GhostImage> onDone, float growTo = 1f, float delay = 0f)
        {
            _onDone     = onDone;
            _color      = color;
            _life       = Mathf.Max(0.01f, life);
            _startAlpha = Mathf.Clamp01(startAlpha);
            _fadePower  = Mathf.Max(0.01f, fadePower);
            _holo       = Mathf.Clamp01(holo);
            _growTo     = Mathf.Max(0.01f, growTo);
            _delay      = Mathf.Max(0f, delay);
            _elapsed    = 0f;
            _feet       = feet;

            transform.position = feet;
            transform.localScale = Vector3.one;
            gameObject.SetActive(true);

            SilhouetteDraw.Apply(_parts, in silhouette, Tint(0f), sortingOrder, MakePart);

            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer sr = _parts[i];
                if (sr == null || !sr.gameObject.activeSelf) continue;
                sr.sortingLayerID = sortingLayerId;
                if (material != null) sr.sharedMaterial = material;
            }

            ApplyHolo();
            _running = true;
        }

        /// <summary>Погасить копию досрочно (сброс боя) — без колбэка: возврат в пул делает вызывающий.</summary>
        public void Stop()
        {
            _running = false;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!_running) return;

            _elapsed += Time.deltaTime;

            // Ожидание своей очереди: кольцо ряби ещё не появилось. Прозрачным, а не выключенным —
            // выключенный объект не тикает, и очередь никогда бы не подошла.
            if (_elapsed < _delay)
            {
                SetTint(new Color(_color.r, _color.g, _color.b, 0f));
                return;
            }

            float t = Mathf.Clamp01((_elapsed - _delay) / _life);

            if (_growTo > 1.0001f)
            {
                // Растём ОТ НОГ, а не от центра: копия остаётся стоящей на земле, иначе кольцо
                // всплывает над ареной.
                float k = Mathf.Lerp(1f, _growTo, t);
                transform.localScale = new Vector3(k, k, 1f);
                transform.position = _feet;
            }

            SetTint(Tint(t));

            if (t < 1f) return;

            _running = false;
            gameObject.SetActive(false);
            _onDone?.Invoke(this);
        }

        private void SetTint(Color tint)
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer sr = _parts[i];
                if (sr != null && sr.gameObject.activeSelf) sr.color = tint;
            }
        }

        /// <summary>Цвет копии на доле жизни: тот же свет юнита, теряющий прозрачность (а не уходящий в другой цвет).</summary>
        private Color Tint(float t)
        {
            Color c = _color;
            c.a = _startAlpha * Mathf.Pow(1f - t, _fadePower);
            return c;
        }

        /// <summary>
        /// Голограмма пишется ОДИН раз при спавне, а не каждый кадр: её сила по жизни копии не меняется —
        /// гаснет прозрачность, а не развоплощение.
        /// </summary>
        private void ApplyHolo()
        {
            if (_holo <= 0.0001f) return;
            _mpb ??= new MaterialPropertyBlock();

            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer sr = _parts[i];
                if (sr == null || !sr.gameObject.activeSelf) continue;

                sr.GetPropertyBlock(_mpb);
                _mpb.SetFloat(BodyShaderIds.Holo, _holo);
                _mpb.SetColor(BodyShaderIds.HoloColor, _color);
                sr.SetPropertyBlock(_mpb);
            }
        }

        private SpriteRenderer MakePart()
        {
            var go = new GameObject("GhostPart");
            go.transform.SetParent(transform, worldPositionStays: false);
            return go.AddComponent<SpriteRenderer>();
        }
    }
}
