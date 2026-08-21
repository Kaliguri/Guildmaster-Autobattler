using System;
using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Net.Tape
{
    /// <summary>Чем кончился разбор чанка.</summary>
    public enum TapeChunkStatus
    {
        /// <summary>Разобран и уложен в ленту.</summary>
        Ok = 0,

        /// <summary>Чанк с таким номером уже применялся — дубль после повтора или реконнекта.</summary>
        Duplicate,

        /// <summary>Формат чужой версии: читать как попало нельзя.</summary>
        VersionMismatch,

        /// <summary>Байты кончились раньше формата или строка длиннее остатка.</summary>
        Corrupted,

        /// <summary>В чанке id, которого нет в реестре контента — расхождение контента у игроков.</summary>
        UnknownContentId,
    }

    /// <summary>
    /// Разбирает чанки в свою <see cref="BattleTape"/>. Гость складывает их и играет ленту тем же
    /// <c>BattleTapePlayback</c>, которым играет соло: своими часами, с локальной интерполяцией,
    /// догоняя отставание сам.
    /// </summary>
    /// <remarks>
    /// <b>Идемпотентность по номеру чанка</b> — не роскошь: повтор потерянного чанка и реконнект дают
    /// один и тот же дубль, а применённый дважды чанк удвоил бы события (две цифры урона на один удар).
    /// <para><b>Неизвестный id — отказ, а не пропуск.</b> Показ без определения эффекта не знает, чем
    /// светить, и молчаливый пропуск дал бы гостю тихо другую картинку. Это тот класс поломки, который
    /// ловит handshake отпечатка контента, но чанк обязан защититься и сам.</para>
    /// </remarks>
    public sealed class TapeChunkReader
    {
        private readonly BattleTape       _tape;
        private readonly IContentDatabase _content;

        // Последний разобранный снимок каждого юнита В ЭТОМ чанке — база дельты, зеркало писателя.
        private readonly Dictionary<int, UnitSnapshot> _previous = new Dictionary<int, UnitSnapshot>(32);

        private readonly HashSet<int> _appliedChunks = new HashSet<int>();

        private readonly List<UnitSnapshot>       _units       = new List<UnitSnapshot>(32);
        private readonly List<ProjectileSnapshot> _projectiles = new List<ProjectileSnapshot>(16);

        // Способности резолвятся не через реестр: AbilityData — сериализуемый класс внутри UnitData, и
        // дома в контент-базе у неё нет. Подробности — в докстринге индекса.
        private readonly TapeAbilityIndex _abilities;

        public TapeChunkReader(BattleTape tape, IContentDatabase content)
        {
            _tape      = tape ?? throw new ArgumentNullException(nameof(tape));
            _content   = content;
            _abilities = new TapeAbilityIndex(content);
        }

        /// <summary>Сколько чанков применено. Дыры в нумерации видит отправитель по запросу повтора.</summary>
        public int AppliedChunkCount => _appliedChunks.Count;

        /// <summary>Последний применённый номер чанка, или <c>-1</c>.</summary>
        public int LastChunkNumber { get; private set; } = -1;

        /// <summary>Причина последнего отказа — для лога и для текста игроку. Пусто при <see cref="TapeChunkStatus.Ok"/>.</summary>
        public string LastError { get; private set; } = string.Empty;

        /// <summary>Разобрать чанк и уложить его кадры и события в ленту.</summary>
        public TapeChunkStatus Read(ArraySegment<byte> chunk)
        {
            LastError = string.Empty;

            try
            {
                return ReadInner(chunk);
            }
            catch (InvalidOperationException e)
            {
                LastError = e.Message;
                return TapeChunkStatus.Corrupted;
            }
        }

        /// <summary>Забыть применённые номера (новый бой): нумерация чанков начинается заново.</summary>
        public void Reset()
        {
            _appliedChunks.Clear();
            LastChunkNumber = -1;
            LastError       = string.Empty;
        }

        private TapeChunkStatus ReadInner(ArraySegment<byte> chunk)
        {
            var bytes = new NetByteReader(chunk);

            byte version = bytes.ReadByte();
            if (version != TapeChunkFormat.Version)
            {
                LastError = $"формат чанка версии {version}, а мы читаем {TapeChunkFormat.Version}";
                return TapeChunkStatus.VersionMismatch;
            }

            int chunkNumber = bytes.ReadInt();
            int firstTick   = bytes.ReadInt();
            int frameCount  = bytes.ReadByte();
            int eventCount  = bytes.ReadUShort();

            // Только ПРОВЕРКА: пометка «применён» ставится в самом конце, после успешного разбора.
            // Пометить здесь значило бы закрыть дыру, которую мы не закрыли: чанк, упавший на разборе,
            // числился бы применённым, гость продолжал бы просить его повтора каждые полсекунды, а
            // читатель отвечал бы «дубликат» — секунда боя терялась навсегда, и запросы шли до конца боя.
            if (_appliedChunks.Contains(chunkNumber)) return TapeChunkStatus.Duplicate;

            _previous.Clear();

            for (int i = 0; i < frameCount; i++)
            {
                int tickOffset = bytes.ReadByte();
                int unitCount  = bytes.ReadByte();

                _units.Clear();
                for (int u = 0; u < unitCount; u++) _units.Add(ReadUnit(bytes));

                int projectileCount = bytes.ReadByte();
                _projectiles.Clear();
                for (int p = 0; p < projectileCount; p++) _projectiles.Add(ReadProjectile(bytes));

                _tape.CaptureSnapshots(firstTick + tickOffset, _units, _projectiles);
            }

            for (int i = 0; i < eventCount; i++)
            {
                TapeChunkStatus status = ReadEvent(bytes, firstTick);
                if (status != TapeChunkStatus.Ok) return status;
            }

            _appliedChunks.Add(chunkNumber);   // разобрали целиком — только теперь он применён
            LastChunkNumber = chunkNumber;
            return TapeChunkStatus.Ok;
        }

        private UnitSnapshot ReadUnit(NetByteReader bytes)
        {
            int  id   = bytes.ReadShort();
            uint mask = bytes.ReadUInt();

            bool hadPrevious = _previous.TryGetValue(id, out UnitSnapshot prev);

            Vector2 position = (mask & TapeChunkFormat.UnitField.Position) != 0
                ? new Vector2(
                    TapeQuantization.UnpackPosition(bytes.ReadShort()),
                    TapeQuantization.UnpackPosition(bytes.ReadShort()))
                : prev.Position;

            // Команда едет только с первым появлением: за бой она не меняется.
            int team = hadPrevious ? prev.Team : bytes.ReadByte();

            float currentHp   = Read(bytes, mask, TapeChunkFormat.UnitField.CurrentHp,   prev.CurrentHP);
            float maxHp       = Read(bytes, mask, TapeChunkFormat.UnitField.MaxHp,       prev.MaxHP);
            float shield      = Read(bytes, mask, TapeChunkFormat.UnitField.Shield,      prev.CurrentShield);
            float resource    = Read(bytes, mask, TapeChunkFormat.UnitField.Resource,    prev.CurrentResource);
            float maxResource = Read(bytes, mask, TapeChunkFormat.UnitField.MaxResource, prev.MaxResource);

            float size = (mask & TapeChunkFormat.UnitField.Size) != 0
                ? TapeQuantization.UnpackScalar(bytes.ReadUShort(), TapeQuantization.SizeScale)
                : prev.Size;

            AttackPhase phase = (mask & TapeChunkFormat.UnitField.Phase) != 0
                ? (AttackPhase)bytes.ReadByte()
                : prev.Phase;

            int windupTicks     = ReadTicks(bytes, mask, TapeChunkFormat.UnitField.WindupTicks,     prev.WindupTicks);
            int windupRemaining = ReadTicks(bytes, mask, TapeChunkFormat.UnitField.WindupRemaining, prev.WindupRemaining);
            int cooldownTicks   = ReadTicks(bytes, mask, TapeChunkFormat.UnitField.AttackCooldown,  prev.AttackCooldownTicks);
            int recoveryTicks   = ReadTicks(bytes, mask, TapeChunkFormat.UnitField.RecoveryTicks,   prev.RecoveryTicks);
            int recoveryLeft    = ReadTicks(bytes, mask, TapeChunkFormat.UnitField.RecoveryLeft,    prev.RecoveryRemaining);
            int channelPeriod   = ReadTicks(bytes, mask, TapeChunkFormat.UnitField.ChannelPeriod,   prev.AttackChannelTickPeriod);
            int channelLeft     = ReadTicks(bytes, mask, TapeChunkFormat.UnitField.ChannelLeft,     prev.AttackChannelTickRemaining);

            int targetId = (mask & TapeChunkFormat.UnitField.TargetId) != 0 ? bytes.ReadShort() : prev.TargetId;

            EffectTag tags = (mask & TapeChunkFormat.UnitField.EffectTags) != 0
                ? (EffectTag)unchecked((int)bytes.ReadUInt())
                : prev.EffectTagMask;

            byte flags = (mask & TapeChunkFormat.UnitField.Flags) != 0
                ? bytes.ReadByte()
                : TapeChunkWriter.PackFlags(in prev);

            float attackRange = (mask & TapeChunkFormat.UnitField.AttackRange) != 0
                ? TapeQuantization.UnpackScalar(bytes.ReadUShort(), TapeQuantization.SizeScale)
                : prev.AttackRange;

            float sprintRamp = (mask & TapeChunkFormat.UnitField.SprintRamp) != 0
                ? TapeQuantization.UnpackUnit(bytes.ReadByte())
                : prev.SprintRamp;

            // PreviousPosition по сети НЕ едет — это позиция предыдущего тика, и она у нас уже есть.
            // Для юнита, впервые появившегося в чанке, прошлой позиции нет: берём текущую, то есть
            // «стоял на месте». Единственная альтернатива — гонять её по сети, платя вдвое за то, что
            // приёмник знает и сам.
            Vector2 previousPosition = hadPrevious ? prev.Position : position;

            var snapshot = new UnitSnapshot(
                id, team, position, previousPosition,
                currentHp, maxHp, shield, resource, maxResource,
                size, phase, windupTicks, windupRemaining, cooldownTicks,
                targetId, tags,
                isDead:       (flags & TapeChunkFormat.UnitFlag.IsDead) != 0,
                attackRange:  attackRange,
                canAct:       (flags & TapeChunkFormat.UnitFlag.CanAct) != 0,
                isDisplaced:  (flags & TapeChunkFormat.UnitFlag.IsDisplaced) != 0,
                isEmpowered:  (flags & TapeChunkFormat.UnitFlag.IsEmpowered) != 0,
                sprintRamp:   sprintRamp,
                chargedSwing: (flags & TapeChunkFormat.UnitFlag.ChargedSwing) != 0,
                recoveryTicks: recoveryTicks,
                recoveryRemaining: recoveryLeft,
                attackChannelTickPeriod: channelPeriod,
                attackChannelTickRemaining: channelLeft,
                isSelfDisplaced: (flags & TapeChunkFormat.UnitFlag.SelfDisplaced) != 0);

            _previous[id] = snapshot;
            return snapshot;
        }

        private static ProjectileSnapshot ReadProjectile(NetByteReader bytes)
        {
            int id       = bytes.ReadShort();
            int sourceId = bytes.ReadShort();
            int targetId = bytes.ReadShort();

            var position = new Vector2(
                TapeQuantization.UnpackPosition(bytes.ReadShort()),
                TapeQuantization.UnpackPosition(bytes.ReadShort()));
            var previous = new Vector2(
                TapeQuantization.UnpackPosition(bytes.ReadShort()),
                TapeQuantization.UnpackPosition(bytes.ReadShort()));
            var velocity = new Vector2(
                TapeQuantization.UnpackPosition(bytes.ReadShort()),
                TapeQuantization.UnpackPosition(bytes.ReadShort()));

            bool isHeal = bytes.ReadBool();

            return new ProjectileSnapshot(id, sourceId, position, previous, velocity, targetId, isHeal);
        }

        private TapeChunkStatus ReadEvent(NetByteReader bytes, int firstTick)
        {
            var  kind       = (TapeEventKind)bytes.ReadByte();
            int  tick       = firstTick + bytes.ReadByte();
            float subTick   = TapeQuantization.UnpackUnit(bytes.ReadByte());
            int  sourceId   = bytes.ReadShort();
            int  targetId   = bytes.ReadShort();
            float amount    = bytes.ReadFloat();
            int  flags      = bytes.ReadInt();

            switch (kind)
            {
                case TapeEventKind.DamageDealt:
                {
                    float hp          = bytes.ReadFloat();
                    float shield      = bytes.ReadFloat();
                    float mitigated   = bytes.ReadFloat();
                    float vulnerability = bytes.ReadFloat();
                    bool  killed      = bytes.ReadBool();
                    var   sourceKind  = (DamageSourceKind)bytes.ReadByte();
                    var   damageType  = (DamageType)bytes.ReadByte();

                    var result = new DamageResult(hp, shield, killed, sourceKind, damageType,
                        vulnerability, mitigated);
                    _tape.RecordDamage(tick, sourceId, targetId, in result, subTick);
                    return TapeChunkStatus.Ok;
                }

                case TapeEventKind.AreaHit:
                {
                    var shape  = (AreaShape)bytes.ReadByte();
                    var origin = new Vector2(bytes.ReadFloat(), bytes.ReadFloat());
                    var dir    = new Vector2(bytes.ReadFloat(), bytes.ReadFloat());
                    float length = bytes.ReadFloat();
                    float width  = bytes.ReadFloat();
                    float radius = bytes.ReadFloat();
                    int   team   = bytes.ReadInt();

                    AreaHit hit = shape == AreaShape.Line
                        ? AreaHit.Line(origin, dir, length, width, team)
                        : AreaHit.Circle(origin, radius, team);

                    _tape.RecordAreaHit(tick, in hit, subTick);
                    return TapeChunkStatus.Ok;
                }

                case TapeEventKind.BattleEnded:
                {
                    var outcomeKind = (BattleOutcomeKind)bytes.ReadByte();
                    int winningTeam = bytes.ReadInt();

                    BattleOutcome outcome = outcomeKind switch
                    {
                        BattleOutcomeKind.TeamWin => BattleOutcome.Win(winningTeam),
                        BattleOutcomeKind.Draw    => BattleOutcome.Draw,
                        _                         => BattleOutcome.Ongoing,
                    };

                    _tape.RecordBattleEnded(tick, in outcome);
                    return TapeChunkStatus.Ok;
                }

                case TapeEventKind.EffectApplied:
                case TapeEventKind.EffectEnded:
                {
                    string id = bytes.ReadString();
                    if (!TryResolve<EffectData>(id, out EffectData def)) return TapeChunkStatus.UnknownContentId;

                    _tape.RecordEffect(tick, kind, targetId, def);
                    return TapeChunkStatus.Ok;
                }

                case TapeEventKind.AbilityCast:
                case TapeEventKind.AbilityCastStarted:
                {
                    string id = bytes.ReadString();

                    AbilityData def = null;
                    if (!string.IsNullOrEmpty(id) && !_abilities.TryGet(id, out def))
                    {
                        LastError = $"в чанке способность '{id}', которой нет ни у одного юнита в контенте";
                        return TapeChunkStatus.UnknownContentId;
                    }

                    _tape.RecordAbility(tick, kind, sourceId, def, amount);
                    return TapeChunkStatus.Ok;
                }

                default:
                    _tape.Record(new TapeEvent(kind, tick, sourceId, targetId, amount, flags, subTick: subTick));
                    return TapeChunkStatus.Ok;
            }
        }

        // Пустой id — законное «определения не было» (так пишется каст без данных). Непустой, но
        // неизвестный — расхождение контента, и это отказ: показ без определения не знает, чем светить.
        private bool TryResolve<T>(string id, out T def) where T : ContentDefinition
        {
            def = null;
            if (string.IsNullOrEmpty(id)) return true;

            if (_content != null && _content.TryGet(id, out def)) return true;

            LastError = $"в чанке id '{id}' типа {typeof(T).Name}, которого нет в реестре контента";
            return false;
        }

        private static float Read(NetByteReader bytes, uint mask, uint field, float fallback) =>
            (mask & field) != 0 ? TapeQuantization.UnpackScalar(bytes.ReadUShort()) : fallback;

        private static int ReadTicks(NetByteReader bytes, uint mask, uint field, int fallback) =>
            (mask & field) != 0 ? bytes.ReadUShort() : fallback;
    }
}
