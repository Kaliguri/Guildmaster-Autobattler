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

        private static readonly int UseCellMapId = Shader.PropertyToID("_UseCellMap");
        private static readonly int DesaturateId = Shader.PropertyToID("_Desaturate");
        private static readonly int ToGreyId     = Shader.PropertyToID("_ToGrey");
        private static readonly int CellMapId    = Shader.PropertyToID("_CellMap");
        private static readonly int MapRectId    = Shader.PropertyToID("_MapRect");
        private static readonly int CellsId      = Shader.PropertyToID("_Cells");
        private static readonly int CellSizeId   = Shader.PropertyToID("_CellSize");
        private static readonly int ProgressId   = Shader.PropertyToID("_Progress");

        private readonly List<Renderer> _targets = new List<Renderer>();
        private readonly List<Material> _original = new List<Material>();

        private Material _runtime;
        private bool     _grey;

        private ArenaDigitalOverlay _sweepSource;
        private bool _sweeping;
        private bool _sweepToGrey;

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
            if (_runtime == null) return;

            _sweeping = false;
            _runtime.SetFloat(UseCellMapId, 0f);
            _runtime.SetFloat(DesaturateId, 1f);

            if (_grey == grey) return;
            _grey = grey;

            if (_targets.Count == 0) CollectTargets();

            for (int i = 0; i < _targets.Count; i++)
                if (_targets[i] != null)
                    _targets[i].sharedMaterial = grey ? _runtime : _original[i];
        }

        /// <summary>
        /// Перекрасить арену ПОКЛЕТОЧНО, вслед за цифровым слоем: клетки меняют цвет вразнобой по той же
        /// карте, что и подмена текстур. Мгновенная перекраска щёлкала бы всем полем сразу, и длинному
        /// акту перехода нечем было бы себя занять.
        /// </summary>
        public void SweepGrey(bool grey, ArenaDigitalOverlay source)
        {
            if (_runtime == null || source == null || source.CellMap == null) { SetGrey(grey); return; }
            if (_targets.Count == 0) CollectTargets();

            // Серый материал должен висеть на рендерерах ВЕСЬ переход — в обе стороны: он и рисует, кто
            // уже сменил цвет, а кто ещё нет. Настоящее состояние фиксируется в конце.
            for (int i = 0; i < _targets.Count; i++)
                if (_targets[i] != null)
                    _targets[i].sharedMaterial = _runtime;

            _runtime.SetFloat(UseCellMapId, 1f);
            _runtime.SetFloat(ToGreyId, grey ? 1f : 0f);
            _runtime.SetTexture(CellMapId, source.CellMap);
            _runtime.SetVector(MapRectId, source.MapRect);
            _runtime.SetVector(CellsId, source.Cells);
            _runtime.SetFloat(CellSizeId, source.CellSizeWorld);
            _runtime.SetFloat(ProgressId, 0f);

            _sweepSource = source;
            _sweepToGrey = grey;
            _sweeping    = true;
            _grey        = grey;
        }

        private void LateUpdate()
        {
            if (!_sweeping || _runtime == null) return;

            _runtime.SetFloat(ProgressId, _sweepSource != null ? _sweepSource.CurrentProgress : 1f);

            if (_sweepSource != null && _sweepSource.Sweeping) return;

            // Переход доиграл: снимаем поклеточный режим и оставляем ровно то состояние, к которому шли.
            _sweeping = false;
            _runtime.SetFloat(UseCellMapId, 0f);
            _runtime.SetFloat(DesaturateId, 1f);

            if (_sweepToGrey) return;

            for (int i = 0; i < _targets.Count; i++)
                if (_targets[i] != null)
                    _targets[i].sharedMaterial = _original[i];
        }
    }
}
