using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Presentation.Arena
{
    /// <summary>
    /// Обесцвечивает арену на месте: полигон — это серая версия ТОЙ ЖЕ локации, а не отдельный набор серых
    /// тайлов. Подменяется материал рендереров, тайлы не трогаются, поэтому приём переживёт любую новую
    /// арену и не потребует рисовать под неё серый дубль.
    /// <para>Состояние показывает ЦВЕТ (серый = полигон), а движение — голубой цифровой слой
    /// (<see cref="ArenaDigitalOverlay"/>). Держать цифру постоянно нельзя: на экране, где игрок стоит
    /// минутами, вечная анимация невыносима (находка Макса на play-QA).</para>
    /// </summary>
    public sealed class ArenaDesaturation : MonoBehaviour
    {
        [Tooltip("Материал на шейдере Guildmaster/Sprite/Desaturate. Рисуем КОПИЕЙ — ассет не грязним.")]
        [SerializeField] private Material _greyMaterial;

        private readonly List<Renderer> _targets = new List<Renderer>();
        private readonly List<Material> _original = new List<Material>();

        private Material _runtime;
        private bool     _grey;

        /// <summary>Серая ли арена сейчас.</summary>
        public bool IsGrey => _grey;

        private void Awake()
        {
            if (_greyMaterial == null)
            {
                Debug.LogWarning("[ArenaDesaturation] - серый материал не назначен → обесцвечивание выключено.");
                enabled = false;
                return;
            }

            _runtime = new Material(_greyMaterial) { name = _greyMaterial.name + " (runtime)" };
        }

        /// <summary>
        /// Собирает ВСЁ, что рисует арену: тайлмапы пола и стен, плюс декор отдельными спрайтами (трава,
        /// камни). Первый заход брал только слои облика — и трава осталась цветной посреди серого поля.
        /// <para>Сбор ленивый: цифровой оверлей создаёт свой квад в Awake, а порядок Awake между
        /// компонентами не определён. К первому обесцвечиванию он уже есть и его можно узнать по шейдеру —
        /// перекрашивать сам эффект, разумеется, нельзя.</para>
        /// </summary>
        private void CollectTargets()
        {
            _targets.Clear();
            _original.Clear();

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                Material material = renderer.sharedMaterial;
                if (material == null) continue;
                if (material.shader != null && material.shader.name.StartsWith("Guildmaster/Arena/")) continue;

                _targets.Add(renderer);
                _original.Add(material);
            }
        }

        private void OnDestroy()
        {
            if (_runtime != null) Destroy(_runtime);
        }

        /// <summary>Перекрасить арену в серое или вернуть её цвет. Мгновенно — плавность даёт цифровой слой.</summary>
        public void SetGrey(bool grey)
        {
            if (_grey == grey || _runtime == null) return;
            _grey = grey;

            if (_targets.Count == 0) CollectTargets();

            for (int i = 0; i < _targets.Count; i++)
                if (_targets[i] != null)
                    _targets[i].sharedMaterial = grey ? _runtime : _original[i];
        }
    }
}
