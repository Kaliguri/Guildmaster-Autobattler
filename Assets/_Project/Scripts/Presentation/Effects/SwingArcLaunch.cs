using Guildmaster.Presentation.Design;
using UnityEngine;

namespace Guildmaster.Presentation.Effects
{
    /// <summary>
    /// С ЧЕМ дуга за клинком выходит в кадр: тумблер, яркость, доли радиуса, затухание, стиль следа.
    /// Единственное место, где эти параметры собираются вместе, — его зовут и бой, и редакторный стенд.
    /// </summary>
    /// <remarks>
    /// Раньше раскладка жила в теле <c>CombatPresenter.OnUnitSwingStarted</c>, куда стенду не дотянуться:
    /// презентер собирается контейнером и живёт лентой боя. Стенд повторял её у себя — и это была вторая
    /// правда о дуге, только уже не о геометрии, а о её виде. Расходились такие копии тихо: забытый
    /// множитель яркости или пропущенный тумблер выглядят не как ошибка, а как «в стенде почему-то
    /// иначе».
    /// <para>
    /// Гейт: <see cref="SwingArcVfx.Begin"/> зовётся из проекта ровно отсюда — это проверяет
    /// <c>SwingArcSingleOwnerTests</c>. Без него правило держалось бы на памяти, а она уже подводила.
    /// </para>
    /// </remarks>
    public static class SwingArcLaunch
    {
        /// <summary>
        /// Показываем ли дугу вообще. Спрашивается ДО того, как эффект возьмут из пула: выключенный
        /// тумблер не должен ни занимать объект, ни жаловаться на незаполненный слот в данных.
        /// </summary>
        public static bool Enabled(CombatFeelConfig feel) => feel != null && feel.EnableSwingArc;

        /// <summary>
        /// Завести дугу этого взмаха.
        /// </summary>
        /// <param name="arc">Эффект, взятый из пула (в бою) или поднятый стендом.</param>
        /// <param name="feel">Feel-конфиг — владелец всех чисел дуги.</param>
        /// <param name="source">Кто машет: у него спрашивается геометрия каждый кадр и место в теле — один раз.</param>
        /// <param name="unitGlow">
        /// Цвет свечения бьющего (HDR, уже поднятый под порог bloom). Своей яркостью дуга домножает его
        /// ЗДЕСЬ: она идёт на КАЖДЫЙ взмах, и свет, отмеренный под разовый каст, в непрерывной серии
        /// ударов заливает бой.
        /// </param>
        /// <returns><c>false</c> — дуги не будет: эффект выключен тумблером либо звать нечего.</returns>
        public static bool Begin(SwingArcVfx arc, CombatFeelConfig feel, ISwingArcSource source, Color unitGlow)
        {
            if (arc == null || feel == null || source == null) return false;
            if (!feel.EnableSwingArc) return false;

            float k = feel.SwingArcBrightness;
            var colour = new Color(unitGlow.r * k, unitGlow.g * k, unitGlow.b * k, unitGlow.a);

            arc.Begin(source, colour, feel.SwingArcInnerShare, feel.SwingArcTailBias,
                      feel.SwingArcFadeOut, feel.SwingArcMaxSpanDeg, feel.SwingArcStyle);
            return true;
        }
    }
}
