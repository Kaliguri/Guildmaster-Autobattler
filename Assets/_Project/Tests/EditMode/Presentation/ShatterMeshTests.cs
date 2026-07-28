using Guildmaster.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Сторож размера осколка. Плотность сетки задаётся Block Pixels в feel-конфиге, но реально её решает
    /// потолок в <see cref="ShatterMesh"/>: он уже однажды тихо съел настройку — на типовом спрайте
    /// уменьшение блока с 6 до 3 упиралось в кламп, и крутить ручку было бесполезно. Тест ловит возврат
    /// такого потолка в рабочий диапазон и заодно держит верхнюю границу, чтобы меш не взорвался.
    /// </summary>
    public sealed class ShatterMeshTests
    {
        private static readonly Vector2 Size   = new Vector2(1f, 1.7f);
        private static readonly Rect    UvRect = new Rect(0f, 0f, 1f, 1f);

        // Блоков в меше: 4 вершины на блок.
        private static int BlockCount(Vector2 regionPixels, int blockPixels)
        {
            Mesh mesh = ShatterMesh.Build(Size, UvRect, regionPixels, blockPixels);
            int blocks = mesh.vertexCount / 4;
            Object.DestroyImmediate(mesh);
            return blocks;
        }

        [Test]
        public void SmallerBlocks_ProduceDenserGrid_OnTypicalSprite()
        {
            var region = new Vector2(64f, 64f); // типовой размер видимой области персонажа

            int coarse = BlockCount(region, 6);
            int fine   = BlockCount(region, 3);

            Assert.That(fine, Is.GreaterThan(coarse * 3),
                "Уменьшение блока вдвое должно давать примерно вчетверо больше осколков. " +
                "Если прирост меньше — сетку режет потолок, и ручка Block Pixels мертва.");
        }

        [Test]
        public void GridStaysWithinCeiling_OnExtremeSettings()
        {
            var region = new Vector2(256f, 256f);

            int blocks = BlockCount(region, 1); // просят пиксель-в-пиксель на большом спрайте

            Assert.That(blocks, Is.LessThanOrEqualTo(48 * 64),
                "Потолок сетки должен держать меш конечным даже на абсурдных настройках.");
        }

        [Test]
        public void TinySprite_StillShatters()
        {
            var region = new Vector2(8f, 8f); // мелкий спрайт: блок крупнее самого спрайта

            int blocks = BlockCount(region, 16);

            Assert.That(blocks, Is.GreaterThanOrEqualTo(3 * 3),
                "Даже на мелком спрайте осколков должно быть больше одного — иначе смерть выглядит как рывок.");
        }
    }
}
