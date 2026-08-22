using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Guildmaster.UI.Components
{
    /// <summary>
    /// Появление титра: знак и крупная надпись, которые въезжают в кадр и гаснут.
    /// </summary>
    /// <remarks>
    /// <b>ОДИН приём на всю игру</b> — вердикт Макса 22.08.2026 (пункт 7А плана
    /// [[ui-uplift]]): «берем такой эффект появления для "Победы" (в конце боя и в конце игры - свое),
    /// "Поражения", входа в бой (В бой! с мечами). Т.е. будет много где, единый стиль и источник и
    /// переюзание под всякие ситуации». Разница между случаями — ДАННЫЕ (слово, знак, тон,
    /// длительность), а не четыре похожих эффекта по экранам: второй такой же, написанный рядом,
    /// разошёлся бы с первым на первой правке.
    /// <para><b>Движение — на переходах USS, а не на твинах в коде.</b> Тем самым оно живёт в теме
    /// рядом с видом: правит его тот же файл, что задаёт кегль и цвет. Классы состояний ставятся с
    /// задержкой в кадр — иначе переход некуда играть: элемент рождается сразу в конечном виде.</para>
    /// <para><b>Титр НИЧЕГО не решает.</b> Он не ждёт ответа, не ловит клики и не закрывает собой
    /// экран: показался и ушёл. Всё, что требует ответа игрока, — это окно сообщений
    /// (<see cref="NoticeDialogView"/>), и смешивать их нельзя.</para>
    /// </remarks>
    [UxmlElement]
    public partial class TitleReveal : VisualElement
    {
        /// <summary>Тон титра: чем этот момент отличается на вид.</summary>
        public enum Tone
        {
            /// <summary>Нейтральный зов: «В бой!».</summary>
            Call,

            /// <summary>Победа.</summary>
            Triumph,

            /// <summary>Поражение.</summary>
            Defeat,
        }

        private readonly VisualElement _glyph;
        private readonly Label _line;
        private readonly Label _sub;

        public TitleReveal()
        {
            AddToClassList("gm-title-reveal");
            pickingMode = PickingMode.Ignore;   // титр не ловит ввод: он ничего не решает

            _glyph = new VisualElement { name = "title-glyph", pickingMode = PickingMode.Ignore };
            _glyph.AddToClassList("gm-title-reveal__glyph");
            Add(_glyph);

            _line = new Label { name = "title-line", pickingMode = PickingMode.Ignore };
            _line.AddToClassList("gm-text-display");
            _line.AddToClassList("gm-title-reveal__line");
            UiTextCase.Bind(_line);
            Add(_line);

            _sub = new Label { name = "title-sub", pickingMode = PickingMode.Ignore };
            _sub.AddToClassList("gm-text-caption");
            _sub.AddToClassList("gm-title-reveal__sub");
            Add(_sub);
        }

        /// <summary>
        /// Одеть титр: слово, приписка под ним, знак и тон.
        /// </summary>
        /// <param name="line">Крупная строка — то, что игрок прочтёт первым.</param>
        /// <param name="sub">Приписка под строкой. Пусто — строки не будет вовсе.</param>
        /// <param name="glyph">Знак над строкой. <c>null</c> — титр идёт без знака.</param>
        public void Dress(string line, string sub, Texture2D glyph, Tone tone)
        {
            _line.text = line ?? string.Empty;

            _sub.text = sub ?? string.Empty;
            _sub.style.display = string.IsNullOrEmpty(sub) ? DisplayStyle.None : DisplayStyle.Flex;

            if (glyph != null)
            {
                _glyph.style.backgroundImage = new StyleBackground(glyph);
                _glyph.style.display = DisplayStyle.Flex;
            }
            else _glyph.style.display = DisplayStyle.None;

            EnableInClassList(ToneClass(Tone.Call),    tone == Tone.Call);
            EnableInClassList(ToneClass(Tone.Triumph), tone == Tone.Triumph);
            EnableInClassList(ToneClass(Tone.Defeat),  tone == Tone.Defeat);
        }

        /// <summary>
        /// Проиграть появление: въезд, выдержка, уход. <paramref name="onDone"/> зовётся, когда титр
        /// отыграл целиком.
        /// </summary>
        /// <remarks>
        /// Времена приходят СНАРУЖИ, потому что у случаев они разные: «В бой!» обязан уйти до первого
        /// удара, а победа забега держится, пока игрок смотрит. Ставить их внутри значило бы завести
        /// одно время на четыре разных момента.
        /// </remarks>
        public void Play(float holdSeconds, Action onDone = null)
        {
            // Классы ставятся СЛЕДУЮЩИМ кадром: переходу USS нужно, чтобы элемент сперва существовал
            // в начальном виде. Поставь мы их сразу — он родился бы уже в конечном, и играть было бы
            // нечего (та же готча, что у любых transition в UITK).
            schedule.Execute(() => AddToClassList(ShownClass)).ExecuteLater(16);

            // Скобки не для красоты: (long)Mathf.Max(...) * 1000 приводит СЕКУНДЫ к целому ДО
            // умножения, и 1.1 с превращается в 1.0 с, а 0.6 — в ноль.
            long holdMs = (long)(Mathf.Max(0.05f, holdSeconds) * 1000f);
            schedule.Execute(() =>
            {
                RemoveFromClassList(ShownClass);
                AddToClassList(GoneClass);
            }).ExecuteLater(holdMs);

            // Уход длится столько же, сколько появление, — время перехода живёт в теме, здесь запас.
            schedule.Execute(() => onDone?.Invoke()).ExecuteLater(holdMs + 600);
        }

        private static string ToneClass(Tone tone) => tone switch
        {
            Tone.Triumph => "gm-title-reveal--triumph",
            Tone.Defeat  => "gm-title-reveal--defeat",
            _            => "gm-title-reveal--call",
        };

        /// <summary>Класс «титр в кадре» — на нём стоит вся анимация въезда.</summary>
        private const string ShownClass = "gm-title-reveal--shown";

        /// <summary>Класс «титр уходит».</summary>
        private const string GoneClass = "gm-title-reveal--gone";
    }
}
