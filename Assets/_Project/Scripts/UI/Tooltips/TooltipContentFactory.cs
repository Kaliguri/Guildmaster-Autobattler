using System.Collections.Generic;
using Guildmaster.Core.Localization;
using Guildmaster.Data.Definitions;
using Guildmaster.Data.Descriptions;
using Guildmaster.Data.Stats;
using Guildmaster.UI.Components;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Tooltips
{
    /// <summary>
    /// Штатная сборка содержимого: реликвия, тег, сосуд, готовый текст.
    /// </summary>
    /// <remarks>
    /// Числа сюда НЕ считаются (HARD, §II.10.1) — имя и описание берутся у
    /// <see cref="IDescriptionService"/>, стат-сводка у <see cref="IUnitStatPreview"/>, то есть из тех
    /// же мест, откуда их берёт бой. Здесь остаётся только раскладка: что во что положить.
    /// </remarks>
    public sealed class TooltipContentFactory : ITooltipContentFactory
    {

        private readonly IContentDatabase _content;
        private readonly IDescriptionService _descriptions;
        private readonly ILocalizationService _loc;
        private readonly IUnitStatPreview _statPreview;
        private readonly IKeywordStyle _style;

        public TooltipContentFactory(IContentDatabase content, IDescriptionService descriptions,
            ILocalizationService loc, IUnitStatPreview statPreview, IKeywordStyle style = null)
        {
            _content      = content;
            _descriptions = descriptions;
            _loc          = loc;
            _statPreview  = statPreview;
            _style        = style;
        }

        public VisualElement Build(TooltipRequest request, bool detailed)
        {
            switch (request.Kind)
            {
                case TooltipKind.Text:   return TextCard(request.Text, request.Title);
                case TooltipKind.Relic:  return RelicCard(request.Id, detailed);
                case TooltipKind.Tag:    return TagCard(request.Id);
                case TooltipKind.Vessel: return VesselCard(request.Id);
                case TooltipKind.Keyword: return KeywordCard(request.Id, detailed);
                case TooltipKind.Stat:   return StatCard(request.Id);
                default:                 return null;
            }
        }

        // Живыми пока не бывает ничего: тултипы этапа 1 показывают данные ОПРЕДЕЛЕНИЙ (они в забеге
        // не меняются). Разбор статов живого юнита появится вместе с панелью юнита — тогда сюда
        // придёт true для Stat, и рефреш заработает без правок системы.
        public bool IsLive(TooltipRequest request) => false;

        private static VisualElement TextCard(string text, string title)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var card = new TooltipCard();
            card.SetTitle(title);
            card.SetDesc(text);
            return card;
        }

        private VisualElement RelicCard(string id, bool detailed)
        {
            if (_content == null || !_content.TryGet(id, out RelicData relic) || relic == null) return null;

            var card = new TooltipCard();
            card.SetTitle(Name(relic, id));
            card.SetMeta(KitPowerLabel(relic.KitPower));
            card.SetTags(TagNames(relic.InfoTags));
            card.SetDesc(_descriptions?.Describe(relic, null));

            // Стат-сводка — только в подробном режиме: краткий вид тултипа отвечает на «что это»,
            // восьмёрка чисел на этот вопрос не отвечает и превращает окно в простыню (§II.10.4).
            if (detailed && _statPreview != null)
            {
                IReadOnlyList<UnitStatLine> lines = _statPreview.Basic(relic);
                for (int i = 0; lines != null && i < lines.Count; i++)
                    card.AddLine(UiString(lines[i].LabelKey, lines[i].LabelFallback), lines[i].Value);
            }

            AppendGlossary(card, relic, detailed);
            return card;
        }

        private VisualElement TagCard(string id)
        {
            if (_content == null || !_content.TryGet(id, out TagData tag) || tag == null) return null;

            var card = new TooltipCard();
            card.SetTitle(Name(tag, id));
            card.SetMeta(TagCategoryLabel(tag.Category));
            card.SetDesc(_descriptions?.Describe(tag, null));
            return card;
        }

        /// <summary>
        /// Характеристика: подпись и объяснение по ключу <c>ui.stat.*</c>. Число НЕ показываем —
        /// оно уже видно в самой строке, а дубль в подсказке даёт два места, которые обязаны сойтись.
        /// </summary>
        private VisualElement StatCard(string statKey)
        {
            if (string.IsNullOrEmpty(statKey)) return null;

            string title = UiString(statKey, null);
            string desc  = UiString(statKey + "." + ContentKeys.DescSuffix, null);
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(desc)) return null;

            var card = new TooltipCard();
            card.SetTitle(title);
            // Объяснение стата ссылается на термины («снижает [kw:physical]») — разворачиваем той же
            // разметкой, что и описания контента, иначе в подсказке торчал бы сырой [kw:…].
            card.SetDesc(KeywordMarkup.Render(
                desc,
                _descriptions != null ? _descriptions.KeywordForm : (System.Func<string, string, string>)null,
                _style));
            return card;
        }

        // Термин глоссария. Кратко — по наведению, подробно (Shift) — полная статья: у keyword сразу
        // два описания (§II.10.7), и подробный режим — законный повод показать длинное.
        private VisualElement KeywordCard(string id, bool detailed)
        {
            if (_content == null || !_content.TryGet(KeywordMarkup.FullId(id), out KeywordDefinition kw) || kw == null)
                return null;

            var card = new TooltipCard();
            card.SetTitle(Name(kw, kw.Id));
            card.SetMeta(KeywordCategoryLabel(kw.Category));
            card.SetDesc(detailed ? _descriptions?.DescribeFull(kw, null) : _descriptions?.Describe(kw, null));
            return card;
        }

        private VisualElement VesselCard(string id)
        {
            if (_content == null || !_content.TryGet(id, out VesselData vessel) || vessel == null) return null;

            var card = new TooltipCard();
            card.SetTitle(Name(vessel, id));
            card.SetDesc(_descriptions?.Describe(vessel, null));
            return card;
        }

        /// <summary>
        /// Слой 2 вложенности (план §II.10.5): в подробном режиме карточка ДОПИСЫВАЕТ определения
        /// терминов, упомянутых в её тексте — вместо второго окна поверх первого.
        /// </summary>
        /// <remarks>
        /// Определения берутся с <c>Strip</c>, то есть БЕЗ ссылок. Это не экономия, а конструкция:
        /// глоссарий ссылается сам на себя (Броня → Урон → Броня), и рекурсию проще сделать
        /// невозможной, чем ловить лимитом глубины, который игрок читает как «дальше не открывается».
        /// </remarks>
        private void AppendGlossary(TooltipCard card, ContentDefinition def, bool detailed)
        {
            if (!detailed || _descriptions == null || _content == null) return;

            IReadOnlyList<string> ids = _descriptions.MentionedKeywords(def);
            for (int i = 0; ids != null && i < ids.Count; i++)
            {
                if (!_content.TryGet(ids[i], out KeywordDefinition kw) || kw == null) continue;
                string term = _descriptions.KeywordForm(kw.Id, null);
                card.AddGlossaryEntry(
                    Bracketed(string.IsNullOrEmpty(term) ? Name(kw, kw.Id) : term),
                    _descriptions.DescribePlain(kw, null));
            }
        }

        // Термин всюду выглядит одинаково — в тексте описания и в списке определений (реш. Макса).
        private static string Bracketed(string term) => string.IsNullOrEmpty(term) ? null : "[" + term + "]";

        // Имя контента; если ключ не заведён — показываем id, а не пустоту: молчащий тултип
        // выглядит как поломка системы, а видимый id сразу говорит, какой строки не хватает.
        private string Name(ContentDefinition def, string id)
        {
            string name = _descriptions?.Name(def);
            return string.IsNullOrEmpty(name) ? id : name;
        }

        private string TagNames(IReadOnlyList<TagData> tags)
        {
            if (tags == null || tags.Count == 0) return null;
            var names = new List<string>(tags.Count);
            for (int i = 0; i < tags.Count; i++)
            {
                if (tags[i] == null) continue;
                names.Add(Name(tags[i], tags[i].Id));
            }
            return names.Count == 0 ? null : string.Join(" · ", names);
        }

        private string KitPowerLabel(KitPower power) => power switch
        {
            KitPower.Cursed => UiString("ui.kit.cursed", "Проклятый кит"),
            KitPower.Divine => UiString("ui.kit.divine", "Божественный кит"),
            _               => UiString("ui.kit.common", "Обычный кит"),
        };

        private string KeywordCategoryLabel(KeywordCategory category) => category switch
        {
            KeywordCategory.Status    => UiString("ui.kw.category.status", "Статус"),
            KeywordCategory.Damage    => UiString("ui.kw.category.damage", "Урон"),
            KeywordCategory.Defense   => UiString("ui.kw.category.defense", "Защита"),
            KeywordCategory.Behaviour => UiString("ui.kw.category.behaviour", "Поведение"),
            KeywordCategory.Run       => UiString("ui.kw.category.run", "Забег"),
            _                         => null,
        };

        private string TagCategoryLabel(TagCategory category) => category switch
        {
            TagCategory.Role       => UiString("ui.tag.category.role", "Роль"),
            TagCategory.DamageType => UiString("ui.tag.category.damage", "Тип урона"),
            TagCategory.Playstyle  => UiString("ui.tag.category.playstyle", "Стиль"),
            TagCategory.Mechanic   => UiString("ui.tag.category.mechanic", "Механика"),
            _                      => null,
        };

        private string UiString(string key, string fallback)
        {
            string value = string.IsNullOrEmpty(key) ? null : _loc?.GetString(ContentKeys.UiTableName, key);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
    }
}
