using Guildmaster.Data.Definitions;
using Guildmaster.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Эффект, выключенный ЧУЖОЙ рукой, обязан вернуться в пул.
    /// </summary>
    /// <remarks>
    /// <b>Инвариант живёт между пулом и чужой иерархией, поэтому он в тесте.</b> Страховка по сроку
    /// считается в <c>Update</c> самого эффекта, а выключенный объект не тикает — вместе с тиком
    /// замирает и она. Эффект остаётся «играющим» навсегда: пул его не получил, показывать некому, а
    /// при следующем включении он оживает с прежним состоянием.
    /// <para>Ловится это на дуге за клинком: она переезжает ВНУТРЬ тела бьющего, а вид юнита уходит в
    /// свой пул вместе со смертью. Игрок видит застрявший росчерк, и у каждого свой — смерти на
    /// машинах разные (наход. Макса 08.08.2026, прогон вдвоём).</para>
    /// <para>Комментарием такое не удержать: выключает эффект не пул, а чужой родитель, и знать про
    /// этот договор он не обязан.</para>
    /// </remarks>
    public sealed class PooledVfxReturnTests
    {
        private GameObject _host;
        private GameObject _body;
        private GameObject _prefab;
        private VfxData    _data;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("vfx-prefab");
            _prefab.AddComponent<PooledVfx>();

            _data = ScriptableObject.CreateInstance<VfxData>();
            var so = new SerializedObject(_data);
            so.FindProperty("_prefab").objectReferenceValue = _prefab;
            SerializedProperty size = so.FindProperty("_sizeUnits");
            if (size != null) size.floatValue = 1f;
            so.ApplyModifiedPropertiesWithoutUndo();

            _host = new GameObject("combat-vfx");
            _body = new GameObject("unit-body");
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)   Object.DestroyImmediate(_host);
            if (_body != null)   Object.DestroyImmediate(_body);
            if (_prefab != null) Object.DestroyImmediate(_prefab);
            if (_data != null)   Object.DestroyImmediate(_data);
        }

        [Test]
        public void EffectDisabledByItsNewParent_GoesBackToThePool()
        {
            CombatVfx combat = _host.AddComponent<CombatVfx>();
            combat.Spawn(_data, Vector3.zero);

            PooledVfx vfx = _host.GetComponentInChildren<PooledVfx>(includeInactive: true);
            Assert.IsNotNull(vfx, "эффект не заспавнился — тест проверяет не то, что думает");
            Assert.IsTrue(vfx.IsPlaying, "заспавненный эффект обязан играть");

            // Дуга переезжает внутрь тела бьющего, а тело уходит в свой пул вместе со смертью юнита.
            vfx.transform.SetParent(_body.transform);
            _body.SetActive(false);

            combat.ReclaimOrphans();

            Assert.IsFalse(vfx.IsPlaying,
                "выключенный чужой рукой эффект остался играющим: пул его не получит никогда, а когда " +
                "вид юнита переиспользуют, застрявший росчерк появится снова");
        }

        [Test]
        public void LiveEffect_IsNotReclaimed()
        {
            CombatVfx combat = _host.AddComponent<CombatVfx>();
            combat.Spawn(_data, Vector3.zero);

            PooledVfx vfx = _host.GetComponentInChildren<PooledVfx>(includeInactive: true);

            combat.ReclaimOrphans();

            Assert.IsTrue(vfx.IsPlaying,
                "живой эффект забрали в пул посреди показа — подбор сирот обязан трогать только тех, " +
                "кому больше некому тикать");
        }
    }
}
