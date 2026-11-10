using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DepthLab.Tests
{
    public class DepthLabExitDoorTests : DepthLabPlayerPrefsFixture
    {
        private GameObject root, world;
        private DepthLabApp app, originalApp;
        private DepthSortingChapter[] chapters;
        private DepthLabExitDoor Door => chapters[0].exitDoor;

        [SetUp]
        public void CreateIsolatedChapterFlow()
        {
            originalApp = DepthLabApp.Instance;
            root = new GameObject("Inactive exit test services");
            root.SetActive(false);
            world = new GameObject("Exit test world");
            app = root.AddComponent<DepthLabApp>();
            app.tutorial = root.AddComponent<DepthLabTutorial>();
            app.input = root.AddComponent<DepthLabInput>();
            app.playerRoot = Child("Rig", world.transform).transform;
            app.headCamera = Child("Head", app.playerRoot).AddComponent<Camera>();
            app.Head.localPosition = Vector3.up * 1.65f;
            app.input.playerRoot = app.playerRoot;
            app.input.headCamera = app.headCamera;
            SetProperty(typeof(DepthLabApp), null, "Instance", app);

            app.menuSpace = Child("Menu", world.transform);
            app.menuCanvas = Child("Pages", world.transform).transform;
            foreach (string field in new[] { "homePage", "chapterPage", "detailPage", "settingsPage",
                "creditsPage", "pausePage", "resultPage", "finalPage", "modalPage", "tutorialPage" })
                typeof(DepthLabApp).GetField(field).SetValue(app, Child(field, app.menuCanvas));
            app.hud = Child("HUD", world.transform);
            app.tutorialProps = Child("Tutorial props", world.transform);
            app.tooltipText = Text("Tooltip");
            app.hudText = Text("HUD text");
            app.hintText = Text("Hint");
            app.resultTitle = Text("Result title");
            app.resultBody = Text("Result body");
            app.finalTotals = Text("Final totals");
            app.nextChapterButton = Text("Next chapter").gameObject.AddComponent<Button>();
            app.chapterStatuses = new TMP_Text[4];
            app.homeStatuses = new TMP_Text[4];
            app.finalCards = new TMP_Text[4];
            chapters = new DepthSortingChapter[4];
            for (int i = 0; i < chapters.Length; i++)
            {
                app.chapterStatuses[i] = Text("Chapter status");
                app.homeStatuses[i] = Text("Home status");
                app.finalCards[i] = Text("Final card");
                GameObject chapterObject = Child("Chapter " + i, world.transform);
                chapterObject.SetActive(false);
                chapterObject.transform.position = Vector3.forward * (24f * (i + 1));
                DepthSortingChapter chapter = chapterObject.AddComponent<DepthSortingChapter>();
                chapters[i] = chapter;
                chapter.chapterIndex = i;
                chapter.spawnPoint = Child("Spawn", chapter.transform).transform;
                chapter.spawnPoint.localPosition = Vector3.back * 3f;
                DepthLabExitDoor door = Child("Door", chapter.transform).AddComponent<DepthLabExitDoor>();
                door.transform.localPosition = Vector3.forward * 6f;
                door.chapter = chapter;
                door.leftPanel = Child("Left leaf", door.transform).transform;
                door.rightPanel = Child("Right leaf", door.transform).transform;
                door.statusLabel = Text("Door status");
                door.conditionLabel = Text("Door condition");
                chapter.exitDoor = door;
            }
            app.chapters = chapters;
            // Keep the app inactive: drive the real flow without Start/Awake or a scene import.
            app.StartChapter(0);
        }

        [TearDown]
        public void RestoreAppAndDestroyTestObjects()
        {
            try
            {
                if (world != null) Object.DestroyImmediate(world);
                if (root != null) Object.DestroyImmediate(root);
            }
            finally
            {
                SetProperty(typeof(DepthLabApp), null, "Instance", originalApp);
            }
        }

        [Test]
        public void WalkingThroughSavesRunAndStartsExactlyTheNextChapterWithoutResultModal()
        {
            SetProperty(typeof(DepthLabApp), app, "ElapsedSeconds", 42f);
            SetProperty(typeof(DepthLabApp), app, "Mistakes", 1);
            SetProperty(typeof(DepthLabApp), app, "Hints", 1);
            SetProperty(typeof(DepthLabApp), app, "Retries", 1);
            Open(Door);
            Walk(Door);

            ChapterRecord record = DepthLabProgress.Read(0);
            Assert.That(record.completed, Is.True);
            Assert.That(record.seconds, Is.EqualTo(42f));
            Assert.That(record.mistakes, Is.EqualTo(1));
            Assert.That(record.hints, Is.EqualTo(1));
            Assert.That(record.retries, Is.EqualTo(1));
            Assert.That(record.stars, Is.EqualTo(2));
            Assert.That(record.attempts, Is.EqualTo(1));
            Assert.That(app.ActiveChapter, Is.EqualTo(1));
            Assert.That(app.IsPlaying, Is.True);
            Assert.That(app.IsPaused, Is.False);
            Assert.That(app.resultPage.activeSelf, Is.False);
            Assert.That(app.menuCanvas.gameObject.activeSelf, Is.False);
            Assert.That(app.ElapsedSeconds + app.Mistakes + app.Hints + app.Retries, Is.Zero);
            Assert.That(DepthLabProgress.Read(1).attempts, Is.EqualTo(1));
            Assert.That(DepthLabProgress.Read(2).attempts, Is.Zero);
            Assert.That(Vector3.Distance(Vector3.ProjectOnPlane(app.Head.position, Vector3.up),
                chapters[1].spawnPoint.position), Is.LessThan(.0001f));

            string saved = PlayerPrefs.GetString(ProgressPrefix + 0);
            Open(chapters[1].exitDoor);
            for (int i = 0; i < 3; i++)
            {
                Call(Door, "LateUpdate");
                chapters[0].WalkThroughExit();
                chapters[0].UseExit();
                app.CompleteChapterFromExit(chapters[0]);
            }
            Assert.That(app.ActiveChapter, Is.EqualTo(1), "Stale callbacks must not skip the next solved chapter.");
            Assert.That(PlayerPrefs.GetString(ProgressPrefix + 0), Is.EqualTo(saved));
            Assert.That(DepthLabProgress.Read(1).completed, Is.False);
            Assert.That(DepthLabProgress.Read(1).attempts, Is.EqualTo(1));
        }

        [TestCase(false, 1f)]
        [TestCase(true, .94f)]
        public void UnsolvedOrInsufficientlyOpenDoorCannotExit(bool solved, float amount)
        {
            if (solved) Solve(chapters[0]);
            SetProperty(typeof(DepthLabExitDoor), Door, "OpenAmount", amount);
            Walk(Door);
            AssertNotExited();
            Open(Door);
            Sample(Door, new Vector3(0f, 1.65f, .25f));
            AssertNotExited();
            Walk(Door);
            Assert.That(app.ActiveChapter, Is.EqualTo(1));
        }

        [TestCase(-.84f, 1.65f)]
        [TestCase(.84f, 1.65f)]
        [TestCase(0f, -.01f)]
        [TestCase(0f, 2.77f)]
        public void CrossingOutsideJambWidthOrHeightDoesNotExit(float x, float y)
        {
            Open(Door);
            Sample(Door, new Vector3(x, y, -.25f));
            Sample(Door, new Vector3(x, y, .2f));
            AssertNotExited();
        }

        [Test]
        public void RearEntryStandingBehindAndSideEntryDoNotExit()
        {
            Open(Door);
            Sample(Door, new Vector3(0f, 1.65f, .3f));
            Sample(Door, new Vector3(0f, 1.65f, .3f));
            Sample(Door, new Vector3(0f, 1.65f, -.25f));
            AssertNotExited();
            Sample(Door, new Vector3(1f, 1.65f, 0f));
            Sample(Door, new Vector3(0f, 1.65f, .2f));
            AssertNotExited();
            Walk(Door);
            Assert.That(app.ActiveChapter, Is.EqualTo(1));
        }

        [Test]
        public void OnlyHeadCrossingCountsNotRigOrPuzzleObjects()
        {
            Open(Door);
            Sample(Door, new Vector3(0f, 1.65f, -.25f));
            Vector3 head = app.Head.position;
            app.playerRoot.position = Door.transform.TransformPoint(new Vector3(0f, 0f, .2f));
            app.Head.position = head;
            Transform piece = Child("Unrelated object", world.transform).transform;
            piece.position = Door.transform.TransformPoint(new Vector3(0f, 1f, .2f));
            Call(Door, "LateUpdate");
            AssertNotExited();
            Sample(Door, new Vector3(0f, 1.65f, .2f));
            Assert.That(app.ActiveChapter, Is.EqualTo(1));
        }

        [Test]
        public void CrossingUsesDoorLocalSpaceAndSupportsSmallWalkingSteps()
        {
            Door.transform.SetPositionAndRotation(new Vector3(12f, 2f, -8f), Quaternion.Euler(0f, 63f, 0f));
            Door.transform.localScale = new Vector3(1.3f, .8f, .75f);
            Open(Door);
            foreach (float z in new[] { -.3f, -.1f, 0f, .1f })
                Sample(Door, new Vector3(.3f, 1.65f, z));
            AssertNotExited();
            Sample(Door, new Vector3(.3f, 1.65f, .16f));
            Assert.That(app.ActiveChapter, Is.EqualTo(1));
        }

        [Test]
        public void ExitFitsBeforeRearWallWithCharacterControllerClearance()
        {
            Open(Door);
            Sample(Door, new Vector3(0f, 1.65f, -.3f));
            // A 0.5m comfort step ends at room z=6.2, well before wall face z=6.9 minus capsule radius.
            Sample(Door, new Vector3(0f, 1.65f, .2f));
            Assert.That(app.ActiveChapter, Is.EqualTo(1));
        }

        [Test]
        public void PauseDiscardsApproachEvenWithoutDoorUpdateDuringPause()
        {
            Open(Door);
            Sample(Door, new Vector3(0f, 1.65f, -.25f));
            app.TogglePause();
            app.Head.position = Door.transform.TransformPoint(new Vector3(0f, 1.65f, .2f));
            chapters[0].WalkThroughExit();
            app.CompleteChapterFromExit(chapters[0]);
            AssertNotExited();
            app.Execute(LabAction.Resume);
            Call(Door, "LateUpdate");
            AssertNotExited();
            Walk(Door);
            Assert.That(app.ActiveChapter, Is.EqualTo(1));
        }

        [Test]
        public void TeleportAcrossOpenDoorDoesNotComplete()
        {
            Open(Door);
            Sample(Door, new Vector3(0f, 1.65f, -.25f));
            app.TeleportTo(Door.transform.TransformPoint(new Vector3(0f, 0f, .2f)));
            Call(Door, "LateUpdate");
            AssertNotExited();
            Walk(Door);
            Assert.That(app.ActiveChapter, Is.EqualTo(1));
        }

        [Test]
        public void LargeUnreportedPoseJumpIsNotAWalkThrough()
        {
            Open(Door);
            Sample(Door, new Vector3(0f, 1.65f, -2f));
            Sample(Door, new Vector3(0f, 1.65f, .2f));
            AssertNotExited();
            Walk(Door);
            Assert.That(app.ActiveChapter, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReentryAndReplayResetDoorAndCrossingState(bool replay)
        {
            Open(Door);
            Sample(Door, new Vector3(0f, 1.65f, -.25f));
            app.StartChapter(0, replay);
            Assert.That(Door.OpenAmount, Is.Zero);
            Assert.That(chapters[0].ExitReady, Is.False);
            Assert.That(Door.leftPanel.localPosition.x, Is.EqualTo(-.44f).Within(.0001f));
            Assert.That(Door.rightPanel.localPosition.x, Is.EqualTo(.44f).Within(.0001f));
            Open(Door);
            Sample(Door, new Vector3(0f, 1.65f, .2f));
            AssertNotExited();
            Walk(Door);
            Assert.That(app.ActiveChapter, Is.EqualTo(1));

            app.StartChapter(0);
            Assert.That(Door.OpenAmount, Is.Zero);
            Open(Door);
            Walk(Door);
            Assert.That(app.ActiveChapter, Is.EqualTo(1), "A previously consumed door must work on a new visit.");
            Assert.That(DepthLabProgress.Read(0).attempts, Is.EqualTo(3));
        }

        [Test]
        public void ManualUseExitRetainsResultModalFallbackAndCannotSaveTwice()
        {
            chapters[0].UseExit();
            AssertNotExited();
            Solve(chapters[0]);
            chapters[0].UseExit();
            Assert.That(app.ActiveChapter, Is.Zero);
            Assert.That(app.IsPlaying, Is.False);
            Assert.That(app.resultPage.activeSelf, Is.True);
            string saved = PlayerPrefs.GetString(ProgressPrefix + 0);
            chapters[0].UseExit();
            app.CompleteChapter();
            Assert.That(PlayerPrefs.GetString(ProgressPrefix + 0), Is.EqualTo(saved));
            Assert.That(DepthLabProgress.Read(1).attempts, Is.Zero);
        }

        [Test]
        public void LastChapterWalkThroughSavesAndShowsExistingFinalFlow()
        {
            for (int i = 0; i < 3; i++)
                DepthLabProgress.Complete(i, new ChapterRecord { stars = 3, seconds = 10f });
            app.StartChapter(3);
            DepthLabExitDoor lastDoor = chapters[3].exitDoor;
            Open(lastDoor);
            Call(lastDoor, "Update");
            Assert.That(lastDoor.conditionLabel.text, Does.Contain("查看总结"));
            Walk(lastDoor);
            Assert.That(DepthLabProgress.Read(3).completed, Is.True);
            Assert.That(app.ActiveChapter, Is.EqualTo(-1));
            Assert.That(app.IsPlaying, Is.False);
            Assert.That(app.finalPage.activeSelf, Is.True);
            Assert.That(app.resultPage.activeSelf, Is.False);
            Assert.That(app.finalTotals.text, Does.Contain("实验室 4/4"));
            Assert.That(PlayerPrefs.HasKey(ProgressPrefix + 4), Is.False);
            Call(lastDoor, "LateUpdate");
            Assert.That(app.finalPage.activeSelf, Is.True);
        }

        [Test]
        public void DoorLabelsDescribeLockedOpeningReadyPausedAndResetStates()
        {
            Call(Door, "Update");
            Assert.That(Door.statusLabel.text, Is.EqualTo("出口未解锁"));
            Assert.That(Door.conditionLabel.text, Is.EqualTo("完成实验后开启"));
            Solve(chapters[0]);
            Call(Door, "RefreshLabels");
            Assert.That(Door.statusLabel.text, Is.EqualTo("正在开门"));
            Open(Door);
            Call(Door, "Update");
            Assert.That(Door.statusLabel.text, Is.EqualTo("出口已开启"));
            Assert.That(Door.conditionLabel.text, Does.Contain("自动进入下一章"));
            app.TogglePause();
            Call(Door, "Update");
            Assert.That(Door.statusLabel.text, Is.EqualTo("已暂停"));
            Assert.That(Door.OpenAmount, Is.EqualTo(1f));
            app.StartChapter(0, true);
            Assert.That(Door.statusLabel.text, Is.EqualTo("出口未解锁"));
        }

        private void AssertNotExited()
        {
            Assert.That(app.ActiveChapter, Is.Zero);
            Assert.That(DepthLabProgress.Read(0).completed, Is.False);
            Assert.That(DepthLabProgress.Read(1).attempts, Is.Zero);
        }

        private void Sample(DepthLabExitDoor door, Vector3 localHead)
        {
            app.Head.position = door.transform.TransformPoint(localHead);
            Call(door, "LateUpdate");
        }

        private void Walk(DepthLabExitDoor door)
        {
            Sample(door, new Vector3(0f, 1.65f, -.25f));
            Sample(door, new Vector3(0f, 1.65f, .2f));
        }

        private static void Open(DepthLabExitDoor door)
        {
            Solve((DepthSortingChapter)door.chapter);
            SetProperty(typeof(DepthLabExitDoor), door, "OpenAmount", 1f);
        }

        private static void Solve(DepthSortingChapter chapter)
        {
            typeof(DepthSortingChapter).GetField("placed", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(chapter, chapter.Goal);
        }

        private TMP_Text Text(string name)
        {
            return Child(name, root.transform).AddComponent<TextMeshPro>();
        }

        private static GameObject Child(string name, Transform parent)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void SetProperty(System.Type type, object target, string name, object value)
        {
            type.GetProperty(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                .SetValue(target, value);
        }

        private static void Call(object target, string name)
        {
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(target, null);
        }
    }
}
