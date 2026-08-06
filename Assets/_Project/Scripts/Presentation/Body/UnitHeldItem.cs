using UnityEngine;

namespace Guildmaster.Presentation.Body
{
    /// <summary>
    /// Объявление предмета в руке: чем он является в бою, держат ли его двумя руками и какая его часть
    /// достаёт до цели. Ставится на КОСТЬ-ХВАТ (<c>Weapon_R</c>), под которой лежит арт предмета
    /// (<c>Weapon_R_Sword_Art</c> и родня). Читается <see cref="UnitPartRegistry"/> при сборке тела и
    /// риг-профилем при замере.
    /// </summary>
    /// <remarks>
    /// Единственный владелец типа предмета. Всё остальное про предмет риг знает структурно — какая рука,
    /// какая кость, где точка хвата, — а вот «оружие это или щит» из геометрии не следует: посох и факел,
    /// баклер и книга формой не различаются. Поэтому тип авторится, а не угадывается.
    /// <para>
    /// Почему компонент, а не суффикс в имени узла (<c>Sword (Weapon)</c>), хотя весь остальной риг говорит
    /// именами: имя узла — часть путей клипов, масок и аватара, и правится только через <c>RigMigrate</c>.
    /// Платить миграцией рига за каждый новый предмет дорого, а метка вешается в инспекторе и анимации не
    /// касается.
    /// </para>
    /// <para>
    /// Метки нет — предмет в хвате остаётся БЕЗ типа, и об этом кричит ошибка при сборке тела: это наше
    /// авторство, поэтому громкий отказ, а не «наверное, меч».
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class UnitHeldItem : MonoBehaviour
    {
        [Tooltip("Чем предмет является в бою. Weapon — всё, чем наносят приём (меч, посох, лук, коготь). " +
                 "Shield — то, чем закрываются и бьют щитовыми приёмами.")]
        [SerializeField] private HeldKind _kind = HeldKind.Weapon;

        [Tooltip("Держат двумя руками (двуручное копьё, секира). Тогда предмет занимает ОБА хвата и " +
                 "отвечает на запрос по любой руке: он один, вторая кисть тянется к нему анимацией.")]
        [SerializeField] private bool _twoHanded;

        [Tooltip("Рабочая часть — тот кусок арта, чья длина и есть вылет предмета: КЛИНОК меча, полотно " +
                 "щита, наконечник с древком у копья. По ней меряется размах и по ней целятся аимы; " +
                 "рукоять и гарда в вылет не входят.")]
        [SerializeField] private SpriteRenderer _reachPart;

        [Header("Объявленный размер (не зависит от картинки)")]
        [Tooltip("ДЛИНА рабочей части в мировых единицах, от точки хвата до острия. Объявляется числом, а " +
                 "не берётся из спрайта: перерисовка арта не должна двигать ни дугу за клинком, ни знак " +
                 "удара. 0 = не объявлено, длина замеряется по мешу (переходный режим).")]
        [SerializeField] private float _declaredLength;

        [Tooltip("Направление рабочей части в градусах, в локальных координатах узла предмета: куда от " +
                 "хвата смотрит остриё. Замеряется той же кнопкой, что и длина.")]
        [SerializeField] private float _declaredAxisDeg = 90f;

        [Tooltip("Показывать гизмо объявленного вылета (жёлтая линия) рядом с замеренным по мешу (зелёная). " +
                 "Разошлись — значит арт перерисовали, а число забыли обновить.")]
        [SerializeField] private bool _showReachGizmo = true;

        /// <summary>Чем предмет является в бою.</summary>
        public HeldKind Kind => _kind;

        /// <summary>
        /// Объявленная длина рабочей части, мировые единицы; <c>0</c> = не объявлено.
        /// <para>
        /// Смысл этого поля — развязать ПОКАЗ и КАРТИНКУ. До 06.08.2026 вылет предмета выводился из меша
        /// спрайта: дальняя вершина от точки хвата. Это работало, но означало, что длина оружия равна
        /// размеру рисунка — перерисовал клинок длиннее, и вместе с ним поехали дуга за клинком, знак
        /// удара и офлайн-замеры, ничего об этом не сказав.
        /// </para>
        /// <para>
        /// Замер по мешу не отменён, он ПЕРЕЕХАЛ в редактор: кнопка меряет и записывает сюда, гизмо
        /// показывает объявленное рядом с фактическим, а гейт роняет тест при расхождении. Та же схема,
        /// по которой темп бега давно живёт числом на префабе, а не выводится из шага.
        /// </para>
        /// </summary>
        public float DeclaredLength => _declaredLength;

        /// <summary>Направление рабочей части от хвата, градусы в локальных координатах узла предмета.</summary>
        public float DeclaredAxisDeg => _declaredAxisDeg;

        /// <summary>Объявлен ли размер: <c>false</c> — работает переходный замер по мешу.</summary>
        public bool HasDeclaredReach => _declaredLength > 0f;

        /// <summary>
        /// Мировая точка острия ПО ОБЪЯВЛЕННОМУ размеру: от узла предмета на <see cref="DeclaredLength"/>
        /// вдоль объявленной оси. Масштаб узла учитывается — юнит другого размерного тира носит то же
        /// оружие пропорционально своей фигуре.
        /// </summary>
        public bool TryGetDeclaredTip(out Vector3 world)
        {
            world = default;
            if (!HasDeclaredReach) return false;

            Vector3 local = Quaternion.Euler(0f, 0f, _declaredAxisDeg) * (Vector3.right * _declaredLength);
            world = transform.TransformPoint(local);
            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Замерить рабочую часть по мешу и ЗАПИСАТЬ результат в объявленные поля. Замер честный — он
        /// единственный способ узнать, где кончается клинок, — но происходит здесь, в редакторе, а не в
        /// каждом кадре игры. Ровно так темп бега давно живёт числом на префабе.
        /// </summary>
        [ContextMenu("Замерить вылет по мешу")]
        private void MeasureReachFromMesh()
        {
            if (_reachPart == null || _reachPart.sprite == null)
            {
                Debug.LogError($"[UnitHeldItem] {name}: нечего мерить — не объявлена рабочая часть " +
                               "(ReachPart) или у неё нет спрайта.", this);
                return;
            }

            // Именно ЧИСТЫЙ замер: UnitPartGeometry.TryGetTip уже предпочитает объявленное значение, и
            // кнопка переписывала бы число им же самим — «замер» стал бы тождеством.
            if (!UnitPartGeometry.TryMeasureTipFromMesh(_reachPart, out Vector3 local))
            {
                Debug.LogError($"[UnitHeldItem] {name}: у рабочей части нет вершин меша — мерить нечего.", this);
                return;
            }
            Vector3 world = _reachPart.transform.TransformPoint(local);
            Vector3 fromGrip = transform.InverseTransformPoint(world);

            UnityEditor.Undo.RecordObject(this, "Замер вылета");
            _declaredLength  = fromGrip.magnitude;
            _declaredAxisDeg = Mathf.Atan2(fromGrip.y, fromGrip.x) * Mathf.Rad2Deg;
            UnityEditor.EditorUtility.SetDirty(this);

            Debug.Log($"[UnitHeldItem] {name}: вылет замерен — длина {_declaredLength:F4}, " +
                      $"ось {_declaredAxisDeg:F2}°. Теперь он объявлен и от картинки не зависит.", this);
        }

        /// <summary>
        /// Жёлтая линия — объявленный вылет, зелёная — фактический по мешу. Пока они совпадают, число
        /// честное; разошлись — арт перерисовали, а число забыли. Это и есть тот «класс с гизмо», по
        /// образцу зоны расстановки арены: геометрия ведёт, арт следует.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (!_showReachGizmo) return;

            if (TryGetDeclaredTip(out Vector3 declared))
            {
                Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.95f);
                Gizmos.DrawLine(transform.position, declared);
                Gizmos.DrawWireSphere(declared, 0.012f);
            }

            // Именно ЧИСТЫЙ замер: TryGetTip вернул бы объявленное значение, и две линии совпали бы
            // всегда — гизмо перестало бы ловить ровно то, ради чего заведено.
            if (UnitPartGeometry.TryMeasureTipFromMesh(_reachPart, out Vector3 localTip))
            {
                Vector3 measured = _reachPart.transform.TransformPoint(localTip);
                Gizmos.color = new Color(0.35f, 0.95f, 0.55f, 0.75f);
                Gizmos.DrawLine(transform.position, measured);
                Gizmos.DrawWireSphere(measured, 0.008f);
            }
        }
#endif

        /// <summary>Держат двумя руками: слот предмета становится <see cref="HandSlot.Both"/>.</summary>
        public bool TwoHanded => _twoHanded;

        /// <summary>
        /// Кусок арта, задающий вылет предмета. Угадывать его нельзя: «самый длинный» ошибётся на копье,
        /// где наконечник короче древка, а «первый попавшийся» — на любом предмете из нескольких кусков.
        /// У меча из клинка, гарды и рукояти замах рисует ОДИН клинок.
        /// </summary>
        public SpriteRenderer ReachPart => _reachPart;
    }
}
