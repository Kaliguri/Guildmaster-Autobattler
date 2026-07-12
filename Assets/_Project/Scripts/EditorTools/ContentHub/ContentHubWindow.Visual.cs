using System.Collections.Generic;
using System.Linq;
using Guildmaster.Data.Definitions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.ContentHub.Editor
{
    /// <summary>Visual: превью юнита — портрет, проигрывание клипов (без рига, по спрайт-кадрам), масштаб, lineup.</summary>
    public sealed partial class ContentHubWindow
    {
        private List<ClipSpriteFrames.Frame> _clipFrames;
        private float _clipLen, _clipT, _clipSpeed = 1f;
        private bool _clipPlaying;
        private double _clipLastTime;
        private Image _clipImage;
        private Label _clipInfo, _clipScale;

        private void BuildVisual(VisualElement container)
        {
            var split = new TwoPaneSplitView(0, 220f, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            container.Add(split);

            // ---- left: visuals list
            var rail = new VisualElement();
            rail.AddToClassList("gh-rail");
            var railScroll = new ScrollView();
            railScroll.style.flexGrow = 1;
            rail.Add(railScroll);
            foreach (var e in VisualEntries())
                railScroll.Add(BuildRow(e));
            split.Add(rail);

            // ---- right: preview
            var right = new ScrollView();
            right.style.flexGrow = 1;
            var inner = new VisualElement();
            inner.AddToClassList("gh-detail");
            right.Add(inner);
            split.Add(right);

            var vis = ResolveVisual();
            if (vis == null)
            {
                inner.Add(new Label("Выбери визуал слева (или юнита с визуалом).").WithClass("gh-stub"));
            }
            else
            {
                BuildVisualPreview(inner, vis);
            }

            inner.Add(new Label("Lineup — все визуалы").WithClass("gh-sec-h"));
            inner.Add(BuildLineup());
        }

        private IEnumerable<ContentEntry> VisualEntries() =>
            ContentIndex.Entries.Where(e => e.Asset is UnitVisual)
                .OrderBy(e => e.DisplayName, System.StringComparer.OrdinalIgnoreCase);

        private UnitVisual ResolveVisual()
        {
            var entry = ResolveSelected();
            if (entry?.Asset is UnitVisual direct) return direct;
            if (entry?.Unit != null && entry.Unit.Visual != null) return entry.Unit.Visual;
            return VisualEntries().FirstOrDefault()?.Asset as UnitVisual;
        }

        private void BuildVisualPreview(VisualElement inner, UnitVisual vis)
        {
            var top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;

            // portrait
            if (vis.Portrait != null)
            {
                var port = new Image { sprite = vis.Portrait };
                port.style.width = 72; port.style.height = 72;
                port.style.marginRight = 12;
                port.scaleMode = ScaleMode.ScaleToFit;
                top.Add(port);
            }

            // player
            var playerCol = new VisualElement();
            _clipImage = new Image { scaleMode = ScaleMode.ScaleToFit };
            _clipImage.style.width = 220; _clipImage.style.height = 220;
            _clipImage.style.backgroundColor = new Color(0.04f, 0.04f, 0.03f, 1f);
            playerCol.Add(_clipImage);

            var ctrl = new VisualElement();
            ctrl.style.flexDirection = FlexDirection.Row;
            ctrl.style.alignItems = Align.Center;
            var playBtn = new Button { text = "⏸" };
            playBtn.AddToClassList("gh-btn");
            playBtn.clicked += () => { _clipPlaying = !_clipPlaying; playBtn.text = _clipPlaying ? "⏸" : "▶"; _clipLastTime = EditorApplication.timeSinceStartup; };
            ctrl.Add(playBtn);

            var speed = new Slider(0.1f, 2f) { value = _clipSpeed };
            speed.style.width = 120;
            var speedLbl = new Label($"{_clipSpeed:0.0}×");
            speed.RegisterValueChangedCallback(e => { _clipSpeed = e.newValue; speedLbl.text = $"{_clipSpeed:0.0}×"; });
            ctrl.Add(speed);
            ctrl.Add(speedLbl);
            playerCol.Add(ctrl);

            _clipInfo = new Label("").WithClass("gh-st-dim");
            _clipScale = new Label("").WithClass("gh-st-dim");
            playerCol.Add(_clipInfo);
            playerCol.Add(_clipScale);

            top.Add(playerCol);
            inner.Add(top);

            // clip selector
            inner.Add(new Label("Клипы").WithClass("gh-sec-h"));
            var btns = new VisualElement();
            btns.style.flexDirection = FlexDirection.Row;
            btns.style.flexWrap = Wrap.Wrap;
            foreach (var (label, clip) in ClipSlots(vis))
            {
                var b = new Button { text = label };
                b.AddToClassList("gh-btn");
                if (clip == null) b.SetEnabled(false);
                else { var c = clip; var l = label; b.clicked += () => LoadClip(c, l); }
                btns.Add(b);
            }
            inner.Add(btns);

            // autoplay attack (or idle)
            var first = ClipSlots(vis).FirstOrDefault(s => s.clip != null);
            if (first.clip != null) LoadClip(first.clip, first.label);
        }

        private static IEnumerable<(string label, AnimationClip clip)> ClipSlots(UnitVisual vis)
        {
            yield return ("Idle", vis.Clip(UnitAnimationState.Idle));
            yield return ("Run", vis.Clip(UnitAnimationState.Run));
            yield return ("Attack", vis.AttackClip);
            yield return ("Death", vis.Clip(UnitAnimationState.Death));
            yield return ("Hit", vis.HitClip);
            for (int i = 0; i < 4; i++)
                yield return ($"Skill{i + 1}", vis.SkillClip(i));
        }

        private VisualElement BuildLineup()
        {
            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;

            foreach (var e in VisualEntries())
            {
                var vis = (UnitVisual)e.Asset;
                var frames = ClipSpriteFrames.Extract(vis.Clip(UnitAnimationState.Idle));
                Sprite thumb = frames.Count > 0 ? frames[0].Sprite : vis.Portrait;

                var cell = new VisualElement();
                cell.style.width = 64; cell.style.height = 78;
                cell.style.marginRight = 6; cell.style.marginBottom = 6;
                cell.style.alignItems = Align.Center;

                var img = new Image { sprite = thumb, scaleMode = ScaleMode.ScaleToFit };
                img.style.width = 56; img.style.height = 56;
                img.style.backgroundColor = new Color(0.04f, 0.04f, 0.03f, 1f);
                cell.Add(img);
                var name = new Label(e.DisplayName);
                name.style.fontSize = 9;
                name.style.overflow = Overflow.Hidden;
                cell.Add(name);

                var captured = e;
                cell.RegisterCallback<PointerDownEvent>(ev => { if (ev.button == 0) SelectAssetAndRebuild(captured.Asset); });
                grid.Add(cell);
            }
            return grid;
        }

        private void LoadClip(AnimationClip clip, string label)
        {
            _clipFrames = ClipSpriteFrames.Extract(clip);
            _clipLen = clip != null ? clip.length : 0f;
            _clipT = 0f;
            _clipPlaying = _clipFrames.Count > 0;
            _clipLastTime = EditorApplication.timeSinceStartup;
            if (_clipInfo != null)
                _clipInfo.text = clip == null
                    ? "нет клипа"
                    : $"{label}: {_clipFrames.Count} кадров · {clip.frameRate:0} fps · {clip.length:0.##}s";
            ApplyClipFrame();
        }

        private void ApplyClipFrame()
        {
            if (_clipImage == null) return;
            var s = ClipSpriteFrames.At(_clipFrames, _clipT);
            _clipImage.sprite = s;
            if (_clipScale != null)
                _clipScale.text = s != null
                    ? $"выс. ≈ {(s.rect.height / s.pixelsPerUnit):0.##} u (реком. 1.7)"
                    : "";
        }

        private void ClipTick()
        {
            if (_clipImage == null || _clipImage.panel == null) return;
            if (!_clipPlaying || _clipFrames == null || _clipFrames.Count == 0 || _clipLen <= 0f)
            {
                _clipLastTime = EditorApplication.timeSinceStartup;
                return;
            }
            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - _clipLastTime) * _clipSpeed;
            _clipLastTime = now;
            _clipT = Mathf.Repeat(_clipT + dt, _clipLen);
            ApplyClipFrame();
        }
    }
}
