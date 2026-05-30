using Guildmaster.Combat;
using System.Collections;
using UnityEngine;
using UnityEngine.Pool;

namespace Guildmaster.Presentation
{
    /// <summary>
    /// Всплывающие числа урона. Zero-alloc LitMotion-твины + ObjectPool.
    /// Подписывается на <see cref="CombatSimulation.OnDamageDealt"/> из <see cref="CombatPresenter"/>
    /// (вики «10» §7).
    /// </summary>
    public sealed class DamageNumberSpawner : MonoBehaviour
    {
        [SerializeField] private DamageNumber _prefab;
        [Tooltip("Высота всплытия числа.")]
        [SerializeField] private float _floatHeight = 1.2f;
        [Tooltip("Длительность анимации (сек).")]
        [SerializeField] private float _duration    = 0.8f;

        private ObjectPool<DamageNumber> _pool;

        private void Awake()
        {
            _pool = new ObjectPool<DamageNumber>(
                createFunc: () => Instantiate(_prefab, transform),
                actionOnGet: n => n.gameObject.SetActive(true),
                actionOnRelease: n => n.gameObject.SetActive(false),
                actionOnDestroy: n => Destroy(n.gameObject),
                collectionCheck: false,
                defaultCapacity: 8,
                maxSize: 32);
        }

        /// <summary>Показать всплывающее число урона над позицией.</summary>
        public void Spawn(Vector2 worldPosition, float damage)
        {
            if (_prefab == null) return;

            var number = _pool.Get();
            number.transform.position = (Vector3)worldPosition + Vector3.up * 0.3f;
            number.SetText(Mathf.RoundToInt(damage).ToString());
            number.Alpha = 1f;

            Vector3 startPos = number.transform.position;
            Vector3 endPos   = startPos + Vector3.up * _floatHeight;
            StartCoroutine(AnimateAndRelease(number, startPos, endPos, _duration));
        }

        private IEnumerator AnimateAndRelease(DamageNumber number, Vector3 startPos, Vector3 endPos, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                number.transform.position = Vector3.LerpUnclamped(startPos, endPos, eased);
                if (t > 0.5f)
                {
                    float fadeT = (t - 0.5f) / 0.5f;
                    number.Alpha = 1f - fadeT;
                }
                yield return null;
            }
            _pool.Release(number);
        }
    }
}
