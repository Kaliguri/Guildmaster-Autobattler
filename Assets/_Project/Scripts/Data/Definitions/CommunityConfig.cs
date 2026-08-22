using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Что игра говорит о себе и своём сообществе в главном меню: обращение, ссылки, приглашение
    /// в список желаемого.
    /// </summary>
    /// <remarks>
    /// <b>Ассет, а не константы в коде</b>: адреса сообщества заводятся и меняются вне разработки —
    /// Discord переезжает, канал появляется, страница магазина открывается в свой день. Ни одно из
    /// этих событий не должно требовать правки <c>.cs</c> и сборки.
    /// <para><b>Пустой адрес — законное состояние, а не «забыли».</b> Ссылка без адреса просто не
    /// показывается: заготовка под будущий канал стоит в списке заранее, чтобы в нужный день
    /// вписать строку, а не заводить запись с нуля.</para>
    /// <para><b>Про магазин здесь только «показывать ли»</b> — сам AppId живёт у
    /// <c>SteamBootstrap</c> и остаётся единственным. Флаг нужен потому, что после релиза кнопка
    /// «в желаемое» теряет смысл: игру уже купили.</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Guildmaster/Config/Community Config", fileName = "CommunityConfig")]
    public sealed class CommunityConfig : ScriptableObject
    {
        /// <summary>Одна ссылка сообщества: значок, подпись и адрес.</summary>
        [System.Serializable]
        public struct LinkEntry
        {
            [Tooltip("Строковый id (link.discord) — для логов и тестов, игроку не виден.")]
            public string Id;

            [Tooltip("Ключ подписи. Подпись читает экранный диктор и показывает подсказка.")]
            public string LabelKey;

            [Tooltip("RU-литерал подписи, пока ключа нет в таблице.")]
            public string LabelFallback;

            [Tooltip("Адрес. ПУСТО — значок не показывается вовсе: канала ещё нет.")]
            public string Url;

            [Tooltip("Монохромный значок бренда (Simple Icons, CC0), SVG. Импортируется как VectorImage.")]
            public UnityEngine.UIElements.VectorImage Icon;
        }

        [Header("Обращение")]
        [SerializeField] private string _headlineKey = "ui.community.headline";
        [SerializeField] private string _headlineFallback = "Это демоверсия";

        [Tooltip("Абзацы обращения. Каждый — своя пара ключ/литерал: перевод абзацами, а не полотном.")]
        [SerializeField] private List<Paragraph> _paragraphs = new List<Paragraph>();

        [Header("Ссылки")]
        [SerializeField] private List<LinkEntry> _links = new List<LinkEntry>();

        [Header("Магазин")]
        [Tooltip("Показывать приглашение в список желаемого. После релиза — выключить.")]
        [SerializeField] private bool _showWishlist = true;

        /// <summary>Один абзац обращения.</summary>
        [System.Serializable]
        public struct Paragraph
        {
            public string Key;

            [TextArea(2, 5)]
            public string Fallback;
        }

        public string HeadlineKey      => _headlineKey;
        public string HeadlineFallback => _headlineFallback;

        /// <summary>Абзацы обращения в порядке показа.</summary>
        public IReadOnlyList<Paragraph> Paragraphs => _paragraphs;

        /// <summary>Ссылки в порядке показа. Те, у которых нет адреса, экран пропускает сам.</summary>
        public IReadOnlyList<LinkEntry> Links => _links;

        /// <summary>Показывать ли блок «Добавить в список желаемого».</summary>
        public bool ShowWishlist => _showWishlist;
    }
}
