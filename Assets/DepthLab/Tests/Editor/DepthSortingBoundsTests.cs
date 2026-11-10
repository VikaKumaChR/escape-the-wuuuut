using NUnit.Framework;
using UnityEngine;

namespace DepthLab.Tests
{
    public class DepthSortingBoundsTests
    {
        private GameObject root;
        private DepthSortingChapter chapter;

        [SetUp]
        public void CreateInactiveChapter()
        {
            root = new GameObject("DepthLab bounds test");
            root.SetActive(false);
            chapter = root.AddComponent<DepthSortingChapter>();
            chapter.boundsHalfSize = new Vector3(6f, 4f, 7f);
        }

        [TearDown]
        public void DestroyChapter()
        {
            if (root != null)
                Object.DestroyImmediate(root);
        }

        [TestCase(6f, 1f, 0f, false)]
        [TestCase(-6f, 1f, 0f, false)]
        [TestCase(0f, 1f, 7f, false)]
        [TestCase(0f, 1f, -7f, false)]
        [TestCase(0f, -.5f, 0f, false)]
        [TestCase(0f, 4f, 0f, false)]
        [TestCase(6.01f, 1f, 0f, true)]
        [TestCase(-6.01f, 1f, 0f, true)]
        [TestCase(0f, 1f, 7.01f, true)]
        [TestCase(0f, 1f, -7.01f, true)]
        [TestCase(0f, -.51f, 0f, true)]
        [TestCase(0f, 4.01f, 0f, true)]
        public void IsOutOfBounds_IncludesFacesButRejectsPointsBeyondEachFace(
            float x, float y, float z, bool expected)
        {
            Assert.That(chapter.IsOutOfBounds(new Vector3(x, y, z)), Is.EqualTo(expected));
        }

        [Test]
        public void IsOutOfBounds_EvaluatesChapterLocalSpaceAfterTranslationRotationAndScale()
        {
            root.transform.SetPositionAndRotation(new Vector3(20f, 10f, -30f), Quaternion.Euler(0f, 90f, 0f));
            root.transform.localScale = new Vector3(2f, 3f, 4f);

            Assert.That(chapter.IsOutOfBounds(new Vector3(44f, 13f, -40f)), Is.False);
            Assert.That(chapter.IsOutOfBounds(new Vector3(44f, 13f, -44f)), Is.True);
            Assert.That(chapter.IsOutOfBounds(new Vector3(52f, 13f, -40f)), Is.True);
            Assert.That(chapter.IsOutOfBounds(new Vector3(44f, 8f, -40f)), Is.True);
        }
    }
}
