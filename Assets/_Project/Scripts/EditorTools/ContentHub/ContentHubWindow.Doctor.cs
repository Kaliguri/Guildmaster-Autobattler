using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>Doctor: полная валидация индекса одним списком + бейдж-счётчик ошибок на pill.</summary>
    public sealed partial class ContentHubWindow
    {
        private Label _doctorBadge;

        private void BuildDoctor(VisualElement container)
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            container.Add(scroll);

            var inner = new VisualElement();
            inner.AddToClassList("gh-detail");
            scroll.Add(inner);

            var rescan = new Button(() => { ContentIndex.Invalidate(); RebuildContent(); }) { text = "Rescan" };
            rescan.AddToClassList("gh-btn");
            inner.Add(rescan);

            var issues = ContentValidationService.ValidateAll();
            var errors = issues.Where(i => i.Severity == IssueSeverity.Error).ToList();
            var warns = issues.Where(i => i.Severity == IssueSeverity.Warning).ToList();

            inner.Add(new Label($"Ошибки: {errors.Count} · Предупреждения: {warns.Count}").WithClass("gh-sec-h"));

            if (issues.Count == 0)
            {
                inner.Add(new Label("Проблем не найдено.").WithClass("gh-stub"));
                return;
            }

            AddIssueGroup(inner, "Ошибки", errors);
            AddIssueGroup(inner, "Предупреждения", warns);
        }

        private void AddIssueGroup(VisualElement parent, string title, List<ValidationIssue> list)
        {
            if (list.Count == 0) return;
            parent.Add(new Label(title).WithClass("gh-sec-h"));
            foreach (var iss in list)
            {
                var row = new VisualElement();
                row.AddToClassList("gh-issue");
                row.Add(EntityChip(iss.Entry, iss.Entry.Domain));
                row.Add(new Label(iss.Message).WithClass("gh-issue-msg"));
                parent.Add(row);
            }
        }

        private void RefreshDoctorBadge()
        {
            if (_doctorBadge == null) return;
            int errors = ContentValidationService.ValidateAll().Count(i => i.Severity == IssueSeverity.Error);
            _doctorBadge.text = errors.ToString();
            _doctorBadge.style.display = errors > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnIndexChanged()
        {
            if (_tabbar != null) PopulateTabs();   // домены могли появиться/исчезнуть
            RefreshDoctorBadge();
            if (_page == Page.Doctor && _content != null) RebuildContent();
        }
    }
}
