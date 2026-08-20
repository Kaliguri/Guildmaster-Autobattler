using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// «Игра занята»: вращающееся кольцо и две строки — что идёт и подробность.
    /// </summary>
    /// <remarks>
    /// <b>Почему это контрол, а не разметка экрана.</b> Композиция нужна трём местам — экрану
    /// ожидания, заслонке выхода из игры и экрану запуска, — а до 20.08.2026 кольцо жило только в
    /// разметке бута, и его анимация была выточена внутри <c>TitleCardScreenView</c>. Второй
    /// потребитель означал бы копию хода вращения, а ход у него непростой: он неравномерный по
    /// заказу Макса от 07.08.2026.
    ///
    /// <para><b>Ход кольца неравномерный намеренно.</b> Скорость гуляет по синусу от четверти до
    /// полутора базовых, оборот в среднем за 0.9 с. Линейное вращение читается как «крутится
    /// шестерёнка», а этот — как усилие; фаза считается от НАКОПЛЕННОГО угла, поэтому рывок всегда
    /// приходится на одно место оборота и движение не плывёт между запусками.</para>
    ///
    /// <para><b>Оговорка про выход из игры.</b> На заслонке выхода кольцо докрутить не обещает:
    /// <c>Application.Quit</c> запускает уборку, часть которой блокирует главный поток, и анимация
    /// в этот момент замирает. Надпись там несёт смысл, кольцо — только пока движок жив.</para>
    /// </remarks>
    [UxmlElement]
    public partial class WaitNote : VisualElement
    {
        private readonly VisualElement _ring;
        private readonly Label _title;
        private readonly Label _detail;

        /// <summary>Что происходит: «Подключение к игре», «Выход из игры».</summary>
        [UxmlAttribute]
        public string Title
        {
            get => _title.text;
            set
            {
                _title.text = value;
                _title.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        /// <summary>Подробность строкой ниже: чего именно ждём. Пусто — строки не будет.</summary>
        [UxmlAttribute]
        public string Detail
        {
            get => _detail.text;
            set
            {
                _detail.text = value;
                _detail.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        public WaitNote()
        {
            AddToClassList("gm-wait");
            pickingMode = PickingMode.Ignore;

            _ring = new VisualElement { name = "wait-ring", pickingMode = PickingMode.Ignore };
            _ring.AddToClassList("gm-wait__ring");
            Add(_ring);

            var column = new VisualElement { pickingMode = PickingMode.Ignore };
            column.AddToClassList("gm-wait__column");

            _title = new Label { name = "wait-title" };
            _title.AddToClassList("gm-text-body");
            _title.AddToClassList("gm-wait__title");
            column.Add(_title);

            _detail = new Label { name = "wait-detail" };
            _detail.AddToClassList("gm-text-caption");
            _detail.AddToClassList("gm-text--muted");
            _detail.AddToClassList("gm-wait__detail");
            _detail.style.display = DisplayStyle.None;
            column.Add(_detail);

            Add(column);
            Spin(_ring);
        }

        /// <summary>
        /// Завести вращение у кольца. Публичный, потому что своё кольцо есть у экрана запуска: там
        /// оно стоит в ряду атрибуций и живёт в разметке, а ход обязан быть тем же самым.
        /// </summary>
        public static void Spin(VisualElement ring)
        {
            if (ring == null) return;

            float angle = 0f;
            ring.schedule.Execute(() =>
            {
                float eased = 0.25f + 0.75f * (1f + Mathf.Sin(angle * Mathf.Deg2Rad * 2f)) * 0.5f;
                angle = (angle + 4f * eased) % 360f;
                ring.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));
            }).Every(16);
        }
    }
}
