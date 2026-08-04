using System;
using System.Collections.Generic;
using Guildmaster.Combat;
using Guildmaster.Combat.Tape;
using Guildmaster.Data.Definitions;
using UnityEngine;

namespace Guildmaster.Net.Tape
{
    /// <summary>
    /// Кодек чанка боевой ленты: хост укладывает срез ленты в байты, гость разбирает их в свою
    /// <see cref="BattleTape"/> и играет её тем же <c>BattleTapePlayback</c>, которым играет соло.
    /// <para><b>Чанк самодостаточен.</b> Дельта считается только внутри него, первое появление юнита
    /// пишется целиком. Непрерывная дельта через границы чанков дала бы на проценты меньше байт, но
    /// потеря одного чанка ломала бы все следующие — а так теряется ровно то, что потерялось.</para>
    /// <para><b>Что квантуется и что нет.</b> Снимки — то, из чего рисуется кадр, — едут короткими
    /// целыми (позиция 1/256 мировой единицы, шкалы в <c>ushort</c>). Числа СОБЫТИЙ едут полным
    /// <c>float</c>: их пара сотен на бой, они попадают в цифры урона на экране и в аудит, и экономить
    /// на них нечего.</para>
    /// <para><b>Определения едут строковыми id.</b> Лента держит ссылки на ассеты
    /// (<see cref="EffectData"/>, <see cref="AbilityData"/>), а <c>UnityEngine.Object</c> по сети не
    /// передать. Поэтому у читателя обязательно есть реестр контента, а неизвестный id — громкий отказ:
    /// handshake отпечатка ловит расхождение раньше, но чанк умеет защититься сам.</para>
    /// </summary>
    public sealed class TapeChunkWriter
    {
        private readonly NetByteWriter _bytes = new NetByteWriter(8192);

        // Последний записанный снимок каждого юнита В ЭТОМ чанке — база для дельты. Очищается на каждом
        // чанке: самодостаточность важнее пары процентов трафика.
        private readonly Dictionary<int, UnitSnapshot> _previous = new Dictionary<int, UnitSnapshot>(32);

        private readonly List<TapeEvent> _events = new List<TapeEvent>(64);

        private int _chunkNumber;

        /// <summary>Номер следующего чанка. Нумерация непрерывна — по ней приёмник видит дыру.</summary>
        public int NextChunkNumber => _chunkNumber;

        /// <summary>
        /// Уложить срез ленты: тики <paramref name="firstTick"/>..<paramref name="firstTick"/>+
        /// <paramref name="tickCount"/>-1 и все события в этом диапазоне.
        /// </summary>
        /// <param name="maxBytes">
        /// Сколько байт можно занять. Предел приходит СНАРУЖИ, потому что знает его транспорт: у Steam
        /// это 512 КБ, у UTP — заметно меньше. Писателю остаётся уважать чужое число, а не держать своё.
        /// </param>
        /// <param name="bytes">
        /// Готовые байты (живут до следующего вызова) или ПУСТОЙ сегмент, если в диапазоне не нашлось ни
        /// одного записанного кадра — раздавать нечего, и это не отказ.
        /// </param>
        /// <returns>
        /// <c>false</c> — и только это — означает «не влезло в <paramref name="maxBytes"/>»: ожидаемый
        /// исход, на который у вызывающего есть ответ (поделить диапазон и позвать снова). Всё остальное
        /// (нет ленты, недопустимое число тиков) — ошибка вызова и летит исключением.
        /// </returns>
        /// <remarks>
        /// Так требует конвенция .NET для <c>TryFormat</c>/<c>TryWrite</c>: <c>false</c> возвращают
        /// исключительно при нехватке места, прочие сбои бросают. Раньше здесь стоял свой потолок в
        /// 64 КБ и <c>throw</c> на нём — то есть управление потоком через исключение. Срабатывало оно
        /// РАНЬШЕ предела транспорта, поэтому задуманное деление чанка было недостижимо ни при каком
        /// входе, а исключение уходило наверх с уже потраченным номером чанка: раздача вставала
        /// навсегда, у гостя оставалась дыра в нумерации, которую он просил повторить до конца боя.
        /// <para>Номер чанка тратится ТОЛЬКО на успешной записи. Поэтому отката (<c>DiscardLast</c>)
        /// больше нет — откатывать нечего.</para>
        /// </remarks>
        /// <param name="includeEvents">
        /// Класть ли в чанк события диапазона. <c>false</c> — только снимки: так уезжает КАДР ПОКОЯ,
        /// который переотправляется десять раз в секунду, пока арена стоит. С событиями внутри каждая
        /// такая посылка проигрывалась бы у гостя заново — удар, смерть, звук, — и звучало это как
        /// зациклившийся эффект (наход. Макса 04.08.2026). События едут ровно один раз, в том чанке,
        /// который двигает бой вперёд.
        /// </param>
        public bool TryWrite(BattleTape tape, int firstTick, int tickCount, int maxBytes,
                             out ArraySegment<byte> bytes, bool includeEvents = true)
        {
            if (tape == null) throw new ArgumentNullException(nameof(tape));
            if (tickCount <= 0 || tickCount > 255)
                throw new ArgumentOutOfRangeException(nameof(tickCount),
                    "смещение тика внутри чанка едет одним байтом: 1..255");
            if (maxBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytes), "предел размера чанка должен быть положительным");

            bytes = default;
            _bytes.Reset();
            _previous.Clear();

            // Сначала собираем кадры в память: их число нужно в заголовке, а пропуски (тик вне окна)
            // сдвинули бы нумерацию, если писать «как получится».
            var frames = new List<int>(tickCount);
            for (int i = 0; i < tickCount; i++)
            {
                int tick = firstTick + i;
                if (tape.TryGetFrame(tick, out _, out _)) frames.Add(tick);
            }

            if (frames.Count == 0)
            {
                bytes = new ArraySegment<byte>(Array.Empty<byte>());
                return true;
            }

            if (includeEvents) tape.CollectEvents(firstTick, firstTick + tickCount - 1, _events);
            else               _events.Clear();

            // Номер пишем, но НЕ тратим: инкремент ниже, за проверкой размера.
            _bytes.WriteByte(TapeChunkFormat.Version);
            _bytes.WriteInt(_chunkNumber);
            _bytes.WriteInt(firstTick);
            _bytes.WriteByte((byte)frames.Count);
            _bytes.WriteUShort((ushort)_events.Count);

            for (int i = 0; i < frames.Count; i++) WriteFrame(tape, frames[i], firstTick);
            for (int i = 0; i < _events.Count; i++) WriteEvent(tape, _events[i], firstTick);

            if (_bytes.Length > maxBytes) return false;   // номер не потрачен — этот же уедет меньшим куском

            _chunkNumber++;
            bytes = _bytes.WrittenSegment;
            return true;
        }

        private void WriteFrame(BattleTape tape, int tick, int firstTick)
        {
            tape.TryGetFrame(tick, out IReadOnlyList<UnitSnapshot> units,
                out IReadOnlyList<ProjectileSnapshot> projectiles);

            _bytes.WriteByte((byte)(tick - firstTick));
            _bytes.WriteByte((byte)Mathf.Min(units.Count, 255));

            for (int i = 0; i < units.Count && i < 255; i++) WriteUnit(units[i]);

            int projectileCount = projectiles != null ? Mathf.Min(projectiles.Count, 255) : 0;
            _bytes.WriteByte((byte)projectileCount);
            for (int i = 0; i < projectileCount; i++) WriteProjectile(projectiles[i]);
        }

        private void WriteUnit(in UnitSnapshot unit)
        {
            bool hadPrevious = _previous.TryGetValue(unit.Id, out UnitSnapshot prev);
            uint mask = hadPrevious ? DiffMask(in prev, in unit) : TapeChunkFormat.UnitField.All;

            _bytes.WriteShort((short)unit.Id);
            _bytes.WriteUInt(mask);

            if ((mask & TapeChunkFormat.UnitField.Position) != 0)
            {
                _bytes.WriteShort(TapeQuantization.PackPosition(unit.Position.x));
                _bytes.WriteShort(TapeQuantization.PackPosition(unit.Position.y));
            }

            // Команда едет один раз — вместе с первым появлением юнита: она не меняется за бой, и место
            // в маске ей не нужно.
            if (!hadPrevious) _bytes.WriteByte((byte)unit.Team);

            if ((mask & TapeChunkFormat.UnitField.CurrentHp)   != 0) _bytes.WriteUShort(TapeQuantization.PackScalar(unit.CurrentHP));
            if ((mask & TapeChunkFormat.UnitField.MaxHp)       != 0) _bytes.WriteUShort(TapeQuantization.PackScalar(unit.MaxHP));
            if ((mask & TapeChunkFormat.UnitField.Shield)      != 0) _bytes.WriteUShort(TapeQuantization.PackScalar(unit.CurrentShield));
            if ((mask & TapeChunkFormat.UnitField.Resource)    != 0) _bytes.WriteUShort(TapeQuantization.PackScalar(unit.CurrentResource));
            if ((mask & TapeChunkFormat.UnitField.MaxResource) != 0) _bytes.WriteUShort(TapeQuantization.PackScalar(unit.MaxResource));
            if ((mask & TapeChunkFormat.UnitField.Size)        != 0) _bytes.WriteUShort(TapeQuantization.PackScalar(unit.Size, TapeQuantization.SizeScale));
            if ((mask & TapeChunkFormat.UnitField.Phase)       != 0) _bytes.WriteByte((byte)unit.Phase);

            if ((mask & TapeChunkFormat.UnitField.WindupTicks)     != 0) _bytes.WriteUShort(TapeQuantization.PackTicks(unit.WindupTicks));
            if ((mask & TapeChunkFormat.UnitField.WindupRemaining) != 0) _bytes.WriteUShort(TapeQuantization.PackTicks(unit.WindupRemaining));
            if ((mask & TapeChunkFormat.UnitField.AttackCooldown)  != 0) _bytes.WriteUShort(TapeQuantization.PackTicks(unit.AttackCooldownTicks));
            if ((mask & TapeChunkFormat.UnitField.RecoveryTicks)   != 0) _bytes.WriteUShort(TapeQuantization.PackTicks(unit.RecoveryTicks));
            if ((mask & TapeChunkFormat.UnitField.RecoveryLeft)    != 0) _bytes.WriteUShort(TapeQuantization.PackTicks(unit.RecoveryRemaining));
            if ((mask & TapeChunkFormat.UnitField.ChannelPeriod)   != 0) _bytes.WriteUShort(TapeQuantization.PackTicks(unit.AttackChannelTickPeriod));
            if ((mask & TapeChunkFormat.UnitField.ChannelLeft)     != 0) _bytes.WriteUShort(TapeQuantization.PackTicks(unit.AttackChannelTickRemaining));

            if ((mask & TapeChunkFormat.UnitField.TargetId)    != 0) _bytes.WriteShort((short)unit.TargetId);
            if ((mask & TapeChunkFormat.UnitField.EffectTags)  != 0) _bytes.WriteUInt(unchecked((uint)unit.EffectTagMask));
            if ((mask & TapeChunkFormat.UnitField.Flags)       != 0) _bytes.WriteByte(PackFlags(in unit));
            if ((mask & TapeChunkFormat.UnitField.AttackRange) != 0) _bytes.WriteUShort(TapeQuantization.PackScalar(unit.AttackRange, TapeQuantization.SizeScale));
            if ((mask & TapeChunkFormat.UnitField.SprintRamp)  != 0) _bytes.WriteByte(TapeQuantization.PackUnit(unit.SprintRamp));

            _previous[unit.Id] = unit;
        }

        private void WriteProjectile(in ProjectileSnapshot p)
        {
            // Снаряды идут целиком, без дельты: их единицы, живут они меньше секунды, и словарь базы под
            // них стоил бы дороже сэкономленных байт.
            _bytes.WriteShort((short)p.Id);
            _bytes.WriteShort((short)p.SourceId);
            _bytes.WriteShort((short)p.TargetId);
            _bytes.WriteShort(TapeQuantization.PackPosition(p.Position.x));
            _bytes.WriteShort(TapeQuantization.PackPosition(p.Position.y));
            _bytes.WriteShort(TapeQuantization.PackPosition(p.PreviousPosition.x));
            _bytes.WriteShort(TapeQuantization.PackPosition(p.PreviousPosition.y));
            _bytes.WriteShort(TapeQuantization.PackPosition(p.Velocity.x));
            _bytes.WriteShort(TapeQuantization.PackPosition(p.Velocity.y));
            _bytes.WriteBool(p.IsHeal);
        }

        private void WriteEvent(BattleTape tape, in TapeEvent ev, int firstTick)
        {
            _bytes.WriteByte((byte)ev.Kind);
            _bytes.WriteByte((byte)(ev.Tick - firstTick));
            _bytes.WriteByte(TapeQuantization.PackUnit(ev.SubTick));
            _bytes.WriteShort((short)ev.SourceId);
            _bytes.WriteShort((short)ev.TargetId);
            _bytes.WriteFloat(ev.Amount);
            _bytes.WriteInt(ev.Flags);

            switch (ev.Kind)
            {
                case TapeEventKind.DamageDealt:
                {
                    DamageResult r = tape.GetDamage(ev.PayloadIndex);
                    _bytes.WriteFloat(r.HpDamage);
                    _bytes.WriteFloat(r.ShieldDamage);
                    _bytes.WriteFloat(r.Mitigated);
                    _bytes.WriteFloat(r.Vulnerability);
                    _bytes.WriteBool(r.KilledTarget);
                    _bytes.WriteByte((byte)r.SourceKind);
                    _bytes.WriteByte((byte)r.Type);
                    break;
                }

                case TapeEventKind.AreaHit:
                {
                    AreaHit hit = tape.GetAreaHit(ev.PayloadIndex);
                    _bytes.WriteByte((byte)hit.Shape);
                    _bytes.WriteFloat(hit.Origin.x);
                    _bytes.WriteFloat(hit.Origin.y);
                    _bytes.WriteFloat(hit.Direction.x);
                    _bytes.WriteFloat(hit.Direction.y);
                    _bytes.WriteFloat(hit.Length);
                    _bytes.WriteFloat(hit.Width);
                    _bytes.WriteFloat(hit.Radius);
                    _bytes.WriteInt(hit.Team);
                    break;
                }

                case TapeEventKind.BattleEnded:
                {
                    BattleOutcome outcome = tape.GetOutcome(ev.PayloadIndex);
                    _bytes.WriteByte((byte)outcome.Kind);
                    _bytes.WriteInt(outcome.WinningTeam);
                    break;
                }

                case TapeEventKind.EffectApplied:
                case TapeEventKind.EffectEnded:
                {
                    EffectData def = tape.GetEffect(ev.PayloadIndex);
                    _bytes.WriteString(def != null ? def.Id : null);
                    break;
                }

                case TapeEventKind.AbilityCast:
                case TapeEventKind.AbilityCastStarted:
                {
                    AbilityData def = tape.GetAbility(ev.PayloadIndex);
                    _bytes.WriteString(def != null ? def.Id : null);
                    break;
                }
            }
        }

        private static uint DiffMask(in UnitSnapshot a, in UnitSnapshot b)
        {
            uint mask = 0;

            // Позиция сравнивается В КВАНТОВАННОМ виде: иначе дрожание в тысячных долях, которое всё
            // равно не переживёт упаковку, гнало бы дельту каждый тик.
            if (TapeQuantization.PackPosition(a.Position.x) != TapeQuantization.PackPosition(b.Position.x)
             || TapeQuantization.PackPosition(a.Position.y) != TapeQuantization.PackPosition(b.Position.y))
                mask |= TapeChunkFormat.UnitField.Position;

            if (TapeQuantization.PackScalar(a.CurrentHP)       != TapeQuantization.PackScalar(b.CurrentHP))       mask |= TapeChunkFormat.UnitField.CurrentHp;
            if (TapeQuantization.PackScalar(a.MaxHP)           != TapeQuantization.PackScalar(b.MaxHP))           mask |= TapeChunkFormat.UnitField.MaxHp;
            if (TapeQuantization.PackScalar(a.CurrentShield)   != TapeQuantization.PackScalar(b.CurrentShield))   mask |= TapeChunkFormat.UnitField.Shield;
            if (TapeQuantization.PackScalar(a.CurrentResource) != TapeQuantization.PackScalar(b.CurrentResource)) mask |= TapeChunkFormat.UnitField.Resource;
            if (TapeQuantization.PackScalar(a.MaxResource)     != TapeQuantization.PackScalar(b.MaxResource))     mask |= TapeChunkFormat.UnitField.MaxResource;

            if (TapeQuantization.PackScalar(a.Size, TapeQuantization.SizeScale)
             != TapeQuantization.PackScalar(b.Size, TapeQuantization.SizeScale)) mask |= TapeChunkFormat.UnitField.Size;

            if (a.Phase != b.Phase) mask |= TapeChunkFormat.UnitField.Phase;

            if (a.WindupTicks                 != b.WindupTicks)                 mask |= TapeChunkFormat.UnitField.WindupTicks;
            if (a.WindupRemaining             != b.WindupRemaining)             mask |= TapeChunkFormat.UnitField.WindupRemaining;
            if (a.AttackCooldownTicks         != b.AttackCooldownTicks)         mask |= TapeChunkFormat.UnitField.AttackCooldown;
            if (a.RecoveryTicks               != b.RecoveryTicks)               mask |= TapeChunkFormat.UnitField.RecoveryTicks;
            if (a.RecoveryRemaining           != b.RecoveryRemaining)           mask |= TapeChunkFormat.UnitField.RecoveryLeft;
            if (a.AttackChannelTickPeriod     != b.AttackChannelTickPeriod)     mask |= TapeChunkFormat.UnitField.ChannelPeriod;
            if (a.AttackChannelTickRemaining  != b.AttackChannelTickRemaining)  mask |= TapeChunkFormat.UnitField.ChannelLeft;

            if (a.TargetId      != b.TargetId)      mask |= TapeChunkFormat.UnitField.TargetId;
            if (a.EffectTagMask != b.EffectTagMask) mask |= TapeChunkFormat.UnitField.EffectTags;
            if (PackFlags(in a) != PackFlags(in b)) mask |= TapeChunkFormat.UnitField.Flags;

            if (TapeQuantization.PackScalar(a.AttackRange, TapeQuantization.SizeScale)
             != TapeQuantization.PackScalar(b.AttackRange, TapeQuantization.SizeScale)) mask |= TapeChunkFormat.UnitField.AttackRange;

            if (TapeQuantization.PackUnit(a.SprintRamp) != TapeQuantization.PackUnit(b.SprintRamp))
                mask |= TapeChunkFormat.UnitField.SprintRamp;

            return mask;
        }

        internal static byte PackFlags(in UnitSnapshot unit)
        {
            byte flags = 0;
            if (unit.IsDead)       flags |= TapeChunkFormat.UnitFlag.IsDead;
            if (unit.CanAct)       flags |= TapeChunkFormat.UnitFlag.CanAct;
            if (unit.IsDisplaced)  flags |= TapeChunkFormat.UnitFlag.IsDisplaced;
            if (unit.IsEmpowered)  flags |= TapeChunkFormat.UnitFlag.IsEmpowered;
            if (unit.ChargedSwing) flags |= TapeChunkFormat.UnitFlag.ChargedSwing;
            return flags;
        }
    }
}
