using NUnit.Framework;
using TwistedSpace.Puzzles;
using UnityEngine;

namespace TwistedSpace.Tests
{
    /// <summary>Guards the conservative, all-target intersection metric independently of scene state.</summary>
    public sealed class ProjectedBoundsTests
    {
        [Test]
        public void IdenticalRectanglesOverlapCompletely()
        {
            Rect rectangle = new Rect(0.2f, 0.3f, 0.4f, 0.3f);
            Assert.That(ProjectedBoundsUtility.ConservativeOverlap(new[] { rectangle, rectangle, rectangle }),
                Is.EqualTo(1f).Within(0.00001f));
        }

        [Test]
        public void OneOutlierInvalidatesAnOtherwiseAlignedGroup()
        {
            Rect good = new Rect(0f, 0f, 0.2f, 0.2f);
            Rect bad = new Rect(0.8f, 0.8f, 0.1f, 0.1f);
            Assert.That(ProjectedBoundsUtility.ConservativeOverlap(new[] { good, good, good, bad }), Is.Zero);
        }

        [Test]
        public void RelaxedThresholdAcceptsSmallMisalignment()
        {
            Rect first = new Rect(0f, 0f, 1f, 1f);
            Rect second = new Rect(0.1f, 0.05f, 1f, 1f);
            float overlap = ProjectedBoundsUtility.ConservativeOverlap(new[] { first, second });
            Assert.That(overlap, Is.GreaterThan(0.65f).And.LessThan(0.8f));
        }

        [Test]
        public void InvalidOrInsufficientRectanglesCannotSolve()
        {
            Rect valid = new Rect(0f, 0f, 1f, 1f);
            Assert.That(ProjectedBoundsUtility.ConservativeOverlap(new[] { valid }), Is.Zero);
            Assert.That(ProjectedBoundsUtility.ConservativeOverlap(new[] { valid, new Rect() }), Is.Zero);
            Assert.That(ProjectedBoundsUtility.ConservativeOverlap(
                new[] { valid, new Rect(float.NaN, 0f, 1f, 1f) }), Is.Zero);
        }
    }
}
