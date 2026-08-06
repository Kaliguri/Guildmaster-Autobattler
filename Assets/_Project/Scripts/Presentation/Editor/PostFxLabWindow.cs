using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Guildmaster.Data.Definitions;
using Guildmaster.Presentation.Body;
using Guildmaster.Presentation.Design;
using Guildmaster.Presentation.Effects;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Guildmaster.Presentation.Editor
{
    /// <summary>
    /// Стенд пост-обработки: показывает боевой профиль и профиль карты ЖИВЬЁМ — с bloom, виньеткой и
    /// всем прочим, — не заходя в play-mode. Слева кадр и ручки сцены, справа сам профиль.
    /// </summary>
    /// <remarks>
    /// Заведён после того, как выяснилось, что оценивать свечение было не по чему: кадр из редактора без
    /// <c>renderPostProcessing</c> показывает арт почти без свечения, профиль пост-обработки лежал пустым
    /// и этого никто не видел, а единственным способом посмотреть правду был play-mode.
    /// <para>
    /// <b>Что в кадре — выбирают тумблеры</b> (06.08.2026, слияние с отдельной «витриной блума»). Каждый
    /// светящийся эффект боя — своя ячейка: включил один, кадрировал крупно — смотришь деталь; включил
    /// все — смотришь, как они выглядят вместе и кто из них ярче кого. Вопрос «как выглядит эффект под
    /// постобработкой» один, и вход к нему обязан быть один: пока стендов было два, у одного факта было
    /// два владельца.
    /// </para>
    /// <para>
    /// <b>Всё показывается статикой, без play-mode.</b> Каждый эффект умеет замереть на своей характерной
    /// фазе, потому что фаза у наших эффектов — параметр шейдера, а не состояние скрипта: у формы удара
    /// это <c>_Progress</c>, у дуги <c>_AngleTo</c>, у разлёта <c>_Shatter</c>, у частиц
    /// <c>ParticleSystem.Simulate</c>.
    /// </para>
    /// <para>
    /// Стенд ничего не оставляет за собой: камера, volume и ячейки живут с <see cref="HideFlags.HideAndDontSave"/>,
    /// не попадают в иерархию и в сохранение сцены и уничтожаются вместе с окном. Именно поэтому он
    /// работает в любой открытой сцене, включая чужую. Профиль-ассет при съёмке серии тоже не трогается:
    /// значения крутятся на временной копии (<see cref="PostFxScratch"/>).
    /// </para>
    /// </remarks>
    public sealed class PostFxLabWindow : EditorWindow
    {
        const string BattleProfilePath = "Assets/Settings/PostFX/BattlePostFX_Base.asset";
        const string MapProfilePath    = "Assets/Settings/PostFX/MapPostFX.asset";
        const string FeelConfigPath    = "Assets/_Project/ScriptableObjects/Configs/CombatFeelConfig.asset";
        const string PalettePath       = "Assets/_Project/ScriptableObjects/Configs/CombatColorPalette.asset";
        // ИГРОВОЙ вид юнита, а не голый риг. Разница не косметическая: обёртка `UnitView_*` масштабирует
        // тело в 2.48 раза, и без неё линейка мельче настоящего юнита во столько же раз — то есть врёт
        // ровно там, где она единственный источник правды о размере.
        const string DefaultSubject    = "Assets/_Project/Prefabs/Units/UnitView_BoneStorybook.prefab";
        const string BalancePath       = "Assets/_Project/ScriptableObjects/Configs/ClassBalanceConfig.asset";
        /// <summary>«Атака 1» — обычный удар мечом; дуга показывается на нём (выбор Макса 06.08.2026).</summary>
        const string AttackClipPath    = "Assets/_Project/Prefabs/Bones/Attack.anim";
        const string ShotFolder        = "Temp/PostFxLab";

        // Серия вариаций едет СРАЗУ в лабораторию, а не в Temp: кадр, оставшийся во временной папке,
        // живёт до первой чистки и не попадает ни в git, ни на глаза.
        const string SeriesFolder  = "../docs/lab/assets/bloom-showcase";
        const string ManifestPath  = "../docs/lab/data/bloom-showcase.json";

        /// <summary>Далеко от любой живой сцены: стенд не должен попадать в чужие камеры и коллайдеры.</summary>
        static readonly Vector3 StageOrigin = new Vector3(10000f, 10000f, 0f);

        /// <summary>
        /// Кадр стенда всегда КВАДРАТНЫЙ и всегда одного размера в мире. Подгонять его под габарит
        /// эффекта нельзя: масштаб поехал бы от того, что выбрано, и два снимка перестали бы
        /// сравниваться — а сравнение здесь единственная цель.
        /// </summary>
        const float FrameSizeWorld = 5.0f;

        /// <summary>
        /// Насколько левее центра стоит юнит-линейка: достаточно, чтобы не лезть под эффект, и не
        /// настолько, чтобы самой уехать за край кадра — обрезанная линейка меряет хуже целой.
        /// </summary>
        const float RulerOffsetX = 1.9f;

        /// <summary>
        /// Фаза, на которой снимается серия: эффект уже раскрыт, но ещё не начал гаснуть. Одна на все
        /// эффекты — иначе вариации отличались бы не яркостью, а моментом, в который застали удар.
        /// </summary>
        const float CanonPhase = 0.45f;

        static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        /// <summary>Что показывает стенд. Ровно одно за раз — смотрят и подбирают по одному эффекту.</summary>
        enum Cell
        {
            HitSlash, HitPierce, HitBlunt, HitBolt,
            SwingArc, Sparks, ImpactDust, CastBurst,
            CastGlow, BlockFlash, DeathFlash, Shatter
        }

        static readonly (Cell cell, string label)[] Cells =
        {
            (Cell.HitSlash,   "Хит: режущий"),
            (Cell.HitPierce,  "Хит: колющий"),
            (Cell.HitBlunt,   "Хит: дробящий"),
            (Cell.HitBolt,    "Хит: всполох"),
            (Cell.SwingArc,   "Дуга за клинком"),
            (Cell.Sparks,     "Искры"),
            (Cell.ImpactDust, "Пыль удара"),
            (Cell.CastBurst,  "Всплеск каста"),
            (Cell.CastGlow,   "Свечение каста"),
            (Cell.BlockFlash, "Вспышка блока"),
            (Cell.DeathFlash, "Смерть: вспышка"),
            (Cell.Shatter,    "Смерть: осколки"),
        };

        // Набор вариаций для серии. Текущее состояние боевого профиля (1.0 / 0.7) стоит последним —
        // жалоба была «слишком ярко и слишком широко», значит смотреть надо, куда двигаться ВНИЗ.
        static readonly (float intensity, float scatter)[] Variations =
        {
            (0.35f, 0.45f),
            (0.55f, 0.55f),
            (0.75f, 0.60f),
            (1.00f, 0.70f),
        };

        enum StageKind { Battle, Map }

        [SerializeField] private StageKind  _stage = StageKind.Battle;
        [SerializeField] private bool       _postOn = true;
        [SerializeField] private GameObject _subjectPrefab;
        [SerializeField] private Color      _background = new Color(0.08f, 0.08f, 0.10f, 1f);
        [SerializeField] private float      _zoom = 1f;
        [SerializeField] private Vector2    _pan;
        [SerializeField] private int         _shotHeight = 1440;

        [Header("Что в кадре")]
        [SerializeField] private Cell _cell = Cell.HitSlash;
        /// <summary>Юнит-линейка сбоку: без него «мелко» и «крупно» не с чем сравнить.</summary>
        [SerializeField] private bool _showRuler = true;

        /// <summary>
        /// Вес удара задаётся УРОНОМ и запасом цели, а не долей напрямую: доля — величина расчётная, и
        /// вбитая руками она молча уезжает мимо игры. Прежний фикс 0.25 при пороге тяжёлого удара 0.15
        /// означал, что стенд всё время показывал САМЫЙ тяжёлый удар в игре и выдавал его за обычный.
        /// </summary>
        [SerializeField] private float _hitDamage = 100f;
        [SerializeField] private float _targetMaxHp;

        [Header("Проигрывание")]
        /// <summary>
        /// Фаза эффекта 0..1. Раньше стенд ставил её константой 0.45 и молчал о том, что фаза вообще
        /// есть, — эффект выглядел то так, то иначе, и это читалось как «спавнится и исчезает».
        /// </summary>
        /// <remarks>
        /// Проигрывание — состояние ПО УМОЛЧАНИЮ, пауза — то, что жмут (решено с Максом 06.08.2026).
        /// Эффект живёт движением, и статичный кадр — частный случай, нужный чтобы разглядеть, а не
        /// чтобы им пользоваться постоянно.
        /// </remarks>
        [SerializeField, Range(0f, 1f)] private float _phase = 0.45f;
        [SerializeField] private bool  _playing = true;

        /// <summary>
        /// Множитель НАСТОЯЩЕГО времени эффекта. ×1 — ровно так, как это видит игрок; замедление нужно,
        /// чтобы разглядеть, но оно обязано быть заявленным, а не подразумеваться.
        /// </summary>
        [SerializeField] private float _timeScale = 1f;

        /// <summary>Сколько эффект живёт на самом деле, секунды. Ставит ячейка при сборке.</summary>
        private float _phaseDuration = 0.5f;

        static readonly float[] TimeScales = { 0.1f, 0.25f, 0.5f, 1f, 2f };

        /// <summary>
        /// Как текущий эффект показывает свою фазу. Заполняется ячейкой при сборке: только она знает,
        /// что у неё за параметр времени — <c>_Progress</c>, угол дуги, домотка частиц или разлёт.
        /// </summary>
        private Action<float> _applyPhase;
        private double _lastTick;
        /// <summary>Тон, в котором светится стенд. Один на все эффекты: сравниваем ЯРКОСТЬ, а не оттенки.</summary>
        [SerializeField] private UnitTone _tone = UnitTone.Fire;

        [Header("Свечение части")]
        [SerializeField] private CastSource _castSource = CastSource.Auto;
        [SerializeField] private float      _glowAmount = 1f;
        [SerializeField] private float      _glowFlatness = 0f;
        [SerializeField] private float      _glowBloom = 2.5f;

        private GameObject     _root;
        private Camera         _camera;
        private Volume         _volume;
        private RenderTexture  _preview;
        private UnityEditor.Editor _profileEditor;
        private Vector2        _knobScroll;
        [SerializeField] private int _tab;
        private string         _lastShot;

        [MenuItem("Alebardium/VFX/Post FX Lab", priority = 700)]
        static void Open() => GetWindow<PostFxLabWindow>("Post FX Lab").minSize = new Vector2(900f, 560f);

        private void OnEnable()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            _lastTick = EditorApplication.timeSinceStartup;

            if (_subjectPrefab == null)
                _subjectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultSubject);

            var feel = AssetDatabase.LoadAssetAtPath<CombatFeelConfig>(FeelConfigPath);
            if (feel != null)
            {
                _glowBloom    = feel.CastGlowBloomIntensity;
                _glowFlatness = feel.CastGlowFlatness;
            }

            // Запас цели берётся из БАЛАНСА, а не из головы: норма живёт в ClassBalanceConfig, и вбитое
            // здесь число разошлось бы с игрой на первой же правке баланса.
            if (_targetMaxHp <= 0f)
            {
                var balance = AssetDatabase.LoadAssetAtPath<Guildmaster.Data.Definitions.ClassBalanceConfig>(BalancePath);
                _targetMaxHp = balance != null ? balance.BaseHp : 2000f;
            }
        }

        /// <summary>Доля максимального HP, снятая ударом, — то, чем игра меряет вес удара.</summary>
        private float HpDamageFrac => Mathf.Clamp01(_hitDamage / Mathf.Max(1f, _targetMaxHp));

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            TearDownStage();
        }

        /// <summary>
        /// Гоним фазу сами по часам редактора: <c>Update</c> у эффектов в редакторе не идёт (и не должен
        /// — они боевые компоненты, а не редакторные), поэтому проигрывание живёт здесь.
        /// </summary>
        private void Tick()
        {
            if (!_playing) { _lastTick = EditorApplication.timeSinceStartup; return; }

            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - _lastTick);
            _lastTick = now;

            // Фаза бежит по НАСТОЯЩЕЙ длительности эффекта, а не по абстрактной скорости: у формы удара
            // это 0.16 с, у разлёта — почти секунда, и общая «скорость» врала бы про обоих сразу.
            // Цикл, а не одиночный прогон: с одного раза такое не разглядеть.
            _phase += dt * Mathf.Max(0.01f, _timeScale) / Mathf.Max(0.01f, _phaseDuration);
            if (_phase > 1f) _phase -= 1f;

            // Стенд снесён (сменили тон, эффект, субъект) — фазе некуда приезжать, но перерисовать
            // ОБЯЗАТЕЛЬНО: пересборка живёт в OnGUI, а OnGUI приходит только по Repaint. Без этой
            // строки смена любой ручки останавливала проигрывание намертво — стенд ждал перерисовки,
            // перерисовка ждала стенда.
            if (_root == null) { Repaint(); return; }

            _applyPhase?.Invoke(_phase);
            Repaint();
        }

        private void OnGUI()
        {
            EnsureStage();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawPreviewColumn();
                DrawProfileColumn();
            }
        }

        /// <summary>
        /// Слева — только КАДР и управление показом. Настройки уехали в табы справа (заказ Макса
        /// 06.08.2026): под превью их помещалась половина, и до нижних приходилось скроллить вслепую.
        /// </summary>
        private void DrawPreviewColumn()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * 0.52f)))
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    StageKind stage = (StageKind)EditorGUILayout.EnumPopup(_stage, EditorStyles.toolbarPopup, GUILayout.Width(80f));
                    if (stage != _stage) { _stage = stage; RebindProfile(); }

                    _postOn = GUILayout.Toggle(_postOn, _postOn ? "Пост ВКЛ" : "Пост ВЫКЛ",
                        EditorStyles.toolbarButton, GUILayout.Width(84f));

                    if (GUILayout.Button("Снять кадр", EditorStyles.toolbarButton, GUILayout.Width(84f))) SaveShot(_postOn);
                    if (GUILayout.Button("A/B", EditorStyles.toolbarButton, GUILayout.Width(40f))) SaveAb();
                    if (GUILayout.Button("Снять серию", EditorStyles.toolbarButton, GUILayout.Width(90f))) ShootSeries();

                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"×{_zoom:0.##}", EditorStyles.miniLabel, GUILayout.Width(44f));
                    if (GUILayout.Button("Сброс вида", EditorStyles.toolbarButton, GUILayout.Width(84f))) ResetView();
                    if (GUILayout.Button("Пересобрать", EditorStyles.toolbarButton, GUILayout.Width(88f))) TearDownStage();
                }

                Rect frame = GUILayoutUtility.GetRect(10f, 10f, 200f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                HandleNavigation(frame);
                if (Event.current.type == EventType.Repaint)
                {
                    RenderTexture rt = RenderPreview((int)frame.width, (int)frame.height, _postOn);
                    if (rt != null) GUI.DrawTexture(frame, rt, ScaleMode.StretchToFill, false);
                }

                DrawPlaybackBar();
            }
        }

        /// <summary>Управление показом живёт под кадром: оно про то, что видно, а не про настройки.</summary>
        private void DrawPlaybackBar()
        {
            EditorGUI.BeginChangeCheck();

            using (new EditorGUILayout.HorizontalScope())
            {
                bool play = GUILayout.Toggle(_playing, _playing ? "⏸ Пауза" : "▶ Играть",
                    EditorStyles.miniButton, GUILayout.Width(80f));
                if (play != _playing) { _playing = play; _lastTick = EditorApplication.timeSinceStartup; }

                if (GUILayout.Button("⟲", EditorStyles.miniButton, GUILayout.Width(24f)))
                {
                    _phase = 0f;
                    if (_root != null) _applyPhase?.Invoke(_phase);
                }

                // Ползунок ставит паузу ТОЛЬКО когда его действительно тронули. Сравнивать его значение
                // с текущей фазой нельзя: во время проигрывания фаза убегает между кадрами GUI, разница
                // появляется сама собой — и стенд вставал на паузу, стоило мышке пройти мимо.
                EditorGUI.BeginChangeCheck();
                float phase = EditorGUILayout.Slider(_phase, 0f, 1f);
                if (EditorGUI.EndChangeCheck()) { _phase = phase; _playing = false; }

                GUILayout.Label($"{_phase * _phaseDuration:0.00} / {_phaseDuration:0.00} с",
                                EditorStyles.miniLabel, GUILayout.Width(90f));
            }

            // Множитель ступенями, а не ползунком: ×1 — это «как видит игрок», и такое значение
            // должно выбираться одним щелчком, а не ловиться в ползунке.
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Темп");
                for (int i = 0; i < TimeScales.Length; i++)
                {
                    bool on = Mathf.Approximately(_timeScale, TimeScales[i]);
                    GUIStyle style = i == 0 ? EditorStyles.miniButtonLeft
                                   : i == TimeScales.Length - 1 ? EditorStyles.miniButtonRight
                                   : EditorStyles.miniButtonMid;
                    if (GUILayout.Toggle(on, $"×{TimeScales[i]:0.##}", style) && !on)
                        _timeScale = TimeScales[i];
                }
                GUILayout.Space(8f);
                _zoom = EditorGUILayout.Slider(_zoom, 0.1f, 12f);
            }

            if (_root != null) _applyPhase?.Invoke(_phase);
            if (EditorGUI.EndChangeCheck()) Repaint();
        }

        /// <summary>Таб «Эффекты»: всё, что описывает САМ эффект и то, чем его меряют.</summary>
        private void DrawEffectsTab()
        {
            EditorGUI.BeginChangeCheck();

            // Эффект выбирается ОДИН. Список с галочками стоял здесь ровно один заход и был снят:
            // смотрят и подбирают всегда по одному, а набор из нескольких вдобавок плавил масштаб
            // кадра и делал снимки несравнимыми.
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("◄", EditorStyles.miniButtonLeft, GUILayout.Width(24f)))
                    _cell = StepCell(-1);
                if (GUILayout.Button("►", EditorStyles.miniButtonRight, GUILayout.Width(24f)))
                    _cell = StepCell(+1);

                int index = IndexOf(_cell);
                int picked = EditorGUILayout.Popup(index, LabelsOf());
                if (picked != index) _cell = Cells[picked].cell;

                _showRuler = GUILayout.Toggle(_showRuler, "Линейка", EditorStyles.miniButton, GUILayout.Width(64f));
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Чем меряем", EditorStyles.boldLabel);
            _tone = (UnitTone)EditorGUILayout.EnumPopup("Тон свечения", _tone);
            _hitDamage   = EditorGUILayout.FloatField("Урон удара", _hitDamage);
            _targetMaxHp = EditorGUILayout.FloatField("HP цели", _targetMaxHp);

            var feel = AssetDatabase.LoadAssetAtPath<CombatFeelConfig>(FeelConfigPath);
            float heavy = feel != null ? feel.HeavyHitFrac : 0.15f;
            EditorGUILayout.LabelField(" ",
                $"{HpDamageFrac * 100f:0.#}% запаса · тяжёлым считается от {heavy * 100f:0.#}%" +
                (HpDamageFrac >= heavy ? " — это уже ПОТОЛОК размера" : ""),
                EditorStyles.miniLabel);

            var prefab = (GameObject)EditorGUILayout.ObjectField("Субъект", _subjectPrefab, typeof(GameObject), false);
            if (prefab != _subjectPrefab) { _subjectPrefab = prefab; TearDownStage(); }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Свечение части", EditorStyles.boldLabel);
            _castSource   = (CastSource)EditorGUILayout.EnumPopup("Чем исполнен приём", _castSource);
            _glowAmount   = EditorGUILayout.Slider("Сила", _glowAmount, 0f, 1f);
            _glowFlatness = EditorGUILayout.Slider("Плоскость", _glowFlatness, 0f, 1f);
            _glowBloom    = EditorGUILayout.Slider("Множитель под bloom", _glowBloom, 1f, 5f);

            if (EditorGUI.EndChangeCheck()) { TearDownStage(); Repaint(); }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Кадр", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _background = EditorGUILayout.ColorField("Фон", _background);
            _shotHeight = EditorGUILayout.IntSlider("Высота снимка", _shotHeight, 720, 2160);
            if (EditorGUI.EndChangeCheck()) Repaint();

            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox("Колесо — приближение, средняя кнопка или Alt+ЛКМ — возить кадр. " +
                                    "Серия всегда снимается в каноническом виде, как бы ты сейчас ни смотрел.",
                                    MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Записать значения в конфиг"))  WriteGlowToConfig();
                if (GUILayout.Button("Открыть папку кадров"))        EditorUtility.RevealInFinder(ShotFolder);
            }

            if (!string.IsNullOrEmpty(_lastShot))
                EditorGUILayout.HelpBox("Последний кадр: " + _lastShot, MessageType.None);
        }

        /// <summary>
        /// Колесо приближает, перетаскивание возит кадр. Живёт здесь, а не ползунками: разглядывать
        /// эффект приходится в тех же движениях, что и всё остальное на экране, и ползунок вместо
        /// колеса — это лишний перевод намерения в число.
        /// </summary>
        /// <remarks>
        /// Ни зум, ни сдвиг НЕ пересобирают стенд: они меняют только камеру. Пересборка на каждый
        /// щелчок колеса резала бы эффект заново (а частицы ещё и домотывались бы с нуля) — стенд
        /// дёргался бы на ровном месте.
        /// </remarks>
        private void HandleNavigation(Rect frame)
        {
            Event e = Event.current;
            if (e == null || !frame.Contains(e.mousePosition)) return;

            if (e.type == EventType.ScrollWheel)
            {
                _zoom = Mathf.Clamp(_zoom * (1f - e.delta.y * 0.03f), 0.1f, 12f);
                e.Use();
                Repaint();
                return;
            }

            // Тащим средней кнопкой или Alt+левой — как в сцене; левая без модификатора остаётся
            // свободной, чтобы окно не воевало с обычным кликом по нему.
            bool dragging = e.type == EventType.MouseDrag && (e.button == 2 || (e.button == 0 && e.alt));
            if (dragging)
            {
                // Пиксели в мир: высота кадра — это две ортографические полу-высоты.
                float worldPerPixel = (_camera != null ? _camera.orthographicSize * 2f : FrameSizeWorld)
                                      / Mathf.Max(1f, frame.height);
                _pan += new Vector2(-e.delta.x, e.delta.y) * worldPerPixel;
                e.Use();
                Repaint();
            }
        }

        static int IndexOf(Cell cell)
        {
            for (int i = 0; i < Cells.Length; i++) if (Cells[i].cell == cell) return i;
            return 0;
        }

        static string[] LabelsOf()
        {
            var names = new string[Cells.Length];
            for (int i = 0; i < Cells.Length; i++) names[i] = Cells[i].label;
            return names;
        }

        /// <summary>Соседний эффект по кругу: перебор стрелками — основной способ смотреть их подряд.</summary>
        Cell StepCell(int delta)
        {
            int i = (IndexOf(_cell) + delta + Cells.Length) % Cells.Length;
            return Cells[i].cell;
        }

        // --- Правая колонка: два таба -------------------------------------------------------------------

        /// <summary>
        /// Настройки разведены по табам, потому что это два разных вопроса: «как настроен ЭКРАН» и
        /// «как устроен ЭФФЕКТ». Раньше они лежали в одной колонке под превью и не помещались.
        /// </summary>
        private void DrawProfileColumn()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                _tab = GUILayout.Toolbar(_tab, TabNames);
                EditorGUILayout.Space(4f);

                using (var scroll = new EditorGUILayout.ScrollViewScope(_knobScroll))
                {
                    _knobScroll = scroll.scrollPosition;
                    if (_tab == 0) DrawPostFxTab();
                    else           DrawEffectsTab();
                }
            }
        }

        static readonly string[] TabNames = { "Постпроцессинг", "Эффекты" };

        private void DrawPostFxTab()
        {
            {
                VolumeProfile profile = CurrentProfile();
                EditorGUILayout.LabelField(_stage == StageKind.Battle ? "Боевой профиль" : "Профиль карты",
                    EditorStyles.boldLabel);

                if (profile == null)
                {
                    EditorGUILayout.HelpBox("Профиль не найден: " + CurrentProfilePath(), MessageType.Error);
                    return;
                }

                // Пустой профиль — та самая тихая поломка: bloom «есть» в тумблере и отсутствует в кадре.
                if (profile.components.Count == 0)
                    EditorGUILayout.HelpBox("В профиле НЕТ компонентов — пост-обработки не будет вовсе.",
                        MessageType.Warning);

                if (_profileEditor == null || _profileEditor.target != profile)
                {
                    if (_profileEditor != null) DestroyImmediate(_profileEditor);
                    _profileEditor = UnityEditor.Editor.CreateEditor(profile);
                }

                // Инспектор профиля правит АССЕТ напрямую — это и есть «как поменять постпроцессинг»:
                // крутишь здесь, значение уезжает в BattlePostFX_Base и играет в бою.
                EditorGUI.BeginChangeCheck();
                _profileEditor.OnInspectorGUI();
                if (EditorGUI.EndChangeCheck()) Repaint();
            }
        }

        // --- Стенд ---------------------------------------------------------------------------------------

        private string CurrentProfilePath() => _stage == StageKind.Battle ? BattleProfilePath : MapProfilePath;

        private VolumeProfile CurrentProfile() =>
            AssetDatabase.LoadAssetAtPath<VolumeProfile>(CurrentProfilePath());

        private void RebindProfile()
        {
            if (_volume != null) _volume.sharedProfile = CurrentProfile();
            Repaint();
        }

        private void EnsureStage()
        {
            if (_root != null) return;

            _root = new GameObject("PostFxLab (temp)") { hideFlags = HideFlags.HideAndDontSave };
            _root.transform.position = StageOrigin;

            var camGo = new GameObject("Camera") { hideFlags = HideFlags.HideAndDontSave };
            camGo.transform.SetParent(_root.transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.allowHDR     = true;
            _camera.clearFlags   = CameraClearFlags.SolidColor;
            _camera.enabled      = false;   // рисуем только по требованию, кадр за кадром
            var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = true;
            urp.antialiasing = AntialiasingMode.None;

            var volGo = new GameObject("Volume") { hideFlags = HideFlags.HideAndDontSave };
            volGo.transform.SetParent(_root.transform, false);
            _volume = volGo.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 10000f;      // перебиваем сценные volume: в кадре должен быть ИМЕННО выбранный профиль
            _volume.sharedProfile = CurrentProfile();

            BuildCells();
        }

        private void BuildCells()
        {
            var feel    = AssetDatabase.LoadAssetAtPath<CombatFeelConfig>(FeelConfigPath);
            var palette = AssetDatabase.LoadAssetAtPath<CombatColorPalette>(PalettePath);
            if (feel == null || palette == null)
            {
                Debug.LogError("[PostFxLab] не найден feel-конфиг или боевая палитра — эффект собрать не из чего.");
                return;
            }

            var holder = new GameObject("Cell " + Cells[IndexOf(_cell)].label) { hideFlags = HideFlags.HideAndDontSave };
            holder.transform.SetParent(_root.transform, false);
            holder.transform.position = StageOrigin;

            _applyPhase = null;
            try
            {
                BuildCell(_cell, holder.transform, feel, palette.UnitMain(_tone), palette.UnitSpread(_tone));
                _applyPhase?.Invoke(_phase);   // собранный эффект сразу встаёт в текущую фазу, а не в нулевую
            }
            catch (Exception e)
            {
                Debug.LogError($"[PostFxLab] эффект «{Cells[IndexOf(_cell)].label}» не собрался: {e.Message}");
            }

            if (_showRuler && NeedsRuler(_cell)) BuildRuler(holder.transform);

            FrameStage();
        }

        /// <summary>
        /// Нужна ли эффекту линейка. У ячеек со своим юнитом (свечение, блок, смерть) масштаб виден и
        /// так — второе тело рядом только запутает, кто из них показывает эффект.
        /// </summary>
        static bool NeedsRuler(Cell cell) =>
            cell != Cell.CastGlow && cell != Cell.BlockFlash && cell != Cell.DeathFlash && cell != Cell.Shatter;

        /// <summary>
        /// Юнит-линейка сбоку от эффекта: мера роста, по которой читается его размер. Стоит именно
        /// СБОКУ, а не под эффектом, — крупные формы закрыли бы тело целиком, и линейка перестала бы
        /// быть линейкой.
        /// </summary>
        /// <remarks>
        /// Приглушён намеренно: белое тело ярче половины порога попало бы в блум своей же светлотой и
        /// подсветилось бы рядом с эффектом, который мы как раз и пришли мерить. Тинт держит его ниже
        /// колена, поэтому линейка в замере не участвует.
        /// </remarks>
        private void BuildRuler(Transform at)
        {
            var go = Spawn(_subjectPrefab, at);
            go.transform.localPosition = new Vector3(-RulerOffsetX, 0f, 0f);

            // Красим НАПРЯМУЮ по рендерерам, а не через BodyVisualState: у боевого шва тинт едет вместе
            // с эффектами, и состояние «только тинт, всё остальное по нулям» он законно считает пустым и
            // не применяет вовсе. Линейка — предмет стенда, а не юнит боя, и красить её можно прямо.
            foreach (SpriteRenderer r in go.GetComponentsInChildren<SpriteRenderer>(true))
                r.color = new Color(0.26f, 0.26f, 0.30f, 1f);
        }

        private void BuildCell(Cell cell, Transform at, CombatFeelConfig feel, Color main, Gradient spread)
        {
            switch (cell)
            {
                case Cell.HitSlash:   BuildHitForm(at, feel, HitFormKind.Slash,  main, endsAtHit: false); break;
                case Cell.HitPierce:  BuildHitForm(at, feel, HitFormKind.Pierce, main, endsAtHit: false); break;
                case Cell.HitBlunt:   BuildHitForm(at, feel, HitFormKind.Blunt,  main, endsAtHit: true);  break;
                case Cell.HitBolt:    BuildHitForm(at, feel, HitFormKind.Bolt,   main, endsAtHit: false); break;
                case Cell.SwingArc:   BuildSwingArc(at, feel, main); break;
                case Cell.Sparks:     BuildParticles(at, feel.VfxHitSpark,   spread); break;
                case Cell.ImpactDust: BuildParticles(at, feel.VfxImpactDust, spread); break;
                case Cell.CastBurst:  BuildParticles(at, feel.VfxCastBurst,  spread); break;
                case Cell.CastGlow:   BuildUnitGlow(at, main, block: false, feel); break;
                case Cell.BlockFlash: BuildUnitGlow(at, main, block: true,  feel); break;
                case Cell.DeathFlash: BuildDeathFlash(at, feel); break;
                case Cell.Shatter:    BuildShatter(at, feel, spread); break;
            }
        }

        /// <summary>
        /// Кадр — квадрат фиксированного размера в мире, приближение только ручкой. Подгонять его под
        /// габарит эффекта нельзя: масштаб поехал бы от выбора, и снимки перестали бы сравниваться.
        /// </summary>
        private void FrameStage()
        {
            if (_camera == null) return;

            _camera.transform.position = new Vector3(StageOrigin.x + _pan.x, StageOrigin.y + _pan.y,
                                                     StageOrigin.z - 10f);
            _camera.orthographicSize   = FrameSizeWorld * 0.5f / Mathf.Max(0.01f, _zoom);
            _camera.aspect             = 1f;
        }

        /// <summary>Канонический кадр: тот, в котором снимаются серии и который сравним между эффектами.</summary>
        private void ResetView()
        {
            _zoom = 1f;
            _pan  = Vector2.zero;
            Repaint();
        }

        static void SetHideFlagsRecursively(Transform node)
        {
            node.gameObject.hideFlags = HideFlags.HideAndDontSave;
            for (int i = 0; i < node.childCount; i++) SetHideFlagsRecursively(node.GetChild(i));
        }

        private void TearDownStage()
        {
            if (_profileEditor != null) { DestroyImmediate(_profileEditor); _profileEditor = null; }
            if (_preview != null) { _preview.Release(); DestroyImmediate(_preview); _preview = null; }
            if (_root != null) { DestroyImmediate(_root); _root = null; }
            _camera = null; _volume = null;

            // Делегат фазы держит ССЫЛКИ на объекты стенда, и пережить стенд он не имеет права: иначе
            // следующий же кадр GUI дёрнет его по уничтоженным объектам. Собирается он вместе с ячейкой
            // и умирает вместе с ней.
            _applyPhase = null;
        }

        /// <summary>Кадр стенда. Свечение подаётся боевым путём — через шов тела, не записью в материал.</summary>
        private RenderTexture RenderPreview(int width, int height, bool post)
        {
            if (_camera == null || width < 8 || height < 8) return null;

            if (_preview == null || _preview.width != width || _preview.height != height)
            {
                if (_preview != null) { _preview.Release(); DestroyImmediate(_preview); }
                _preview = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR);
            }

            // Кадрируем на КАЖДЫЙ кадр: зум и сдвиг меняют только камеру, и гонять ради них пересборку
            // стенда было бы и медленно, и неверно — частицы домотывались бы заново.
            FrameStage();

            _camera.backgroundColor = _background;
            _camera.GetUniversalAdditionalCameraData().renderPostProcessing = post;
            _volume.enabled = post;

            _camera.targetTexture = _preview;
            _camera.Render();
            _camera.targetTexture = null;
            return _preview;
        }

        // --- Ячейки ---------------------------------------------------------------------------------------

        /// <summary>
        /// Форма удара, замершая на пике. Фаза выставляется руками: <c>_Progress</c> двигает
        /// <c>Update</c>, которого в редакторе нет, и без этого форма стояла бы в нулевом кадре — то есть
        /// невидимой.
        /// </summary>
        private void BuildHitForm(Transform at, CombatFeelConfig feel, HitFormKind kind, Color rim, bool endsAtHit)
        {
            GameObject prefab = feel.VfxHitForm != null ? feel.VfxHitForm.Prefab : null;
            if (prefab == null) throw new InvalidOperationException("не назначен префаб формы удара");

            var go = Spawn(prefab, at);
            var vfx = go.GetComponentInChildren<HitFormVfx>(true);
            if (vfx == null) throw new InvalidOperationException("в префабе формы нет HitFormVfx");

            HitFormParams p = HitFormFactory.Build(feel, kind, at.position, Vector2.right,
                hpDamageFrac: HpDamageFrac, core: feel.HitFormCoreColor, rim: rim,
                seed: 0x5EEDu + (uint)kind, endsAtHit: endsAtHit, freezeSeconds: 0f);

            vfx.Apply(in p);
            _phaseDuration = p.Life;   // ровно столько форма живёт в бою
            _applyPhase = t => SetFloat(go, "_Progress", t);
        }

        /// <summary>
        /// Дуга за клинком в середине взмаха. Источник-риг ей не нужен: геометрия дуги — это пара углов
        /// в шейдере, и статике достаточно их.
        /// </summary>
        /// <summary>
        /// Дуга за клинком на ЖИВОЙ анимации атаки: юнит играет клип, дуга заметает сектор за настоящим
        /// остриём, длительность — весь клип, а не одна дуга.
        /// </summary>
        /// <remarks>
        /// Геометрия берётся ТЕМ ЖЕ путём, что в бою (<c>UnitView.TryGetSwingArc</c>): бьющая часть из
        /// реестра, остриё через <see cref="UnitPartGeometry"/>, центр вращения — плечо той же стороны.
        /// Считать её здесь по-своему значило бы завести второго владельца правды о взмахе, и стенд
        /// начал бы показывать дугу, которой в бою нет.
        /// <para>
        /// Позу гоним <c>SampleAnimation</c>, а не аниматором: в редакторе он не тикает, а нам нужна
        /// поза в произвольной фазе — в том числе на паузе.
        /// </para>
        /// </remarks>
        private void BuildSwingArc(Transform at, CombatFeelConfig feel, Color colour)
        {
            GameObject prefab = feel.VfxSwingArc != null ? feel.VfxSwingArc.Prefab : null;
            if (prefab == null) throw new InvalidOperationException("не назначен префаб дуги");

            GameObject unit = Spawn(_subjectPrefab, at);
            var body = unit.GetComponentInChildren<SkeletalBodyVisual>(true);
            if (body == null) throw new InvalidOperationException("в субъекте нет SkeletalBodyVisual");

            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AttackClipPath);
            if (clip == null) throw new InvalidOperationException("не найден клип атаки: " + AttackClipPath);

            // Корень путей клипа ищем ПО САМИМ КОСТЯМ, а не по носителю Animator: у сторибук-вида
            // аниматор висит на «Body», а скелет лежит под «BoneVisual», и от аниматора пути клипа
            // («Hips/Torso/…») не разрешаются вовсе. Промах здесь молчаливый: поза просто остаётся
            // префабной, будто анимации нет.
            GameObject rig = FindClipRoot(unit, clip);
            if (rig == null) throw new InvalidOperationException(
                "не найден корень, от которого клип адресует кости — сэмплировать нечего");

            var go = Spawn(prefab, at);
            var arc = go.GetComponentInChildren<SwingArcVfx>(true);
            if (arc == null) throw new InvalidOperationException("в префабе дуги нет SwingArcVfx");

            // Begin поднимает эффект и раскладывает СТИЛЬ из конфига — без него дуга существует, но не
            // показывается, и настройки пришлось бы дублировать здесь вторым списком.
            var sampler = new ClipSampler(clip, rig);
            var source = new ClipSwingSource();
            // Сортировка — из ДАННЫХ эффекта, как в бою (CombatVfx кладёт туда же). Без этого стенд
            // показывал дугу поверх клинка просто потому, что префаб лежит в сцене как попало.
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.sortingLayerID = SortingLayer.NameToID(feel.VfxSwingArc.SortingLayerName);
                r.sortingOrder   = feel.VfxSwingArc.SortingOrder;
            }

            // ОКНО ВЗМАХА — то же, что у боя: дуга живёт не весь клип, а от маркера StrikeStart до
            // StrikeEnd, дальше догорает. Показывать её всю анимацию значило бы показывать эффект,
            // которого в игре нет.
            bool hasWindow = Data.Definitions.ClipMarkers.StrikeWindowNormalized(clip, out float from, out float to);
            if (!hasWindow) { from = 0f; to = 1f; }

            sampler.Sample(0f);
            source.Swinging = false;
            SwingArcGeometry.TryResolve(body, out source.Pivot, out source.Tip, out _);

            arc.Begin(source, colour, feel.SwingArcInnerShare, feel.SwingArcTailBias,
                      feel.SwingArcFadeOut, feel.SwingArcMaxSpanDeg, feel.SwingArcStyle);

            // Весь клип целиком, а не только время дуги: смотрят ВЗМАХ, а дуга — его часть.
            _phaseDuration = Mathf.Max(0.05f, clip.length);

            float lastNorm = 0f;

            _applyPhase = t =>
            {
                if (unit == null || arc == null) return;

                float norm = Mathf.Clamp01(t);
                sampler.Sample(norm * clip.length);

                // Взмах идёт ровно внутри окна: вне его источник отвечает «нечего показывать», и дуга
                // сама уходит в догорание — тем же путём, что в бою после конца удара.
                bool inWindow = norm >= from && norm <= to;
                source.Swinging = inWindow &&
                    SwingArcGeometry.TryResolve(body, out source.Pivot, out source.Tip, out _);

                // Цикл повторяется: на новом заходе дугу надо ЗАВЕСТИ заново — в бою её на каждый взмах
                // спавнит презентер, здесь роль спавна играет возврат фазы к началу.
                if (norm < lastNorm)
                {
                    SwingArcGeometry.TryResolve(body, out source.Pivot, out source.Tip, out _);
                    arc.Begin(source, colour, feel.SwingArcInnerShare, feel.SwingArcTailBias,
                              feel.SwingArcFadeOut, feel.SwingArcMaxSpanDeg, feel.SwingArcStyle);
                }

                float dt = (norm - lastNorm) * clip.length;
                lastNorm = norm;

                // Дугу ведёт САМ эффект — тем же кодом, что в бою. Стенд только ставит позу и отдаёт
                // время; считать здесь угол, сектор и затухание значило бы завести вторую правду о
                // взмахе, и она уже разошлась с боевой.
                arc.Tick(dt > 0f ? dt : Mathf.Max(1e-4f, clip.length * 0.01f));
            };
        }

        /// <summary>
        /// Поза клипа, разложенная ПО КРИВЫМ вручную: план строится один раз, дальше применяется за
        /// проход по костям.
        /// </summary>
        /// <remarks>
        /// Два очевидных пути не работают, и оба молча. <c>clip.SampleAnimation</c> вне play-mode
        /// генерик-клип по костям не раскладывает вовсе. <see cref="AnimationMode"/> раскладывает, но
        /// не видит объекты с <see cref="HideFlags.HideAndDontSave"/> — а весь стенд состоит именно из
        /// таких, чтобы не мусорить в сцене. В обоих случаях поза остаётся префабной, и выглядит это как
        /// «анимация не играет», хотя ошибки нигде нет.
        /// <para>
        /// Прямое чтение кривых от этого свободно и заодно ничего не включает глобально: соседнее окно
        /// анимации и инспектор остаются в своём состоянии.
        /// </para>
        /// </remarks>
        private sealed class ClipSampler
        {
            private readonly List<(Transform target, AnimationCurve[] pos, AnimationCurve[] rot, AnimationCurve[] scale)> _plan
                = new List<(Transform, AnimationCurve[], AnimationCurve[], AnimationCurve[])>();

            public ClipSampler(AnimationClip clip, GameObject root)
            {
                if (clip == null || root == null) return;

                var byTarget = new Dictionary<Transform, (AnimationCurve[] pos, AnimationCurve[] rot, AnimationCurve[] scale)>();

                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    Transform target = string.IsNullOrEmpty(binding.path)
                        ? root.transform
                        : root.transform.Find(binding.path);
                    if (target == null) continue;

                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null) continue;

                    if (!byTarget.TryGetValue(target, out var slot))
                        slot = (new AnimationCurve[3], new AnimationCurve[3], new AnimationCurve[3]);

                    int axis = AxisOf(binding.propertyName);
                    if (axis < 0) continue;

                    if (binding.propertyName.StartsWith("m_LocalPosition"))      slot.pos[axis]   = curve;
                    else if (binding.propertyName.StartsWith("localEulerAngles")) slot.rot[axis]   = curve;
                    else if (binding.propertyName.StartsWith("m_LocalScale"))     slot.scale[axis] = curve;
                    else continue;

                    byTarget[target] = slot;
                }

                foreach (var pair in byTarget)
                    _plan.Add((pair.Key, pair.Value.pos, pair.Value.rot, pair.Value.scale));
            }

            static int AxisOf(string property) =>
                property.EndsWith(".x") ? 0 : property.EndsWith(".y") ? 1 : property.EndsWith(".z") ? 2 : -1;

            public void Sample(float time)
            {
                foreach (var (target, pos, rot, scale) in _plan)
                {
                    if (target == null) continue;
                    if (pos[0] != null || pos[1] != null || pos[2] != null)
                        target.localPosition = Blend(target.localPosition, pos, time);
                    if (rot[0] != null || rot[1] != null || rot[2] != null)
                        target.localEulerAngles = Blend(target.localEulerAngles, rot, time);
                    if (scale[0] != null || scale[1] != null || scale[2] != null)
                        target.localScale = Blend(target.localScale, scale, time);
                }
            }

            /// <summary>Ось без своей кривой остаётся как есть: клип правит не всё подряд.</summary>
            static Vector3 Blend(Vector3 current, AnimationCurve[] axes, float time) => new Vector3(
                axes[0] != null ? axes[0].Evaluate(time) : current.x,
                axes[1] != null ? axes[1].Evaluate(time) : current.y,
                axes[2] != null ? axes[2].Evaluate(time) : current.z);
        }

        /// <summary>
        /// Найти объект, от которого пути клипа разрешаются в настоящие кости. Проверяем ПЕРВЫЙ путь
        /// клипа на каждом кандидате — это единственный честный признак: имя носителя аниматора,
        /// название узла и структура вида у разных юнитов свои, а совпадение пути не врёт.
        /// </summary>
        static GameObject FindClipRoot(GameObject unit, AnimationClip clip)
        {
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            string probe = bindings.Length > 0 ? bindings[0].path : null;
            if (string.IsNullOrEmpty(probe)) return unit;

            foreach (Transform candidate in unit.GetComponentsInChildren<Transform>(true))
                if (candidate.Find(probe) != null) return candidate.gameObject;

            return null;
        }

        /// <summary>
        /// Источник взмаха для стенда: отдаёт позу, которую стенд поставил клипом. Ответ «взмах
        /// кончился» — тот же, что в бою: он переводит дугу в догорание, и без него хвост не утёк бы.
        /// </summary>
        private sealed class ClipSwingSource : ISwingArcSource
        {
            public Vector3 Pivot, Tip;
            public bool Swinging;

            public bool TryGetSwingArc(out Vector3 pivot, out Vector3 tip, out float progress)
            {
                pivot = Pivot; tip = Tip; progress = 1f;
                return Swinging;
            }
        }

        /// <summary>
        /// Плечо и остриё — ровно как в бою: бьющая часть из реестра, центр вращения — плечо её стороны.
        /// Дуга идёт вокруг ПЛЕЧА, а не кисти: рука — жёсткий рычаг, и вращается вся плоскость удара.
        /// </summary>
        static bool TrySwingGeometry(SkeletalBodyVisual body, out Vector3 pivot, out Vector3 tip)
        {
            pivot = default; tip = default;
            if (body?.Parts == null) return false;
            if (!body.Parts.TryGetStrikeSource(HandSlot.None, out UnitPart source)) return false;
            if (!UnitPartGeometry.TryGetTip(source, out tip)) return false;

            BodySide side = source.Slot == HandSlot.Left ? BodySide.Left
                          : source.Slot == HandSlot.Right ? BodySide.Right
                          : source.Side;

            if (!body.Parts.TryGetBone(RigNaming.ShoulderBone(side), side, out UnitPart shoulder)
                || shoulder.Renderer == null) return false;

            pivot = shoulder.Renderer.transform.position;
            return true;
        }

        /// <summary>
        /// Частицы, домотанные до характерного кадра. Сид фиксируем: без него два прогона стенда дают
        /// разный рой, и сравнивать вариации между собой становится нечем.
        /// </summary>
        private void BuildParticles(Transform at, VfxData data, Gradient spread)
        {
            GameObject prefab = data != null ? data.Prefab : null;
            if (prefab == null) throw new InvalidOperationException("не назначен префаб частиц");

            var go = Spawn(prefab, at);
            var systems = new List<ParticleSystem>();
            float life = 0.5f;

            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.useAutoRandomSeed = false;
                ps.randomSeed = 0x5EED;

                // Цвет приходит с юнита, а не из префаба: один префаб служит всем, и без покраски
                // стенд мерил бы яркость чужого цвета.
                ParticleSystem.MainModule mainModule = ps.main;
                mainModule.startColor = new ParticleSystem.MinMaxGradient(spread.Evaluate(0f), spread.Evaluate(1f));

                life = Mathf.Max(life, mainModule.duration + mainModule.startLifetime.constantMax);
                systems.Add(ps);
            }

            _phaseDuration = life;   // выброс плюс дожитие последней частицы

            // Домотка всегда ОТ НУЛЯ (restart), а не шагами: только так фаза детерминирована и два
            // прогона стенда дают один и тот же рой. Шаговая симуляция копила бы расхождение.
            _applyPhase = t =>
            {
                foreach (ParticleSystem ps in systems)
                    ps.Simulate(Mathf.Max(0.0001f, t * life), withChildren: true, restart: true);
            };
        }

        /// <summary>Свечение на теле: заряд каста либо вспышка щита, поданные боевым швом.</summary>
        private void BuildUnitGlow(Transform at, Color main, bool block, CombatFeelConfig feel)
        {
            var go = Spawn(_subjectPrefab, at);
            var body = go.GetComponentInChildren<SkeletalBodyVisual>(true);
            if (body == null) throw new InvalidOperationException("в субъекте нет SkeletalBodyVisual");

            // Множитель под bloom крутится ручкой стенда, а не берётся из цвета: именно его и подбирают.
            Color glow = new Color(main.r * _glowBloom / Mathf.Max(0.01f, MainBrightnessOf(main)),
                                   main.g * _glowBloom / Mathf.Max(0.01f, MainBrightnessOf(main)),
                                   main.b * _glowBloom / Mathf.Max(0.01f, MainBrightnessOf(main)), 1f);

            PartMask parts = CastGlowMask.Resolve(body.Parts, block ? CastSource.Shield : _castSource);

            // Каст НАЛИВАЕТСЯ к выпуску приёма, блок — вспыхивает и гаснет. Это разные жесты, и общей
            // кривой у них быть не может: свет каста копится, свет блока отвечает на удар.
            //
            // Длительность каста приходит из СИМУЛЯЦИИ (это cast-time приёма), и здесь её взять неоткуда
            // — берём типичную секунду и говорим об этом вслух в подписи. Спад блока свой, из конфига.
            _phaseDuration = block ? Mathf.Max(0.05f, feel.CastGlowRelease) : 1f;

            _applyPhase = t =>
            {
                float amount = block
                    ? _glowAmount * (1f - Mathf.Clamp01(t))
                    : _glowAmount * Mathf.Clamp01(t);

                body.Apply(new BodyVisualState(
                    Color.white,
                    0f, Color.white,
                    0f, Color.white, 1f, 1f, 0f,
                    0f, Color.white,
                    amount, glow, parts, _glowFlatness));
            };
        }

        /// <summary>Во сколько раз цвет уже поднят над LDR — чтобы ручка множителя не умножала повторно.</summary>
        static float MainBrightnessOf(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));

        /// <summary>Пересвет тела перед расколом — самое яркое событие боя, и на стенде это должно быть видно.</summary>
        private void BuildDeathFlash(Transform at, CombatFeelConfig feel)
        {
            var go = Spawn(_subjectPrefab, at);
            var body = go.GetComponentInChildren<SkeletalBodyVisual>(true);
            if (body == null) throw new InvalidOperationException("в субъекте нет SkeletalBodyVisual");

            // Вспышка смерти вспыхивает разом и гаснет: пересвет — это МОМЕНТ, а не состояние, и
            // наливаться ему неоткуда. Живёт она ровно столько, сколько ей отмерено перед расколом.
            _phaseDuration = Mathf.Max(0.05f, feel.ShatterFlashIn + feel.ShatterFlashOut);

            _applyPhase = t => body.Apply(new BodyVisualState(
                Color.white,
                1f - Mathf.Clamp01(t), feel.DeathFlashColor,
                0f, Color.white, 1f, 1f, 0f,
                0f, Color.white,
                0f, Color.white, default, 0f));
        }

        /// <summary>Разлёт на осколки в середине жизни: <c>_Shatter</c> тоже фаза, а не состояние скрипта.</summary>
        private void BuildShatter(Transform at, CombatFeelConfig feel, Gradient spread)
        {
            var unit = Spawn(_subjectPrefab, at);

            // Дробится КАЖДАЯ часть скелета, как и в бою: разлёт одного спрайта оставлял бы юнита стоять
            // целым, а рядом с ним летели бы куски непонятно чего. Мера роста при этом одна на всех —
            // габарит ВСЕГО тела, иначе кисть раскрошится на столько же осколков, что и торс.
            var parts = new List<SpriteRenderer>();
            Bounds body = default;
            bool any = false;
            foreach (SpriteRenderer r in unit.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (r.sprite == null || !r.enabled) continue;
                parts.Add(r);
                if (!any) { body = r.bounds; any = true; } else body.Encapsulate(r.bounds);
            }
            if (parts.Count == 0) throw new InvalidOperationException("у субъекта нет спрайтов — резать нечего");

            float height = Mathf.Max(0.01f, body.size.y);
            var pieces = new List<GameObject>();

            foreach (SpriteRenderer src in parts)
            {
                var go = new GameObject("Shatter " + src.name) { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(src.transform.parent, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                go.AddComponent<DeathShatter>().Play(src, feel, spread, height, null);

                src.enabled = false;   // часть уже развоплотилась — иначе осколки лежат поверх целой
                pieces.Add(go);
            }

            // Порядок стадий тот же, что в бою: сперва пересвет, потом разлёт. Обе длительности —
            // из конфига, а не на глаз: вспышка и разлёт настраиваются по отдельности и уже разошлись.
            float flashSeconds = Mathf.Max(0.01f, feel.ShatterFlashIn + feel.ShatterFlashOut);
            _phaseDuration = flashSeconds + Mathf.Max(0.05f, feel.ShatterDuration);
            float flashShare = flashSeconds / _phaseDuration;

            _applyPhase = t =>
            {
                float flash = t < flashShare ? 1f - t / flashShare : 0f;
                foreach (GameObject go in pieces)
                {
                    SetFloat(go, "_FlashAmount", flash);
                    SetFloat(go, "_Shatter", Mathf.Clamp01(t));
                    SetFloat(go, "_Explode", Mathf.Clamp01(t));
                }
            };
        }

        // Подписи в кадре не рисуем: эффект в кадре ровно один, его имя стоит в выборе стенда и уезжает
        // в манифест, а на сайте становится обычным текстом. Подпись поверх снимка была бы третьей
        // копией того же слова — и единственной, которую нельзя ни перевести, ни поправить.

        private GameObject Spawn(GameObject prefab, Transform at)
        {
            if (prefab == null) throw new InvalidOperationException("префаб не найден");
            GameObject go = Instantiate(prefab, at.position, Quaternion.identity, at);
            SetHideFlagsRecursively(go.transform);

            // Полосы HP и маны едут вместе с игровым видом юнита, но телом не являются: в кадре стенда
            // они лезут поверх эффекта своим масштабом и светятся, а меряем мы не их. Гасим у ЛЮБОГО
            // юнита стенда — и у линейки, и у того, на ком показан эффект.
            foreach (Canvas canvas in go.GetComponentsInChildren<Canvas>(true))
                canvas.enabled = false;

            return go;
        }

        /// <summary>
        /// Дописать свойство в блок рендерера. Именно дописать: <c>GetPropertyBlock</c> возвращает то, что
        /// эффект уже положил при своём <c>Apply</c>/<c>Begin</c>, и заменять блок целиком значило бы
        /// стереть всю его настройку ради одного числа.
        /// </summary>
        static void SetColor(GameObject go, string property, Color value)
        {
            if (go == null) return;
            var renderer = go.GetComponentInChildren<Renderer>(true);
            if (renderer == null) return;

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(Shader.PropertyToID(property), value);
            renderer.SetPropertyBlock(block);
        }

        static void SetFloat(GameObject go, string property, float value)
        {
            // Объект мог уйти вместе со стендом раньше, чем до него добрался делегат фазы: сравнение с
            // null у Unity-объектов ловит и уничтоженные, а не только пустые ссылки.
            if (go == null) return;

            var renderer = go.GetComponentInChildren<Renderer>(true);
            if (renderer == null) return;

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetFloat(Shader.PropertyToID(property), value);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>Источник взмаха для статики: плечо на месте, кончик клинка — на радиусе.</summary>
        private sealed class StaticSwingSource : ISwingArcSource
        {
            private readonly Vector3 _pivot;
            public StaticSwingSource(Vector3 pivot) => _pivot = pivot;

            public bool TryGetSwingArc(out Vector3 pivot, out Vector3 tip, out float progress)
            {
                pivot = _pivot;
                tip = _pivot + new Vector3(1.2f, 0f, 0f);
                progress = 1f;
                return true;
            }
        }

        // --- Кадры и запись значений ---------------------------------------------------------------------

        private void SaveShot(bool post)
        {
            string file = Shot(post, _stage.ToString().ToLowerInvariant() + (post ? "_post" : "_raw"));
            _lastShot = file;
            Debug.Log("[PostFxLab] кадр: " + file);
        }

        private void SaveAb()
        {
            string raw  = Shot(false, _stage.ToString().ToLowerInvariant() + "_raw");
            string post = Shot(true,  _stage.ToString().ToLowerInvariant() + "_post");
            _lastShot = post;
            Debug.Log("[PostFxLab] A/B:\n  " + raw + "\n  " + post);
        }

        /// <summary>
        /// Серия вариаций блума по ВЫБРАННОМУ эффекту. Существует потому, что подбор ползунком — это
        /// сравнение кадра с памятью о прошлом кадре, а глаз к свечению адаптируется за секунды и такое
        /// сравнение проигрывает.
        /// </summary>
        /// <remarks>
        /// Снимается один эффект, а не все сразу: их и смотрят по одному. Манифест при этом
        /// <b>дополняется</b>, а не переписывается — иначе съёмка второго эффекта стирала бы первый, и
        /// собрать на сайте полный набор было бы невозможно в принципе.
        /// <para>
        /// Профиль-ассет не трогается: значения крутятся на временной копии. Правка настоящего профиля
        /// ради замера пометила бы ассет изменённым, и подобранное «на посмотреть» уехало бы в игру молча.
        /// </para>
        /// </remarks>
        private void ShootSeries()
        {
            EnsureStage();

            VolumeProfile source = CurrentProfile();
            if (source == null) { Debug.LogError("[PostFxLab] профиль не найден: " + CurrentProfilePath()); return; }

            VolumeProfile scratch = PostFxScratch.Clone(source);
            if (!scratch.TryGet(out Bloom bloom))
            {
                Debug.LogError("[PostFxLab] в профиле нет Bloom — серию снимать не по чему.");
                PostFxScratch.Destroy(scratch);
                return;
            }

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, SeriesFolder));
            Directory.CreateDirectory(folder);

            string label = Cells[IndexOf(_cell)].label;
            var files = new List<string>();

            // Съёмка идёт из КАНОНИЧЕСКОГО вида, а не из того, как стенд повёрнут прямо сейчас: кадры
            // сравниваются между собой и между эффектами, и приближение, забытое на ×4, тихо сделало бы
            // всю серию несравнимой с остальными.
            float zoomWas = _zoom; Vector2 panWas = _pan;
            _zoom = 1f; _pan = Vector2.zero;

            // Фаза тоже часть канона: снимать бегущий эффект значило бы ловить его в случайный момент,
            // и четыре вариации отличались бы не яркостью, а тем, где застали удар.
            float phaseWas = _phase; bool playingWas = _playing;
            _playing = false;
            _phase = CanonPhase;
            _applyPhase?.Invoke(_phase);

            _volume.sharedProfile = scratch;
            try
            {
                for (int i = 0; i < Variations.Length; i++)
                {
                    (float intensity, float scatter) = Variations[i];
                    EditorUtility.DisplayProgressBar("Post FX Lab",
                        $"{label}: вариация {i + 1} из {Variations.Length} — яркость {intensity:0.00}, растекание {scatter:0.00}",
                        (float)i / Variations.Length);

                    bloom.intensity.overrideState = true; bloom.intensity.value = intensity;
                    bloom.scatter.overrideState   = true; bloom.scatter.value   = scatter;

                    int size = Mathf.Clamp(_shotHeight, 720, 2160);   // кадр квадратный
                    Texture2D tex = Capture(size, size);
                    string file = Path.Combine(folder, FileNameOf(_cell, intensity, scatter));
                    File.WriteAllBytes(file, tex.EncodeToPNG());
                    DestroyImmediate(tex);
                    files.Add(file);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _volume.sharedProfile = source;
                PostFxScratch.Destroy(scratch);
                _zoom = zoomWas; _pan = panWas;
                _phase = phaseWas; _playing = playingWas;
                _applyPhase?.Invoke(_phase);
            }

            WriteManifest();
            _lastShot = files.Count > 0 ? files[files.Count - 1] : null;
            Debug.Log($"[PostFxLab] снято вариаций «{label}»: {files.Count}\n  " + string.Join("\n  ", files));
        }

        /// <summary>
        /// Манифест для раздела лаборатории: какие эффекты сняты и в каких вариациях. Пишется прогоном, а
        /// не рукой, — иначе стенд разойдётся с сайтом на первом же новом эффекте, ровно как это было с
        /// прежней витриной элементов интерфейса.
        /// </summary>
        /// <remarks>
        /// Запись СЛИВАЕТСЯ с уже лежащей: снятый сегодня эффект добавляется или обновляет свой блок, а
        /// остальные остаются как были. Иначе манифест всегда описывал бы ровно последнюю съёмку.
        /// </remarks>
        private void WriteManifest()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, ManifestPath));
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            Newtonsoft.Json.Linq.JObject root = null;
            if (File.Exists(path))
            {
                try { root = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path)); }
                catch (Exception e)
                {
                    // Битый манифест не должен молча стать пустым: скажем вслух и соберём заново.
                    Debug.LogWarning("[PostFxLab] манифест не прочитался, пишу заново: " + e.Message);
                }
            }
            root ??= new Newtonsoft.Json.Linq.JObject();

            var effects = root["effects"] as Newtonsoft.Json.Linq.JArray;
            if (effects == null) { effects = new Newtonsoft.Json.Linq.JArray(); root["effects"] = effects; }

            var shots = new Newtonsoft.Json.Linq.JArray();
            for (int i = 0; i < Variations.Length; i++)
            {
                (float intensity, float scatter) = Variations[i];
                shots.Add(new Newtonsoft.Json.Linq.JObject
                {
                    ["file"]      = FileNameOf(_cell, intensity, scatter),
                    ["intensity"] = intensity,
                    ["scatter"]   = scatter,
                    ["current"]   = i == Variations.Length - 1
                });
            }

            var entry = new Newtonsoft.Json.Linq.JObject
            {
                ["id"]    = _cell.ToString(),
                ["label"] = Cells[IndexOf(_cell)].label,
                ["tone"]  = _tone.ToString(),
                ["shots"] = shots
            };

            int at = -1;
            for (int i = 0; i < effects.Count; i++)
                if ((string)effects[i]["id"] == _cell.ToString()) { at = i; break; }

            if (at >= 0) effects[at] = entry;
            else effects.Add(entry);

            File.WriteAllText(path, root.ToString(Newtonsoft.Json.Formatting.Indented) + "\n",
                              new System.Text.UTF8Encoding(false));
        }

        static string FileNameOf(Cell cell, float intensity, float scatter) =>
            $"{cell}_i{intensity.ToString("0.00", Culture)}_s{scatter.ToString("0.00", Culture)}.png";

        private string Shot(bool post, string name)
        {
            // Стенд может быть ещё не собран: кнопку жмут и сразу после смены эффекта, и снаружи
            // (из execute_code), где OnGUI не отрабатывал вовсе.
            EnsureStage();
            if (_camera == null) return "<стенд не собрался>";

            // Фазу применяем ЯВНО: обычно её ставит тик проигрывания или отрисовка окна, но снимок
            // могут просить и мимо них (кнопкой сразу после смены эффекта, скриптом снаружи) — и тогда
            // в кадр уезжает поза, оставшаяся от прошлого раза.
            _applyPhase?.Invoke(_phase);

            const int size = 1024;
            Texture2D tex = Capture(size, Mathf.RoundToInt(size / Mathf.Max(0.1f, _camera.aspect)), post);
            if (tex == null) return "<нет кадра>";

            Directory.CreateDirectory(ShotFolder);
            string file = Path.GetFullPath(Path.Combine(ShotFolder, name + ".png"));
            File.WriteAllBytes(file, tex.EncodeToPNG());
            DestroyImmediate(tex);
            return file;
        }

        /// <summary>Кадр стенда как <c>Texture2D</c>. Вызывающий обязан его уничтожить.</summary>
        private Texture2D Capture(int width, int height, bool post = true)
        {
            RenderTexture rt = RenderPreview(width, height, post);
            if (rt == null) return null;

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }

        /// <summary>
        /// Подобранное свечение уезжает в feel-конфиг: играет ассет, а не ползунок стенда, и забытая ручка
        /// иначе живёт только здесь.
        /// </summary>
        private void WriteGlowToConfig()
        {
            var feel = AssetDatabase.LoadAssetAtPath<CombatFeelConfig>(FeelConfigPath);
            if (feel == null) { Debug.LogError("[PostFxLab] не найден CombatFeelConfig: " + FeelConfigPath); return; }

            // ВСЕ ручки разом, а не выборочно. Прежняя версия писала плоскость и множитель, но теряла
            // СИЛУ — и подобранное значение молча не доезжало до игры: со стенда оно выглядело
            // сохранённым, а в бою оставалось прежним.
            var so = new SerializedObject(feel);
            so.FindProperty("_castGlowStrength").floatValue       = _glowAmount;
            so.FindProperty("_castGlowFlatness").floatValue       = _glowFlatness;
            so.FindProperty("_castGlowBloomIntensity").floatValue = _glowBloom;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(feel);
            AssetDatabase.SaveAssetIfDirty(feel);

            Debug.Log($"[PostFxLab] в конфиг записано: сила {_glowAmount:0.##}, плоскость {_glowFlatness:0.##}, " +
                      $"множитель bloom {_glowBloom:0.##}");
        }
    }
}
