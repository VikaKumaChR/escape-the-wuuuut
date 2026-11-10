using NUnit.Framework;
using UnityEngine;

namespace DepthLab.Tests
{
    public class DepthLabProgressTests : DepthLabPlayerPrefsFixture
    {
        [Test]
        public void Read_MissingKeyReturnsIndependentEmptyRecordsWithoutWriting()
        {
            ChapterRecord first = DepthLabProgress.Read(0);
            AssertRecord(first, false, 0, 0f, 0, 0, 0, 0);
            first.stars = 3;
            AssertRecord(DepthLabProgress.Read(0), false, 0, 0f, 0, 0, 0, 0);
            Assert.That(PlayerPrefs.HasKey(ProgressPrefix + 0), Is.False);
        }

        [TestCase("")]
        [TestCase("{")]
        [TestCase("not-json")]
        public void Read_InvalidDataReturnsEmptyRecordWithoutOverwritingStoredValue(string value)
        {
            PlayerPrefs.SetString(ProgressPrefix + 0, value);

            AssertRecord(DepthLabProgress.Read(0), false, 0, 0f, 0, 0, 0, 0);
            Assert.That(PlayerPrefs.HasKey(ProgressPrefix + 0), Is.True);
            Assert.That(PlayerPrefs.GetString(ProgressPrefix + 0), Is.EqualTo(value));
        }

        [TestCase(-1)]
        [TestCase(4)]
        [TestCase(100)]
        public void Unlocked_RejectsIndicesOutsideChapterRange(int index)
        {
            for (int i = 0; i < 4; i++)
                DepthLabProgress.Complete(i, Run(3, 10f));

            Assert.That(DepthLabProgress.Unlocked(index), Is.False);
        }

        [Test]
        public void Unlocked_RequiresCompletionOfImmediatePredecessor()
        {
            Assert.That(DepthLabProgress.Unlocked(0), Is.True);
            for (int i = 1; i < 4; i++)
                Assert.That(DepthLabProgress.Unlocked(i), Is.False);

            DepthLabProgress.BeginAttempt(0);
            Assert.That(DepthLabProgress.Unlocked(1), Is.False, "An attempt is not completion.");

            for (int i = 0; i < 3; i++)
            {
                DepthLabProgress.Complete(i, Run(1, 500f));
                Assert.That(DepthLabProgress.Unlocked(i + 1), Is.True);
                if (i + 2 < 4)
                    Assert.That(DepthLabProgress.Unlocked(i + 2), Is.False);
            }
        }

        [Test]
        public void Complete_FirstRunPersistsEveryFieldAndUsesRecordedAttemptCount()
        {
            DepthLabProgress.BeginAttempt(0);
            DepthLabProgress.BeginAttempt(0);
            ChapterRecord run = Run(2, 123.5f, 1, 2, 3);
            run.attempts = 99;

            DepthLabProgress.Complete(0, run);

            AssertRecord(DepthLabProgress.Read(0), true, 2, 123.5f, 1, 2, 3, 2);
            Assert.That(PlayerPrefs.HasKey(ProgressPrefix + 0), Is.True);
            ChapterRecord serialized = JsonUtility.FromJson<ChapterRecord>(
                PlayerPrefs.GetString(ProgressPrefix + 0));
            AssertRecord(serialized, true, 2, 123.5f, 1, 2, 3, 2);
        }

        [TestCase(3, 300f, true)]
        [TestCase(2, 99f, true)]
        [TestCase(2, 100f, false)]
        [TestCase(2, 101f, false)]
        [TestCase(1, 10f, false)]
        public void Complete_SelectsBestRunByStarsThenTimeAndKeepsWholeRun(
            int stars, float seconds, bool replacesBest)
        {
            DepthLabProgress.BeginAttempt(0);
            DepthLabProgress.Complete(0, Run(2, 100f, 1, 2, 3));
            DepthLabProgress.BeginAttempt(0);
            string before = PlayerPrefs.GetString(ProgressPrefix + 0);

            DepthLabProgress.Complete(0, Run(stars, seconds, 4, 5, 6));

            if (replacesBest)
                AssertRecord(DepthLabProgress.Read(0), true, stars, seconds, 4, 5, 6, 2);
            else
            {
                AssertRecord(DepthLabProgress.Read(0), true, 2, 100f, 1, 2, 3, 2);
                Assert.That(PlayerPrefs.GetString(ProgressPrefix + 0), Is.EqualTo(before));
            }
        }

        [Test]
        public void BeginAttempt_AfterCompletionPreservesBestRunAndOtherChapters()
        {
            DepthLabProgress.BeginAttempt(0);
            DepthLabProgress.Complete(0, Run(3, 42f, 1, 2, 3));
            DepthLabProgress.BeginAttempt(1);
            string otherChapter = PlayerPrefs.GetString(ProgressPrefix + 1);

            DepthLabProgress.BeginAttempt(0);

            AssertRecord(DepthLabProgress.Read(0), true, 3, 42f, 1, 2, 3, 2);
            Assert.That(PlayerPrefs.GetString(ProgressPrefix + 1), Is.EqualTo(otherChapter));
            Assert.That(DepthLabProgress.Unlocked(1), Is.True);
        }

        [Test]
        public void Reset_DeletesOnlyFourProgressKeysAndRelocksChapters()
        {
            foreach (string name in SettingNames)
                PlayerPrefs.SetString(SettingsPrefix + name, "keep-" + name);
            PlayerPrefs.SetString(UnrelatedKey, "unrelated");
            PlayerPrefs.SetString(ProgressPrefix + 4, "outside-chapter-range");
            for (int i = 0; i < 4; i++)
            {
                DepthLabProgress.BeginAttempt(i);
                DepthLabProgress.Complete(i, Run(3, 10f));
            }

            DepthLabProgress.Reset();
            DepthLabProgress.Reset();

            for (int i = 0; i < 4; i++)
            {
                Assert.That(PlayerPrefs.HasKey(ProgressPrefix + i), Is.False);
                AssertRecord(DepthLabProgress.Read(i), false, 0, 0f, 0, 0, 0, 0);
                Assert.That(DepthLabProgress.Unlocked(i), Is.EqualTo(i == 0));
            }
            foreach (string name in SettingNames)
                Assert.That(PlayerPrefs.GetString(SettingsPrefix + name), Is.EqualTo("keep-" + name));
            Assert.That(PlayerPrefs.GetString(UnrelatedKey), Is.EqualTo("unrelated"));
            Assert.That(PlayerPrefs.GetString(ProgressPrefix + 4), Is.EqualTo("outside-chapter-range"));
        }

        private static ChapterRecord Run(int stars, float seconds, int mistakes = 0, int hints = 0, int retries = 0)
        {
            return new ChapterRecord
            {
                stars = stars, seconds = seconds, mistakes = mistakes, hints = hints, retries = retries
            };
        }

        private static void AssertRecord(ChapterRecord record, bool completed, int stars, float seconds,
            int mistakes, int hints, int retries, int attempts)
        {
            Assert.That(record.completed, Is.EqualTo(completed));
            Assert.That(record.stars, Is.EqualTo(stars));
            Assert.That(record.seconds, Is.EqualTo(seconds));
            Assert.That(record.mistakes, Is.EqualTo(mistakes));
            Assert.That(record.hints, Is.EqualTo(hints));
            Assert.That(record.retries, Is.EqualTo(retries));
            Assert.That(record.attempts, Is.EqualTo(attempts));
        }
    }
}
