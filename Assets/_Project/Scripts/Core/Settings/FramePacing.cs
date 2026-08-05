namespace Guildmaster.Core.Settings
{
    /// <summary>
    /// Во что превращается пара «синхронизация + потолок кадров», выбранная игроком. Правило вынесено из
    /// <c>DisplayService</c> отдельным типом, потому что оно чистое и обязано проверяться без редактора.
    /// </summary>
    /// <remarks>
    /// <b>Синхронизация главнее потолка, и это не наш выбор, а поведение Unity.</b> При
    /// <c>vSyncCount &gt; 0</c> движок игнорирует <c>Application.targetFrameRate</c> целиком: кадры пасует
    /// развёртка монитора. Поэтому потолок здесь ОБНУЛЯЕТСЯ при включённой синхронизации — иначе в файле и
    /// в UI жило бы число, на которое игра не смотрит, и «поставил 60, а идёт 165» читалось бы как баг.
    /// <para><b>Синхронизация включена по умолчанию.</b> До 2026-08-06 у игры не было ни того, ни другого:
    /// <c>targetFrameRate</c> не задавался нигде, а <c>vSyncCount</c> приходил из уровня качества, и на
    /// активном уровне (<c>Very Low</c>) он равен нулю. Игра рисовала столько кадров, сколько потянет
    /// железо, ради кадров, которых никто не видит — нагрев, шум кулера, севшая батарея на ноутбуке.</para>
    /// <para><b>Почему синхронизация, а не только потолок.</b> Документация Unity рекомендует
    /// <c>vSyncCount</c> как аппаратный механизм; <c>targetFrameRate</c> — программный таймер, дающий
    /// микростаттер. Потолок остаётся опцией для тех, кому нужно НИЖЕ частоты монитора (тихий кулер,
    /// батарея, стрим), и тогда синхронизацию приходится выключать.</para>
    /// </remarks>
    public readonly struct FramePacing
    {
        /// <summary>Потолок «без потолка»: столько кадров, сколько получится.</summary>
        public const int Unlimited = 0;

        /// <summary>Ниже этого потолок не опускаем: играть в слайд-шоу игрок не просил.</summary>
        public const int MinCap = 20;

        private FramePacing(bool vSync, int frameRateCap)
        {
            VSync = vSync;
            FrameRateCap = frameRateCap;
        }

        /// <summary>Синхронизировать ли кадры с развёрткой монитора.</summary>
        public bool VSync { get; }

        /// <summary>
        /// Потолок кадров в секунду, <see cref="Unlimited"/> — без потолка. При включённой
        /// <see cref="VSync"/> всегда <see cref="Unlimited"/>: движок всё равно не будет его слушать.
        /// </summary>
        public int FrameRateCap { get; }

        /// <summary>Осмысленно ли сейчас показывать выбор потолка в UI (или его надо гасить).</summary>
        public bool FrameRateCapSelectable => !VSync;

        /// <summary>
        /// Свести настройки игрока к тому, что применяется. Оба поля nullable, потому что «не задано»
        /// обязано отличаться от «выбрано и равно false/0» — ровно как в <see cref="DisplaySettings"/>.
        /// </summary>
        public static FramePacing Resolve(bool? vSync, int? frameRateCap)
        {
            bool sync = vSync ?? true;
            if (sync) return new FramePacing(true, Unlimited);

            int cap = frameRateCap ?? Unlimited;
            if (cap != Unlimited && cap < MinCap) cap = MinCap;
            return new FramePacing(false, cap);
        }
    }
}
