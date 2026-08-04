using Guildmaster.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Guildmaster.Tests.EditMode.Presentation
{
    /// <summary>
    /// Сторож размера осколка. Осколок меряется ДЛИНОЙ (доля роста тела → локальные единицы части), а не
    /// числом исходных пикселей — иначе разрешение арта определяет крупность кусков: у покадрового
    /// бестиария весь юнит занимает 48 px, у частей скелетного «сторибука» одна кисть — 112 px при PPU
    /// 1000, и общий «чанк в 6 px» рассыпал второго на десяток тысяч пылинок.
    /// </summary>
    public sealed class ShatterMeshTests
    {
        private static readonly Rect UvRect = new Rect(0f, 0f, 1f, 1f);

        // Блоков в меше: 4 вершины на блок.
        private static int BlockCount(Vector2 size, Vector2 regionPixels, float shard)
        {
            Mesh mesh = ShatterMesh.Build(size, UvRect, regionPixels, shard);
            int blocks = mesh.vertexCount / 4;
            Object.DestroyImmediate(mesh);
            return blocks;
        }

        [Test]
        public void SmallerShards_ProduceDenserGrid()
        {
            var size   = new Vector2(1f, 1.7f);
            var region = new Vector2(64f, 64f);

            int coarse = BlockCount(size, region, 0.2f);
            int fine   = BlockCount(size, region, 0.1f);

            Assert.That(fine, Is.GreaterThan(coarse * 3),
                "Уменьшение осколка вдвое должно давать примерно вчетверо больше кусков. " +
                "Если прирост меньше — сетку режет потолок, и ручка мертва.");
        }

        [Test]
        public void SameWorldSize_DifferentArtResolution_GivesSameShardCount()
        {
            var size = new Vector2(1f, 1.7f);

            // Одна и та же деталь, нарисованная пиксель-артом и «сторибуком»: разрешение исходника разное,
            // видимый размер один. Кусков обязано быть поровну — это и есть смысл меры в длине.
            int pixelArt  = BlockCount(size, new Vector2(48f, 82f),   0.2f);
            int storybook = BlockCount(size, new Vector2(585f, 995f), 0.2f);

            Assert.That(storybook, Is.EqualTo(pixelArt),
                "Разрешение арта не имеет права менять крупность осколков — иначе каждый новый кит " +
                "приносит свою плотность разлёта.");
        }

        [Test]
        public void PartSmallerThanShard_FliesAsOnePiece()
        {
            // Кисть скелетного юнита рядом с осколком в десятую часть роста: дробить её не на что.
            int blocks = BlockCount(new Vector2(0.08f, 0.07f), new Vector2(112f, 106f), 0.17f);

            Assert.That(blocks, Is.EqualTo(1),
                "Часть мельче осколка обязана улететь целым куском: насильное дробление делает кисть " +
                "мельче торса при общем размере чанка.");
        }

        [Test]
        public void GridStaysWithinCeiling_OnExtremeSettings()
        {
            int blocks = BlockCount(new Vector2(2f, 3f), new Vector2(2048f, 2048f), 0.0001f);

            Assert.That(blocks, Is.LessThanOrEqualTo(32 * 32),
                "Потолок сетки должен держать меш конечным даже на абсурдных настройках.");
        }
    }
}
