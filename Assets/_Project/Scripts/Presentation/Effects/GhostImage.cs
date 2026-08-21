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

            SilhouetteDraw.Apply(_parts, in silhouette, new Color(color.r, color.g, color.b, _startAlpha),
                                 sortingOrder, MakePart);

            for (int i = 0; i < _parts.Count; i++)
            {
                SpriteRenderer sr = _parts[i];
                if (sr == null || !sr.gameObject.activeSelf) continue;
                sr.sortingLayerID = sortingLayerId;
                if (material != null) sr.sharedMaterial = material;
            }

            PrimeParts(_parts, _mpb ??= new MaterialPropertyBlock(), _holo, _color);
            _running = true;
        }

        /// <summary>
        /// Состояние копии на момент <paramref name="elapsed"/>: дожила ли она, её прозрачность и масштаб.
        /// ОДИН владелец кривой жизни: её зовёт и бой (<see cref="Update"/>), и стенд Post FX Lab, который
        /// расставляет копии по фазе сам — там нет тика MonoBehaviour. Своя формула на стороне стенда
        /// разошлась бы с боем молча: копии остались бы нарисованными, просто гасли бы иначе.
        /// </summary>
        /// <returns><c>false</c> — копия отжила: рисовать её больше не нужно.</returns>
        public static bool Sample(float elapsed, float life, float delay, float startAlpha, float fadePower,
                                  float growTo, out float alpha, out float scale)
        {
            alpha = 0f;
            scale = 1f;
            if (elapsed < delay) return true;   // ещё не появилась — но появится

            float t = Mathf.Clamp01((elapsed - delay) / Mathf.Max(0.01f, life));
            alpha = Mathf.Clamp01(startAlpha) * Mathf.Pow(1f - t, Mathf.Max(0.01f, fadePower));
            scale = growTo > 1.0001f ? Mathf.Lerp(1f, growTo, t) : 1f;
            return t < 1f;
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

            // Кривая жизни — общая с Post FX Lab (см. Sample). Ожидание своей очереди отыгрывается
            // прозрачностью, а не выключением: выключенный объект не тикает, и очередь никогда бы не
            // подошла.
            bool alive = Sample(_elapsed, _life, _delay, _startAlpha, _fadePower, _growTo,
                                out float alpha, out float scale);

            // Масштаб растёт ОТ НОГ, а не от центра: копия остаётся стоящей на земле, иначе кольцо
            // ряби всплывает над ареной.
            if (_growTo > 1.0001f)
            {
                transform.localScale = new Vector3(scale, scale, 1f);
                transform.position = _feet;
            }

            SetTint(new Color(_color.r, _color.g, _color.b, alpha));

            if (alive) return;

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

        /// <summary>
        /// Праймить части копии: записать им property block с силой голограммы. Пишется ОДИН раз при
        /// спавне — по жизни копии голограмма не меняется, гаснет прозрачность, а не развоплощение.
        /// </summary>
        /// <remarks>
        /// <b>Блок пишется ВСЕГДА, даже при нулевой голограмме, и это не расточительство.</b> Наш
        /// спрайтовый шейдер (SRP Batcher) подхватывает <c>SpriteRenderer.color</c> только после первой
        /// записи per-instance блока — ровно та же причина, по которой у живого тела есть
        /// <c>IUnitBodyVisual.Prime</c>. Без прайма копия рисуется БЕЛОЙ и не гаснет: ни цвет юнита, ни
        /// падающая прозрачность до неё не доезжают (поймано раскадровкой 21.08.2026 — на стенде стояла
        /// стена белых силуэтов, одинаковых во всех двадцати четырёх кадрах).
        /// <para>Метод статический и публичный, потому что копии рисует не только бой: стенд Post FX Lab
        /// расставляет их сам, и свой прайм там разошёлся бы с этим молча.</para>
        /// </remarks>
        public static void PrimeParts(IReadOnlyList<SpriteRenderer> parts, MaterialPropertyBlock mpb,
                                      float holo, Color holoColor)
        {
            if (parts == null || mpb == null) return;

            for (int i = 0; i < parts.Count; i++)
            {
                SpriteRenderer sr = parts[i];
                if (sr == null || !sr.gameObject.activeSelf) continue;

                sr.GetPropertyBlock(mpb);
                mpb.SetFloat(BodyShaderIds.Holo, Mathf.Clamp01(holo));
                mpb.SetColor(BodyShaderIds.HoloColor, holoColor);
                mpb.SetFloat(BodyShaderIds.FlashAmount, 0f);
                mpb.SetFloat(BodyShaderIds.Outline, 0f);
                mpb.SetFloat(BodyShaderIds.GlowAmount, 0f);
                sr.SetPropertyBlock(mpb);
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
