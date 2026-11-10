using NUnit.Framework;

namespace DepthLab.Tests
{
    public class DepthLabScoringTests
    {
        [TestCase(180f, 0, 0, 0, 3)]
        [TestCase(180.01f, 0, 0, 0, 2)]
        [TestCase(360f, 0, 0, 0, 2)]
        [TestCase(360.01f, 0, 0, 0, 1)]
        [TestCase(20f, 1, 0, 0, 2)]
        [TestCase(20f, 0, 1, 0, 2)]
        [TestCase(20f, 0, 0, 1, 2)]
        [TestCase(360f, 1, 1, 1, 2)]
        [TestCase(20f, 2, 1, 1, 1)]
        [TestCase(20f, 4, 0, 0, 1)]
        [TestCase(20f, 0, 4, 0, 1)]
        [TestCase(20f, 0, 0, 4, 1)]
        public void CalculateStars_RequiresBothTimeAndCombinedPenalty(
            float seconds, int mistakes, int hints, int retries, int expected)
        {
            Assert.That(DepthLabProgress.CalculateStars(
                seconds, mistakes, hints, retries, 180f, 360f, 0, 3), Is.EqualTo(expected));
        }

        [TestCase(45f, 1, 1, 0, 3)]
        [TestCase(45.01f, 1, 1, 0, 2)]
        [TestCase(45f, 1, 1, 1, 2)]
        [TestCase(90f, 2, 2, 1, 2)]
        [TestCase(90.01f, 2, 2, 1, 1)]
        [TestCase(90f, 2, 2, 2, 1)]
        public void CalculateStars_UsesChapterSpecificThresholds(
            float seconds, int mistakes, int hints, int retries, int expected)
        {
            Assert.That(DepthLabProgress.CalculateStars(
                seconds, mistakes, hints, retries, 45f, 90f, 2, 5), Is.EqualTo(expected));
        }
    }
}
