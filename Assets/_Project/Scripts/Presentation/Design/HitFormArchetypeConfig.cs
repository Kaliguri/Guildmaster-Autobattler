using UnityEngine;

namespace Guildmaster.Presentation.Design
{
    /// <summary>
    /// Правило генерации одного архетипа формы удара. Не набор готовых чисел: длина и радиус звезды
    /// фиксированы (их несёт вес удара), а прогиб, толщина и лучи заданы КОРИДОРАМИ — каждый удар
    /// выбирает своё значение внутри них.
    /// </summary>
    /// <remarks>
    /// <b>Что процедурность НЕ трогает: размер.</b> Он говорит игроку, насколько силён удар, и
    /// случайность в нём отняла бы единственный канал силы. Гуляют прогиб, толщина, неровность краёв и
    /// звезда — то, от чего два удара подряд выглядят по-разному, а архетип остаётся узнаваем.
    /// <para>
    /// Все размеры — <b>в долях H</b> (рост юнита-человека), потому что сетка спрайтов ещё будет
    /// меняться: доли переживут смену, мировые единицы — нет.
    /// </para>
    /// </remarks>
    [System.Serializable]
    public sealed class HitFormArchetypeConfig
    {
        [Tooltip("Полная длина формы в долях H. Ноль = длина не нормируется, а приходит извне " +
                 "(линия-всполох: её длина — вся дистанция выстрела, и она сама по себе сообщение).")]
        [SerializeField] private float _lengthH = 1.4f;

        [Tooltip("Коридор прогиба в долях H: x — минимум, y — максимум. Ноль-ноль = прямая форма.")]
        [SerializeField] private Vector2 _arcH = new Vector2(0.20f, 0.28f);

        [Tooltip("Коридор полутолщины в середине, доли H. К обоим остриям толщина сходит на нет.")]
        [SerializeField] private Vector2 _halfThicknessH = new Vector2(0.055f, 0.075f);

        [Tooltip("Неровность краёв 0..1 — насколько форма отходит от идеальной дуги.")]
        [SerializeField, Range(0f, 1f)] private float _roughness = 0.3f;

        [Tooltip("Радиус звезды в долях H. Больше нуля только у дробящего: у остальных вспышку-трещину " +
                 "в конце заменяет проход навылет.")]
        [SerializeField] private float _starRadiusH;

        [Tooltip("Коридор числа лучей звезды: x — минимум, y — максимум.")]
        [SerializeField] private Vector2Int _starRays = new Vector2Int(7, 9);

        /// <summary>Полная длина формы, доли H. 0 = длину задаёт вызывающий (линия-всполох).</summary>
        public float LengthH => _lengthH;

        /// <summary>Коридор прогиба, доли H.</summary>
        public Vector2 ArcH => _arcH;

        /// <summary>Коридор полутолщины, доли H.</summary>
        public Vector2 HalfThicknessH => _halfThicknessH;

        /// <summary>Неровность краёв 0..1.</summary>
        public float Roughness => _roughness;

        /// <summary>Радиус звезды, доли H (0 — звезды нет).</summary>
        public float StarRadiusH => _starRadiusH;

        /// <summary>Коридор числа лучей звезды.</summary>
        public Vector2Int StarRays => _starRays;

        // --- Стартовые наборы: числа из канона gdd/70-gamefeel/vfx-language §Числа. Живут кодом только
        // как ДЕФОЛТ нового поля; играет всегда ассет, и крутит его Макс.

        /// <summary>Режущий: росчерк 1:12, проходит навылет.</summary>
        public static HitFormArchetypeConfig Slash() => new HitFormArchetypeConfig
        {
            _lengthH = 1.4f,
            _arcH = new Vector2(0.20f, 0.28f),
            _halfThicknessH = new Vector2(0.055f, 0.075f),
            _roughness = 0.3f,
        };

        /// <summary>Колющий: 1:18, заметно тоньше режущего, прогиб почти нулевой.</summary>
        public static HitFormArchetypeConfig Pierce() => new HitFormArchetypeConfig
        {
            // Укол короткий: при обычном ударе его росчерк укладывается примерно в ширину тела, а не
            // прошивает пол-арены. Было 1.1 роста — это полтора юнита в длину (Макс, 04.08.2026).
            _lengthH = 0.3f,
            _arcH = new Vector2(0.015f, 0.035f),
            _halfThicknessH = new Vector2(0.028f, 0.036f),
            _roughness = 0.18f,
        };

        /// <summary>Дробящий: короткий мягкий след до цели плюс обязательная звезда в точке контакта.</summary>
        public static HitFormArchetypeConfig Blunt() => new HitFormArchetypeConfig
        {
            _lengthH = 0.5f,
            _arcH = new Vector2(0.09f, 0.14f),
            _halfThicknessH = new Vector2(0.048f, 0.066f),
            _roughness = 0.35f,
            _starRadiusH = 0.35f,
            _starRays = new Vector2Int(7, 9),
        };

        /// <summary>Линия-всполох выстрела: тот же колющий с нулевым прогибом и длиной по дистанции.</summary>
        public static HitFormArchetypeConfig Bolt() => new HitFormArchetypeConfig
        {
            // Длина СВОЯ, а не «вся дистанция выстрела»: с четырёх единиц полёта прежнее правило рисовало
            // росчерк в восемь единиц — линия через полэкрана вместо знака попадания. Откуда прилетело,
            // по-прежнему говорит НАПРАВЛЕНИЕ (точка A — место выстрела), и оно не потерялось.
            _lengthH = 0.45f,
            _arcH = Vector2.zero,
            _halfThicknessH = new Vector2(0.024f, 0.032f),
            _roughness = 0.12f,
        };
    }
}
