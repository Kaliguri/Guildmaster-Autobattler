using System.Collections.Generic;
using System.Text;
using Guildmaster.Core.Audio;
using Guildmaster.Core.Localization;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Stats;
using MessagePipe;

namespace Guildmaster.UI
{
    /// <summary>
    /// ViewModel loadout-экрана (план шаг 5): держит цель (юнит + текущий/выбранный релик), отдаёт грид
    /// доступных реликов и применяет выбор через <see cref="EquipRelicRequest"/> (слушает фаза расстановки).
    /// POCO по образцу <c>SettingsViewModel</c>: сервисы Core/Data, никакой ссылки на боевой слой.
    /// Релик = весь боевой кит (вики «13» §3.1); при выборе играет звук-стингер релика (хук FMOD).
    /// </summary>
    public sealed class LoadoutViewModel
    {
        private readonly IContentDatabase    _content;
        private readonly ILocalizationService _loc;
        private readonly IAudioService       _audio;
        private readonly IPublisher<EquipRelicRequest> _equipPub;

        private int _unitId = -1;
        private VesselData _vessel;

        public RelicData Current  { get; private set; }
        public RelicData Selected { get; private set; }

        public LoadoutViewModel(
            IContentDatabase content,
            ILocalizationService loc,
            IAudioService audio,
            IPublisher<EquipRelicRequest> equipPub)
        {
            _content  = content;
            _loc      = loc;
            _audio    = audio;
            _equipPub = equipPub;
        }

        /// <summary>Все доступные релики для грида (пока — весь контент; фильтр по владению — Фаза 5).</summary>
        public IReadOnlyList<RelicData> Relics => _content.All<RelicData>();

        /// <summary>Открыть loadout для цели: запомнить юнита/сосуд/текущий релик, предвыбрать текущий.</summary>
        public void Open(OpenLoadoutRequest req)
        {
            _unitId  = req.UnitId;
            _vessel  = req.Vessel;
            Current  = req.CurrentRelic;
            Selected = req.CurrentRelic;
        }

        /// <summary>Выбрать релик (клик по карточке): предпросмотр + звук-стингер релика.</summary>
        public void Select(RelicData relic)
        {
            if (relic == null) return;
            Selected = relic;
            // Хук звука выбора: точный ключ «<relicId>.select»; FMOD-событие Макс заводит позже (тишина безопасна).
            _audio?.Play(relic.Id + ".select");
        }

        /// <summary>Применить выбор: публикуем экип (фаза расстановки пересоберёт превью).</summary>
        public void Apply()
        {
            if (_unitId < 0 || Selected == null) return;
            _equipPub.Publish(new EquipRelicRequest(_unitId, Selected));
            Current = Selected;
        }

        public bool IsSelected(RelicData r) => r != null && r == Selected;
        public bool IsCurrent(RelicData r)  => r != null && r == Current;

        public string Name(RelicData r) => r != null ? _loc.GetString(r.Id + ".name") : string.Empty;
        public string Desc(RelicData r) => r != null ? _loc.GetString(r.Id + ".desc") : string.Empty;

        /// <summary>
        /// Теги «быстрого чтения» релика для карточки в порядке осей (Role→DamageType→Playstyle→Mechanic):
        /// авто Role/DamageType из данных + ручные InfoTags. Резолв — <see cref="UnitTagResolver"/>.
        /// </summary>
        public IReadOnlyList<TagData> ResolveTags(RelicData r) => UnitTagResolver.Resolve(r, _content);

        /// <summary>Строка тегов релика (локализованные имена <c>InfoTags</c>) — легаси-путь старого LoadoutScreen.</summary>
        public string Tags(RelicData r)
        {
            if (r?.InfoTags == null || r.InfoTags.Length == 0) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < r.InfoTags.Length; i++)
            {
                if (r.InfoTags[i] == null) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(_loc.GetString(r.InfoTags[i].Id + ".name"));
            }
            return sb.ToString();
        }

        /// <summary>Короткая сводка ключевых статов из стат-блока релика (Override-значения кита).</summary>
        public string StatsSummary(RelicData r)
        {
            if (r?.Stats == null || r.Stats.Length == 0) return "—";
            var sb = new StringBuilder();
            for (int i = 0; i < r.Stats.Length; i++)
            {
                string label = StatLabel(r.Stats[i].Stat);
                if (label == null) continue;
                if (sb.Length > 0) sb.Append("  ");
                sb.Append(label).Append(' ').Append(Mathf(r.Stats[i].Value));
            }
            return sb.Length > 0 ? sb.ToString() : "—";
        }

        private static string Mathf(float v) => v % 1f == 0f ? ((int)v).ToString() : v.ToString("0.0");

        private static string StatLabel(StatType s) => s switch
        {
            StatType.MaxHP            => "HP",
            StatType.AutoAttackDamage => "DMG",
            StatType.AttackSpeed      => "AS",
            StatType.AttackRange      => "RNG",
            StatType.MoveSpeed        => "MS",
            StatType.PhysArmor        => "ARM",
            _ => null,
        };
    }
}
