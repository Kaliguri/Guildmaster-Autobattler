using System;
using UnityEngine;

namespace Guildmaster.Data.Definitions
{
    /// <summary>
    /// Палитра проекта в виде, доступном МИРУ. Значения живут в <c>UI/Theme/tokens.*.uss</c> — там же,
    /// где HARD-правило «тёплый свет», и там же их правит дизайнер вместе со стилями. Но USS умеет читать
    /// только UI Toolkit, а карта акта, боевые VFX и перекрасчик спрайтов рисуются мимо него.
    /// <para>Поэтому ассет — <b>снимок</b>, а не второй владелец: его пересобирает
    /// <c>Alebardium → Дизайн-система → Пересобрать палитру</c>, а тест сверяет с исходником и краснеет,
    /// если снимок устарел. Руками ассет не правят: правка уедет при первой пересборке.</para>
    /// </summary>
    public sealed class GuildmasterPalette : ScriptableObject
    {
        /// <summary>Одна запись палитры: имя токена как в USS (с ведущими дефисами) и его цвет.</summary>
        [Serializable]
        public struct Entry
        {
            public string Token;
            public Color  Color;
        }

        [Tooltip("Снимок токенов из UI/Theme/tokens.*.uss. Собирается меню, руками не правится.")]
        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        public Entry[] Entries => _entries;

        /// <summary>
        /// Цвет по имени токена (<c>--gm-color-map-node-rim</c>). Токена нет — это не «взять что-нибудь
        /// похожее», а разошедшиеся имена: возвращаем <c>false</c>, чтобы вызывающий сказал об этом вслух.
        /// </summary>
        public bool TryGet(string token, out Color color)
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Token != token) continue;
                color = _entries[i].Color;
                return true;
            }
            color = default;
            return false;
        }

        /// <summary>Заполнить снимок (зовёт только генератор из Editor-сборки).</summary>
        public void SetEntries(Entry[] entries) => _entries = entries ?? Array.Empty<Entry>();
    }
}
