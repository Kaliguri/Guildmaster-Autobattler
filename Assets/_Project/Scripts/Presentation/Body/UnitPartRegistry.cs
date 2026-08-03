using System.Collections.Generic;
using UnityEngine;

namespace Guildmaster.Presentation.Body
{
    /// <summary>
    /// Реестр частей тела, выведенный ИЗ КОНВЕНЦИИ РИГА: кто чем является, читается из иерархии
    /// (<see cref="RigNaming"/>), а не назначается вторым списком рядом с ней.
    /// </summary>
    /// <remarks>
    /// Ручная разметка «эта часть — оружие» была бы вторым владельцем: риг уже держит структурно всё, что
    /// нужно, — предмет в руке это кость-хват (<c>Weapon_R</c>), сторона это суффикс <c>_R</c>/<c>_L</c>
    /// в имени кости, часть тела это кость над узлом <c>…_Art</c>. Метка нужна ровно для того, что из
    /// геометрии не следует, — типа предмета (<see cref="UnitHeldItem"/>).
    /// <para>
    /// Поэтому реестр расстановко-независим сам собой: меч со щитом, два кинжала, двуручное копьё и пустые
    /// руки собираются одним кодом, и новая расстановка не требует ни строки здесь.
    /// </para>
    /// <para>
    /// <b>Индекс части — это индекс в списке частей ТЕЛА</b> (не в <see cref="Parts"/>): именно по нему тело
    /// проверяет маску при записи property block. Потерянные ссылки в списке тела записи не получают, но
    /// индексы живых от этого не сдвигаются.
    /// </para>
    /// </remarks>
    public sealed class UnitPartRegistry : IUnitPartLookup
    {
        private readonly UnitPart[] _parts;
        private readonly int        _slotCount;   // длина списка частей тела: по ней строится маска «всё тело»

        private UnitPartRegistry(UnitPart[] parts, int slotCount)
        {
            _parts     = parts;
            _slotCount = slotCount;
        }

        /// <summary>Пустое тело: рисовать нечего, запросы отвечают «нет».</summary>
        public static readonly UnitPartRegistry Empty = new UnitPartRegistry(System.Array.Empty<UnitPart>(), 0);

        public IReadOnlyList<UnitPart> Parts => _parts;

        public PartMask Everything => PartMask.All(_slotCount);

        /// <summary>
        /// Собрать реестр по частям скелетного тела. <paramref name="root"/> ограничивает поиск стороны —
        /// выше корня тела уже чужая иерархия; <paramref name="context"/> подставляется в ошибки, чтобы клик
        /// в консоли вёл на юнита.
        /// </summary>
        public static UnitPartRegistry FromBody(IReadOnlyList<SpriteRenderer> renderers, Transform root, Object context = null)
        {
            if (renderers == null || renderers.Count == 0) return Empty;

            int slots = renderers.Count;
            if (slots > PartMask.MaxParts)
            {
                // Части за 64-м битом адресовать нечем: они не засветятся и не будут найдены запросом, а
                // выглядеть это будет как «свечение иногда не работает». Поэтому громко и сразу.
                Debug.LogError($"[UnitPartRegistry] тело из {slots} частей — маска адресует только " +
                               $"{PartMask.MaxParts}. Части с индексом {PartMask.MaxParts} и дальше не " +
                               "адресуются: раздели юнита или сократи число частей.", context);
                slots = PartMask.MaxParts;
            }

            var parts = new List<UnitPart>(slots);
            for (int i = 0; i < slots; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer == null) continue;   // потерянная ссылка: о ней ругается само тело

                Transform bone = RigNaming.BoneOf(renderer.transform);
                if (bone == null) continue;

                bool     isHeld = RigNaming.IsGrip(bone);
                bool     isHand = HasGrip(bone);
                BodySide side   = RigNaming.SideOf(bone, root);

                HandSlot slot = HandSlot.None;
                HeldKind kind = HeldKind.None;
                if (isHeld) ResolveHeld(bone, side, context, out slot, out kind);

                parts.Add(new UnitPart(i, renderer, bone.name, side, slot, kind, isHand));
            }

            return new UnitPartRegistry(parts.ToArray(), slots);
        }

        /// <summary>
        /// Реестр тела из ОДНОГО спрайта — покадровый юнит. Частей нет, костей нет, хватов нет: единственная
        /// запись отвечает и на «дай часть», и на «чем ударишь», поэтому такое тело светится целиком.
        /// </summary>
        public static UnitPartRegistry ForSingleSprite(SpriteRenderer sprite)
        {
            if (sprite == null) return Empty;
            var part = new UnitPart(0, sprite, sprite.name, BodySide.None, HandSlot.None, HeldKind.None, isHand: false);
            return new UnitPartRegistry(new[] { part }, 1);
        }

        /// <summary>Кисть — кость, у которой в детях есть узел хвата: она способна держать предмет.</summary>
        private static bool HasGrip(Transform bone)
        {
            for (int i = 0; i < bone.childCount; i++)
                if (RigNaming.IsGrip(bone.GetChild(i))) return true;
            return false;
        }

        private static void ResolveHeld(Transform bone, BodySide side, Object context,
            out HandSlot slot, out HeldKind kind)
        {
            slot = HandSlot.None;
            kind = HeldKind.None;

            var mark = bone.GetComponent<UnitHeldItem>();
            if (mark == null)
            {
                Debug.LogError($"[UnitPartRegistry] предмет '{bone.name}' сидит в хвате, но не объявлен: " +
                               "повесь на его кость UnitHeldItem (Weapon/Shield). Без типа он не будет " +
                               "найден запросом «дай оружие» и не засветится на касте.", context);
                return;
            }

            kind = mark.Kind;
            if (mark.TwoHanded)
            {
                slot = HandSlot.Both;   // один предмет, две кисти: отвечает на любую руку
                return;
            }

            slot = side switch
            {
                BodySide.Left  => HandSlot.Left,
                BodySide.Right => HandSlot.Right,
                _              => HandSlot.None,
            };

            if (slot == HandSlot.None)
                Debug.LogError($"[UnitPartRegistry] предмет '{bone.name}' в хвате, но сторона руки не " +
                               "определена: конвенция ждёт суффикс '_L' или '_R' в имени кости-хвата " +
                               "или её предка. Запрос «что в правой руке» его не найдёт.", context);
        }

        public bool TryGetHeld(HandSlot slot, out UnitPart part)
        {
            for (int i = 0; i < _parts.Length; i++)
            {
                // Пересечение, а не равенство: двуручный предмет сидит в Both и обязан отвечать на Left и Right.
                if (_parts[i].IsHeld && (_parts[i].Slot & slot) != 0)
                {
                    part = _parts[i];
                    return true;
                }
            }
            part = default;
            return false;
        }

        public bool TryGetHeld(HeldKind kind, out UnitPart part)
        {
            for (int i = 0; i < _parts.Length; i++)
            {
                if (_parts[i].Kind == kind && kind != HeldKind.None)
                {
                    part = _parts[i];
                    return true;
                }
            }
            part = default;
            return false;
        }

        public bool TryGetBone(string boneName, BodySide side, out UnitPart part)
        {
            for (int i = 0; i < _parts.Length; i++)
            {
                if (_parts[i].Bone != boneName) continue;
                if (side != BodySide.None && _parts[i].Side != side) continue;
                part = _parts[i];
                return true;
            }
            part = default;
            return false;
        }

        public bool TryGetStrikeSource(HandSlot slot, out UnitPart part)
        {
            // Строго ЭТА рука: чем она занята, тем и бьёт — щит в ней означает щитовой приём, а не повод
            // взять меч из другой. Подстановка чужой руки светила бы не тем, что назвал навык.
            if (slot != HandSlot.None)
            {
                if (TryGetHeld(slot, out part)) return true;
                if (TryGetHand(SideOf(slot), out part)) return true;
            }
            else
            {
                if (TryGetHeld(HeldKind.Weapon, out part)) return true;
                if (TryGetHand(BodySide.None, out part)) return true;
            }

            // Тело из одного спрайта: у него нет ни костей, ни хватов, и источник удара у него он сам.
            if (_parts.Length == 1)
            {
                part = _parts[0];
                return true;
            }

            part = default;
            return false;
        }

        private bool TryGetHand(BodySide side, out UnitPart part)
        {
            for (int i = 0; i < _parts.Length; i++)
            {
                if (!_parts[i].IsHand) continue;
                if (side != BodySide.None && _parts[i].Side != side) continue;
                part = _parts[i];
                return true;
            }
            part = default;
            return false;
        }

        /// <summary>Сторона, которой соответствует хват. Двуручный держат обе — за ведущую берём правую.</summary>
        private static BodySide SideOf(HandSlot slot) =>
            (slot & HandSlot.Right) != 0 ? BodySide.Right :
            (slot & HandSlot.Left)  != 0 ? BodySide.Left  : BodySide.None;
    }
}
