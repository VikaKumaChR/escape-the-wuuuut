using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Object = UnityEngine.Object;

namespace DepthLab.Tests
{
    // The isolated input runtime provides real frame edges without entering PlayMode.
    public class DepthLabInputTests : InputTestFixture
    {
        private const float Tolerance = .0001f;
        private GameObject root, activeObjects;
        private DepthLabInput input;
        private DepthLabApp app, originalApp;
        private DepthLabTutorial tutorial;
        private Transform rig, head;
        private Keyboard keyboard;
        private Mouse mouse;
        private EventSystem events, originalEvents;
        private DepthLabInputTestRaycaster raycaster;
        private Scene physicsScene;

        [SetUp]
        public void CreateIsolatedInput()
        {
            originalApp = DepthLabApp.Instance;
            originalEvents = EventSystem.current;
            root = new GameObject("DepthLab input test");
            root.SetActive(false);
            activeObjects = new GameObject("DepthLab input test pages");
            physicsScene = EditorSceneManager.NewPreviewScene();
            SceneManager.MoveGameObjectToScene(activeObjects, physicsScene);
            app = root.AddComponent<DepthLabApp>();
            tutorial = root.AddComponent<DepthLabTutorial>();
            input = root.AddComponent<DepthLabInput>();
            input.settings = root.AddComponent<DepthLabSettings>();
            rig = Child("Rig", activeObjects.transform).transform;
            head = Child("Head", rig).transform;
            head.localPosition = new Vector3(0f, 1.65f, 0f);
            input.headCamera = head.gameObject.AddComponent<Camera>();
            input.headCamera.pixelRect = new Rect(0f, 0f, 100f, 100f);
            input.playerRoot = rig;
            input.motor = rig.gameObject.AddComponent<CharacterController>();
            input.motor.radius = .25f;
            input.motor.height = 1.75f;
            input.motor.center = Vector3.up * .875f;
            input.motor.skinWidth = .02f;
            input.motor.stepOffset = .1f;
            input.motor.minMoveDistance = 0f;
            app.headCamera = input.headCamera;
            app.playerRoot = rig;
            app.input = input;
            app.tutorial = tutorial;
            tutorial.head = head;
            tutorial.playerRoot = rig;
            // Never activate the app or call Begin: both can reach unrelated scene/global state.
            Property(typeof(DepthLabApp), "Instance").SetValue(null, app);
            events = Child("EventSystem", activeObjects.transform).AddComponent<EventSystem>();
            // These UI behaviours do not receive their runtime lifecycle in EditMode.
            Call(events, "OnEnable");
            EventSystem.current = events;
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            Frame();
            input.ResetPose();
            Mode("gameplay");
        }

        [TearDown]
        public void RestoreGlobalsAndDestroyOnlyTestObjects()
        {
            try
            {
                if (input != null) Call(input, "CancelInput");
                if (raycaster != null) Call(raycaster, "OnDisable");
                if (events != null) Call(events, "OnDisable");
                if (app != null) Object.DestroyImmediate(app);
                if (activeObjects != null) Object.DestroyImmediate(activeObjects);
                if (root != null) Object.DestroyImmediate(root);
                if (physicsScene.IsValid()) EditorSceneManager.ClosePreviewScene(physicsScene);
            }
            finally
            {
                Property(typeof(DepthLabApp), "Instance").SetValue(null, originalApp);
                if (originalEvents != null) EventSystem.current = originalEvents;
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            }
        }

        [Test]
        public void Desktop_DefaultOrNullComfortSettingsMoveAndTurnEveryFrame(
            [Values(30, 60, 144)] int fps, [Values(false, true)] bool nullSettings)
        {
            if (nullSettings) input.settings = null;
            float dt = 1f / fps;
            for (int frame = 0; frame < fps; frame++)
            {
                Move(Vector2.up, false, dt, frame * dt);
                Assert.That(rig.position.z, Is.EqualTo(1.5f * (frame + 1) * dt).Within(Tolerance));
            }
            for (int frame = 0; frame < fps; frame++)
            {
                Turn(1f, false, dt);
                AssertRotation(rig.rotation, Quaternion.Euler(0f, 60f * (frame + 1) * dt, 0f));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void XR_DefaultOrNullMovementStepsAtPointThreeSecondsAndNeutralRearms(bool nullSettings)
        {
            if (nullSettings) input.settings = null;
            Move(Vector2.up, true, .001f, 0f);
            Assert.That(rig.position.z, Is.EqualTo(.45f).Within(Tolerance));
            Move(Vector2.up, true, 1f, .299f);
            Assert.That(rig.position.z, Is.EqualTo(.45f).Within(Tolerance));
            Move(Vector2.up, true, .001f, .3f);
            Assert.That(rig.position.z, Is.EqualTo(.9f).Within(Tolerance));
            Move(Vector2.up, true, .001f, .599f);
            Assert.That(rig.position.z, Is.EqualTo(.9f).Within(Tolerance));
            Move(Vector2.up, true, .001f, .6f);
            Assert.That(rig.position.z, Is.EqualTo(1.35f).Within(Tolerance));
            Move(Vector2.up * .2f, true, .001f, .61f);
            Move(Vector2.up, true, .001f, .62f);
            Assert.That(rig.position.z, Is.EqualTo(1.8f).Within(Tolerance));
        }

        [Test]
        public void XR_StepDistanceIsCappedAtHalfAMetre()
        {
            input.settings.moveSpeed = 10f;
            Move(Vector2.up, true, 10f, 0f);
            Assert.That(rig.position.z, Is.EqualTo(.5f).Within(Tolerance));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void XR_DefaultOrNullSnapRequiresNeutralBeforeTurningAgain(bool nullSettings)
        {
            if (nullSettings) input.settings = null;
            Turn(.64f, true, 1f);
            AssertRotation(rig.rotation, Quaternion.identity);
            Turn(.7f, true, .001f);
            AssertRotation(rig.rotation, Quaternion.Euler(0f, 30f, 0f));
            Turn(1f, true, 1f);
            Turn(-1f, true, 1f);
            Turn(.3f, true, 1f);
            Turn(-1f, true, 1f);
            AssertRotation(rig.rotation, Quaternion.Euler(0f, 30f, 0f));
            Turn(.24f, true, .001f);
            Turn(-1f, true, .001f);
            AssertRotation(rig.rotation, Quaternion.identity);
        }

        [Test]
        public void XR_SnapUsesConfiguredAngle()
        {
            input.settings.snapAngle = 60f;
            Turn(-1f, true, .001f);
            AssertRotation(rig.rotation, Quaternion.Euler(0f, -60f, 0f));
        }

        [Test]
        public void XR_OptionalSmoothModesAreFrameRateIndependent([Values(30, 60, 144)] int fps)
        {
            input.settings.moveMode = input.settings.turnMode = 1;
            for (int frame = 0; frame < fps; frame++)
                Move(Vector2.up, true, 1f / fps, frame / (float)fps);
            Assert.That(rig.position.z, Is.EqualTo(1.5f).Within(Tolerance));
            for (int frame = 0; frame < fps; frame++)
                Turn(1f, true, 1f / fps);
            AssertRotation(rig.rotation, Quaternion.Euler(0f, 60f, 0f));
        }

        [TestCase(false, .15f)]
        [TestCase(true, .45f)]
        public void Move_NormalizesDiagonalsAndPreservesHeadLocalTranslation(bool xr, float distance)
        {
            Vector3 local = head.localPosition;
            Move(Vector2.one, xr, .1f, 0f);
            Assert.That(rig.position.magnitude, Is.EqualTo(distance).Within(Tolerance));
            Assert.That(rig.position.x, Is.EqualTo(rig.position.z).Within(Tolerance));
            AssertVector(head.localPosition, local);
            AssertVector(rig.InverseTransformPoint(head.position), local);
        }

        [TestCase(1f)]
        [TestCase(-1f)]
        public void Move_UsesSceneGeometryNotGlobalOrSpawnRelativeClamps(float direction)
        {
            rig.position = new Vector3(10f, 3f, 20f);
            input.ResetPose();
            Move(Vector2.up * direction, false, 4f, 0f);
            AssertVector(rig.position, new Vector3(10f, 3f, 20f + direction * 6f));
            Move(Vector2.right * direction, false, 4f, 1f);
            AssertVector(rig.position, new Vector3(10f + direction * 6f, 3f, 20f + direction * 6f));
        }

        [Test]
        public void Turn_PivotsAroundOffsetHeadAndKeepsCapsuleFixedOutsideOldBounds()
        {
            head.localPosition += Vector3.forward;
            Vector3 worldHead = head.position;
            Vector3 localHead = head.localPosition;
            Turn(1f, false, .5f);
            AssertVector(head.position, worldHead);
            AssertVector(head.localPosition, localHead);
            AssertRotation(rig.rotation, Quaternion.Euler(0f, 30f, 0f));
            rig.SetPositionAndRotation(new Vector3(4f, 0f, 0f), Quaternion.identity);
            worldHead = head.position;
            Call(input, "AlignMotor");
            Vector3 capsuleCenter = rig.TransformPoint(input.motor.center);
            Turn(-1f, false, .5f);
            Assert.That(rig.position.x, Is.GreaterThan(4f));
            AssertVector(head.position, worldHead);
            AssertVector(head.localPosition, localHead);
            AssertVector(rig.TransformPoint(input.motor.center), capsuleCenter);
            AssertRotation(rig.rotation, Quaternion.Euler(0f, -30f, 0f));
        }

        [TestCase("missing")]
        [TestCase("disabled")]
        [TestCase("collisionDisabled")]
        public void Move_WithoutUsableMotorNeverFallsBackToTransform(string failure)
        {
            if (failure == "missing") Object.DestroyImmediate(input.motor);
            else if (failure == "disabled") input.motor.enabled = false;
            else input.motor.detectCollisions = false;
            Move(Vector2.up, false, 1f, 0f);
            AssertVector(rig.position, Vector3.zero);
        }

        [Test]
        public void Move_ResolvesUnassignedOrWrongRootMotor()
        {
            CharacterController correct = input.motor;
            input.motor = Child("Wrong rig", activeObjects.transform).AddComponent<CharacterController>();
            input.motor.transform.position = Vector3.left * 10f;
            Move(Vector2.up, false, .1f, 0f);
            Assert.That(input.motor, Is.SameAs(correct));
            Assert.That(rig.position.z, Is.EqualTo(.15f).Within(Tolerance));
            input.motor = null;
            Move(Vector2.up, false, .1f, 1f);
            Assert.That(input.motor, Is.SameAs(correct));
        }

        [Test]
        public void Move_CharacterControllerStopsAtSolidsUnderOffsetHead(
            [Values(PrimitiveType.Cube, PrimitiveType.Sphere, PrimitiveType.Cylinder)] PrimitiveType type,
            [Values(false, true)] bool xr)
        {
            head.localPosition = new Vector3(1.1f, 1.65f, .4f);
            Vector3 localHead = head.localPosition;
            Quaternion localRotation = head.localRotation;
            Solid(type, new Vector3(1.1f, type == PrimitiveType.Cylinder ? .45f : .6f, 2f),
                new Vector3(1f, type == PrimitiveType.Cylinder ? .45f : 1.2f, 1f));
            Solid(PrimitiveType.Cube, new Vector3(0f, -.1f, 0f), new Vector3(20f, .2f, 20f));
            for (int i = 0; i < 20; i++) Move(Vector2.up, xr, .1f, i * .31f);
            Assert.That(rig.position.z, Is.InRange(.3f, 1.05f), "Head capsule, not the offset origin, must collide.");
            Assert.That(rig.position.y, Is.LessThan(.15f), "Do not climb a pedestal.");
            AssertVector(head.localPosition, localHead);
            AssertRotation(head.localRotation, localRotation);
        }

        [Test]
        public void Move_PassesOldRearClampButStopsAtPhysicalRoomWallEvenOnLongFrame()
        {
            rig.position = Vector3.back * 3f;
            input.ResetPose();
            Solid(PrimitiveType.Cube, new Vector3(0f, 1.8f, 7f), new Vector3(12f, 3.6f, .2f));
            Move(Vector2.up, false, 4f, 0f);
            Assert.That(rig.position.z, Is.EqualTo(3f).Within(Tolerance));
            Move(Vector2.up, false, 10f, 1f);
            Assert.That(rig.position.z, Is.InRange(6.5f, 6.75f));
        }

        [Test]
        public void Move_SlidesAlongWallRatherThanStoppingAllMotion()
        {
            Solid(PrimitiveType.Cube, new Vector3(1f, 1.5f, 0f), new Vector3(.1f, 3f, 20f));
            for (int i = 0; i < 30; i++) Move(Vector2.one, false, .1f, i * .1f);
            Assert.That(rig.position.x, Is.InRange(.6f, .76f));
            Assert.That(rig.position.z, Is.GreaterThan(2.5f));
        }

        [Test]
        public void Move_CollidesWithDynamicPuzzleBodyAndIgnoresTriggerVolumes()
        {
            GameObject trigger = Solid(PrimitiveType.Cube, new Vector3(0f, 1f, 1f), Vector3.one);
            trigger.GetComponent<Collider>().isTrigger = true;
            GameObject piece = Solid(PrimitiveType.Sphere, new Vector3(0f, 1.1f, 3f), Vector3.one);
            piece.AddComponent<Rigidbody>().useGravity = false;
            Move(Vector2.up, false, 3f, 0f);
            Assert.That(rig.position.z, Is.InRange(2.1f, 2.4f));
        }

        [Test]
        public void Grab_MouseRayAcquiresPhysicalBodyAndReleaseRestoresPhysics()
        {
            XRGrabInteractable grab = PhysicalGrab(PrimitiveType.Cube, new Vector3(0f, 1.65f, 2f));
            Rigidbody body = grab.GetComponent<Rigidbody>();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(50f, 50f) }
                .WithButton(MouseButton.Left, true));
            InputSystem.Update();
            Call(input, "UpdateGrab");
            Assert.That(Get<XRGrabInteractable>(input, "held"), Is.SameAs(grab));
            Assert.That(body.isKinematic, Is.True);
            Assert.That(body.useGravity, Is.False);
            Call(input, "ReleaseGrab");
            Assert.That(body.isKinematic, Is.False);
            Assert.That(body.useGravity, Is.True);
            Assert.That(body.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode.ContinuousDynamic));
            AssertVector(body.linearVelocity, Vector3.zero);
        }

        [TestCase("trigger")]
        [TestCase("disabledCollider")]
        [TestCase("disabledCollision")]
        public void Grab_RequiresEnabledSolidPhysicsGeometry(string failure)
        {
            XRGrabInteractable grab = PhysicalGrab(PrimitiveType.Cube, new Vector3(2f, 1.3f, 0f));
            if (failure == "trigger") grab.GetComponent<Collider>().isTrigger = true;
            else if (failure == "disabledCollider") grab.GetComponent<Collider>().enabled = false;
            else grab.GetComponent<Rigidbody>().detectCollisions = false;
            Call(input, "BeginGrab", grab, grab.transform.position, 2f);
            Assert.That(Get<XRGrabInteractable>(input, "held"), Is.Null);
            Assert.That(grab.GetComponent<Rigidbody>().isKinematic, Is.False);
        }

        [Test]
        public void Grab_SweepsBoxSphereAndConvexMeshAgainstThinWall(
            [Values("box", "sphere", "convex", "compound")] string shape)
        {
            XRGrabInteractable grab = PhysicalGrab(shape == "sphere" ? PrimitiveType.Sphere : PrimitiveType.Cube,
                new Vector3(2f, 1.3f, 0f));
            if (shape == "convex")
            {
                Object.DestroyImmediate(grab.GetComponent<BoxCollider>());
                MeshCollider mesh = grab.gameObject.AddComponent<MeshCollider>();
                mesh.sharedMesh = grab.GetComponent<MeshFilter>().sharedMesh;
                mesh.convex = true;
            }
            else if (shape == "compound")
            {
                GameObject child = Child("Forward compound collider", grab.transform);
                child.transform.localPosition = Vector3.forward;
                child.AddComponent<BoxCollider>();
            }
            Solid(PrimitiveType.Cube, new Vector3(2f, 1.3f, 1.5f), new Vector3(4f, 4f, .02f));
            BeginPhysicalGrab(grab);
            AdvanceGrab(new Vector3(2f, 1.3f, 8f), 5);
            Rigidbody body = grab.GetComponent<Rigidbody>();
            Assert.That(body.position.z, Is.InRange(.8f, 1.3f));
            foreach (Collider collider in grab.GetComponentsInChildren<Collider>())
                Assert.That(collider.bounds.max.z, Is.LessThan(1.495f), "Every compound shape must stay before the wall.");
            AdvanceGrab(new Vector3(2f, 1.3f, -.5f));
            Assert.That(body.position.z, Is.LessThan(0f), "A blocked grab must still move away.");
        }

        [Test]
        public void Grab_StopsAtPedestalAndOtherBody(
            [Values(PrimitiveType.Cylinder, PrimitiveType.Sphere, PrimitiveType.Cube)] PrimitiveType type,
            [Values(false, true)] bool dynamicObstacle)
        {
            GameObject obstacle = Solid(type, new Vector3(2f, 1f, 2f), Vector3.one);
            if (dynamicObstacle)
            {
                Rigidbody obstacleBody = obstacle.AddComponent<Rigidbody>();
                obstacleBody.useGravity = false;
                obstacleBody.constraints = RigidbodyConstraints.FreezeAll;
            }
            XRGrabInteractable grab = PhysicalGrab(PrimitiveType.Cube, new Vector3(2f, 1f, 0f));
            BeginPhysicalGrab(grab);
            AdvanceGrab(new Vector3(2f, 1f, 6f), 5);
            Assert.That(grab.GetComponent<Rigidbody>().position.z, Is.InRange(1.1f, 1.4f));
        }

        [Test]
        public void Grab_StopsAtOffsetPlayerCapsuleWithoutWritingTrackedPose()
        {
            head.localPosition = new Vector3(1f, 1.65f, .5f);
            Call(input, "AlignMotor");
            Vector3 localHead = head.localPosition;
            Quaternion localRotation = head.localRotation;
            XRGrabInteractable grab = PhysicalGrab(PrimitiveType.Cube, new Vector3(1f, 1.3f, 2f));
            BeginPhysicalGrab(grab);
            AdvanceGrab(new Vector3(1f, 1.3f, -2f), 5);
            Assert.That(grab.GetComponent<Rigidbody>().position.z, Is.InRange(.9f, 1.1f));
            AssertVector(head.localPosition, localHead);
            AssertRotation(head.localRotation, localRotation);
        }

        [TestCase("trigger")]
        [TestCase("ignoredPair")]
        public void Grab_RespectsNonCollidingGeometry(string exclusion)
        {
            XRGrabInteractable grab = PhysicalGrab(PrimitiveType.Cube, new Vector3(2f, 1.3f, 0f));
            Collider wall = Solid(PrimitiveType.Cube, new Vector3(2f, 1.3f, 1.5f),
                new Vector3(4f, 4f, .02f)).GetComponent<Collider>();
            if (exclusion == "trigger") wall.isTrigger = true;
            else Physics.IgnoreCollision(grab.GetComponent<Collider>(), wall);
            BeginPhysicalGrab(grab);
            AdvanceGrab(new Vector3(2f, 1.3f, 3f));
            Assert.That(grab.GetComponent<Rigidbody>().position.z, Is.EqualTo(3f).Within(.01f));
        }

        [Test]
        public void Grab_InitialFloorOverlapCannotBeDraggedDeeperButCanEscape()
        {
            Solid(PrimitiveType.Cube, new Vector3(2f, -.1f, 0f), new Vector3(4f, .2f, 4f));
            XRGrabInteractable grab = PhysicalGrab(PrimitiveType.Cube, new Vector3(2f, .19f, 0f));
            BeginPhysicalGrab(grab);
            AdvanceGrab(new Vector3(2f, -1f, 0f));
            Assert.That(grab.GetComponent<Rigidbody>().position.y, Is.EqualTo(.19f).Within(.01f));
            AdvanceGrab(new Vector3(2f, 1.5f, 0f));
            Assert.That(grab.GetComponent<Rigidbody>().position.y, Is.EqualTo(1.5f).Within(.01f));
        }

        [TestCase("paused")]
        [TestCase("settingsPage")]
        [TestCase("modalPage")]
        public void Grab_FixedUpdateCancelsBeforeMovingWhenInteractionBecomesBlocked(string gate)
        {
            XRGrabInteractable grab = PhysicalGrab(PrimitiveType.Cube, new Vector3(2f, 1.3f, 0f));
            BeginPhysicalGrab(grab);
            Vector3 start = grab.GetComponent<Rigidbody>().position;
            Set(input, "grabPosition", start + Vector3.forward * 5f);
            if (gate == "paused") SetProperty(app, "IsPaused", true);
            else Page(gate);
            Call(input, "FixedUpdate");
            Assert.That(Get<Rigidbody>(input, "heldBody"), Is.Null);
            AssertVector(grab.GetComponent<Rigidbody>().position, start);
            Assert.That(grab.GetComponent<Rigidbody>().isKinematic, Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Grab_ExplicitReleaseValidatesPuzzleExactlyOnceEvenBeforePieceUpdate(bool correctSlot)
        {
            ChapterPiece piece = SortingPiece();
            DepthSortingChapter chapter = piece.chapter;
            BeginPhysicalGrab(piece.grab);
            Vector3 drop = chapter.slots[0].position + (correctSlot ? Vector3.zero : Vector3.right * 2f);
            AdvanceGrab(drop);
            Call(input, "ReleaseGrab");
            Call(piece, "Update");
            Call(piece, "Update");
            Assert.That(piece.IsPlaced, Is.EqualTo(correctSlot));
            Assert.That(chapter.Progress, Is.EqualTo(correctSlot ? 1 : 0));
            Assert.That(app.Mistakes, Is.EqualTo(correctSlot ? 0 : 1));
            AssertVector(piece.body.position, correctSlot ? chapter.slots[0].position : piece.home.position);
        }

        [Test]
        public void Grab_CancellationReturnsPuzzlePieceWithoutScoringDrop()
        {
            ChapterPiece piece = SortingPiece();
            BeginPhysicalGrab(piece.grab);
            AdvanceGrab(piece.chapter.slots[0].position);
            Call(input, "OnApplicationFocus", false);
            Call(piece, "Update");
            Assert.That(piece.IsPlaced, Is.False);
            Assert.That(app.Mistakes, Is.Zero);
            Assert.That(piece.chapter.Progress, Is.Zero);
            AssertVector(piece.body.position, piece.home.position);
        }

        [Test]
        public void Grab_PauseFreezeRemainsOwnedByChapterAndResumeRestoresPhysics()
        {
            ChapterPiece piece = SortingPiece();
            BeginPhysicalGrab(piece.grab);
            AdvanceGrab(piece.chapter.slots[0].position);
            SetProperty(app, "IsPaused", true);
            piece.chapter.SetPaused(true);
            Vector3 frozen = piece.body.position;
            Call(input, "FixedUpdate");
            Assert.That(piece.body.isKinematic, Is.True, "Input must not undo the chapter's freeze.");
            AssertVector(piece.body.position, frozen);
            AssertVector(piece.body.position, piece.home.position);
            SetProperty(app, "IsPaused", false);
            piece.chapter.SetPaused(false);
            Call(piece, "Update");
            Assert.That(piece.body.isKinematic, Is.False);
            Assert.That(piece.body.useGravity, Is.True);
            Assert.That(piece.body.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode.ContinuousDynamic));
            Assert.That(app.Mistakes, Is.Zero);
            Assert.That(piece.chapter.Progress, Is.Zero);
        }

        [TestCase("home")]
        [TestCase("gameplay")]
        [TestCase("tutorial")]
        public void UpdateInput_AllowsDesktopExplorationInActiveContexts(string mode)
        {
            Mode(mode);
            Frame(true, new Vector2(10f, 0f), keys: new[] { Key.W, Key.E });
            Tick();
            Assert.That(rig.position.magnitude, Is.GreaterThan(.1f));
            Assert.That(Quaternion.Angle(rig.rotation, Quaternion.identity), Is.GreaterThan(1f));
            Assert.That(Quaternion.Angle(head.localRotation, Quaternion.identity), Is.GreaterThan(1f));
            AssertVector(head.localPosition, new Vector3(0f, 1.65f, 0f));
            if (mode == "home") Assert.That(input.MovementEnabled, Is.False);
        }

        [TestCase("chapterPage")]
        [TestCase("detailPage")]
        [TestCase("creditsPage")]
        [TestCase("finalPage")]
        public void UpdateInput_BrowsingPagesAllowMovementAndLookWithoutGrabbing(string page)
        {
            Mode("menu");
            Page(page);
            Frame(true, Vector2.one * 10f, keys: new[] { Key.W, Key.E });
            Tick();
            Assert.That(rig.position.magnitude, Is.GreaterThan(.1f));
            Assert.That(Quaternion.Angle(rig.rotation, Quaternion.identity), Is.GreaterThan(1f));
            Assert.That(Quaternion.Angle(head.localRotation, Quaternion.identity), Is.GreaterThan(1f));
            Assert.That(Get<XRGrabInteractable>(input, "held"), Is.Null);
        }

        [TestCase("resultPage")]
        [TestCase("tutorialPage")]
        public void UpdateInput_NonBrowsingMenuPagesDoNotAllowExploration(string page)
        {
            Mode("menu");
            Page(page);
            Frame(true, Vector2.one * 10f, keys: new[] { Key.W, Key.E });
            Tick();
            AssertStationary();
        }

        [Test]
        public void UpdateInput_HomeMustBeActiveInHierarchyAndIsNeverAllowedInXR()
        {
            Mode("home");
            app.homePage.transform.SetParent(root.transform, false);
            Assert.That(app.homePage.activeSelf, Is.True);
            Frame(true, Vector2.one * 10f, keys: new[] { Key.W, Key.E });
            Tick();
            AssertStationary();
            app.homePage.transform.SetParent(activeObjects.transform, false);
            Tick(xr: true);
            AssertStationary();
        }

        [Test]
        public void UpdateInput_BlockedContextsCancelAllInput(
            [Values("home", "gameplay", "tutorial")] string mode,
            [Values("paused", "settingsPage", "modalPage", "pausePage", "focus", "legacyTyping", "tmpTyping")] string gate)
        {
            Mode(mode);
            if (gate == "paused") SetProperty(app, "IsPaused", true);
            else if (gate.EndsWith("Page")) Page(gate);
            else if (gate.EndsWith("Typing"))
            {
                GameObject field = Child("Typing", root.transform);
                if (gate == "tmpTyping") field.AddComponent<TMP_InputField>();
                else field.AddComponent<InputField>();
                events.SetSelectedGameObject(field);
            }
            Set(input, "mouseLookCaptured", true);
            Set(input, "nextMoveTime", 99f);
            Set(input, "turnLatched", true);
            Rigidbody body = Child("Held body", root.transform).AddComponent<Rigidbody>();
            body.isKinematic = true;
            Set(input, "heldBody", body);
            Frame(true, Vector2.one * 10f, keys: new[] { Key.W, Key.E });
            Tick(focused: gate != "focus");
            AssertStationary();
            Assert.That(Get<bool>(input, "mouseLookCaptured"), Is.False);
            Assert.That(Get<float>(input, "nextMoveTime"), Is.Zero);
            Assert.That(Get<bool>(input, "turnLatched"), Is.False);
            Assert.That(Get<Rigidbody>(input, "heldBody"), Is.Null);
        }

        [Test]
        public void UpdateInput_InactiveAncestorOverlaysDoNotBlockHome()
        {
            Mode("home");
            foreach (string name in new[] { "settingsPage", "modalPage", "pausePage" })
                Page(name).transform.SetParent(root.transform, false);
            Frame(keys: new[] { Key.W });
            Tick();
            Assert.That(rig.position.z, Is.EqualTo(.15f).Within(Tolerance));
        }

        [TestCase("app")]
        [TestCase("head")]
        [TestCase("root")]
        public void UpdateInput_MissingReferencesCancelCapture(string missing)
        {
            Set(input, "mouseLookCaptured", true);
            if (missing == "head") input.headCamera = null;
            if (missing == "root") input.playerRoot = null;
            Frame(true, Vector2.one * 10f, keys: new[] { Key.W, Key.E });
            Call(input, "UpdateInput", missing == "app" ? null : app, false, true, .1f, 0f);
            AssertStationary();
            Assert.That(Get<bool>(input, "mouseLookCaptured"), Is.False);
        }

        [TestCase("gameplay")]
        [TestCase("tutorial")]
        public void UpdateInput_MovementDisabledStillAllowsLookAndMaintainsGrab(string mode)
        {
            Mode(mode);
            input.MovementEnabled = false;
            XRGrabInteractable grab = SeedGrab();
            Frame(left: true);
            Frame(true, new Vector2(10f, 0f), left: true, keys: new[] { Key.W, Key.E });
            Tick();
            AssertVector(rig.position, Vector3.zero);
            AssertRotation(rig.rotation, Quaternion.identity);
            Assert.That(Quaternion.Angle(head.localRotation, Quaternion.identity), Is.GreaterThan(1f));
            Assert.That(Get<XRGrabInteractable>(input, "held"), Is.SameAs(grab));
            Assert.That(Get<Vector3>(input, "grabPosition").sqrMagnitude, Is.GreaterThan(0f));
        }

        [Test]
        public void UpdateInput_HomeReleasesGrabAndNeverChecksForPreviewAcquisition()
        {
            Mode("home");
            DepthLabInputTestRaycaster raycaster = CreateUI();
            XRGrabInteractable grab = SeedGrab();
            Rigidbody body = grab.GetComponent<Rigidbody>();
            Frame(left: true, overUI: true);
            Tick();
            Assert.That(Get<XRGrabInteractable>(input, "held"), Is.Null);
            Assert.That(body.isKinematic, Is.False);
            Assert.That(body.useGravity, Is.True);
            // A fresh LMB press would query UI before any Physics raycast if UpdateGrab ran.
            Assert.That(raycaster.Calls, Is.Zero);
            Mode("gameplay");
            Frame();
            Frame(left: true, overUI: true);
            Tick();
            Assert.That(raycaster.Calls, Is.GreaterThan(0), "Positive control: gameplay checks acquisition.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MouseLook_CapturesOnOrOffUIAndContinuesAcrossUIUntilRelease(bool startsOverUI)
        {
            CreateUI();
            Frame(true, new Vector2(10f, 0f), overUI: startsOverUI);
            if (startsOverUI) Assert.That(Call(input, "IsPointerOverUI"), Is.True);
            Tick();
            AssertRotation(head.localRotation, Quaternion.Euler(0f, 1.2f, 0f));
            Frame(true, new Vector2(10f, 0f), overUI: !startsOverUI);
            Assert.That(mouse.rightButton.wasPressedThisFrame, Is.False);
            Tick();
            AssertRotation(head.localRotation, Quaternion.Euler(0f, 2.4f, 0f));
            Frame(false, new Vector2(10f, 0f));
            Tick();
            AssertRotation(head.localRotation, Quaternion.Euler(0f, 2.4f, 0f));
            Assert.That(Get<bool>(input, "mouseLookCaptured"), Is.False);
        }

        [TestCase("blocked")]
        [TestCase("focus")]
        [TestCase("pause")]
        [TestCase("reset")]
        [TestCase("disable")]
        public void MouseLook_CancellationRequiresFreshPressRatherThanContinuedHold(string cancellation)
        {
            Frame(true, new Vector2(10f, 0f));
            Tick();
            Assert.That(Get<bool>(input, "mouseLookCaptured"), Is.True);
            Frame(true);
            if (cancellation == "blocked")
            {
                Page("settingsPage");
                Tick();
                app.settingsPage.SetActive(false);
            }
            else if (cancellation == "focus") Call(input, "OnApplicationFocus", false);
            else if (cancellation == "pause") Call(input, "OnApplicationPause", true);
            else if (cancellation == "reset") input.ResetPose();
            else Call(input, "OnDisable");
            Quaternion before = head.localRotation;
            Assert.That(Get<bool>(input, "mouseLookCaptured"), Is.False);
            Call(input, "OnApplicationFocus", true);
            Call(input, "OnApplicationPause", false);
            Frame(true, new Vector2(10f, 0f));
            Tick();
            AssertRotation(head.localRotation, before);
            Frame();
            Tick();
            Frame(true, new Vector2(10f, 0f));
            Tick();
            Assert.That(Quaternion.Angle(head.localRotation, before), Is.GreaterThan(1f));
        }

        [Test]
        public void UpdateInput_XRDoesNotApplyDesktopKeysOrMouseToHead()
        {
            input.MovementEnabled = false;
            Quaternion before = Quaternion.Euler(10f, 20f, 5f);
            head.localRotation = before;
            Vector3 local = head.localPosition;
            Set(input, "mouseLookCaptured", true);
            Frame(true, Vector2.one * 100f, keys: new[] { Key.W, Key.D, Key.Q, Key.E });
            Tick(xr: true);
            AssertRotation(head.localRotation, before);
            AssertVector(head.localPosition, local);
            AssertVector(rig.position, Vector3.zero);
            AssertRotation(rig.rotation, Quaternion.identity);
            Assert.That(Get<bool>(input, "mouseLookCaptured"), Is.False);
        }

        [Test]
        public void Tutorial_ClampedLookDoesNotReportControllersButActualLocalRotationDoes()
        {
            TutorialStep(1);
            head.localRotation = Quaternion.Euler(80f, 0f, 0f);
            input.ResetPose();
            Frame(true, new Vector2(0f, -10f));
            Tick();
            Assert.That(tutorial.Ready, Is.False);
            Frame(true, new Vector2(0f, 10f));
            Tick();
            Assert.That(tutorial.Ready, Is.True);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(5)]
        public void Tutorial_BlockedMovementAndRootTurningAloneCannotSatisfyPoseSteps(int step)
        {
            rig.position = new Vector3(4f, 0f, 0f);
            head.localPosition += Vector3.forward;
            input.ResetPose();
            Solid(PrimitiveType.Cube, new Vector3(4.4f, 1.5f, 0f), new Vector3(.2f, 3f, 20f));
            for (int i = 0; i < 5; i++) Move(Vector2.right, false, .1f, i * .1f);
            TutorialStep(step);
            Frame(keys: new[] { Key.D, Key.E });
            Tick();
            Call(tutorial, "Update");
            Assert.That(tutorial.Ready, Is.False);
            AssertVector(head.localPosition, new Vector3(0f, 1.65f, 1f));
            if (step == 5)
            {
                Move(Vector2.up, false, .3f, 1f);
                Call(tutorial, "Update");
                Assert.That(tutorial.Ready, Is.True, "Actual translation must still satisfy locomotion.");
            }
        }

        private void TutorialStep(int step)
        {
            Mode("tutorial");
            Set(tutorial, "begun", true);
            SetProperty(tutorial, "Step", step);
            SetProperty(tutorial, "Ready", false);
            Call(tutorial, "CaptureStepBaseline");
        }

        private void Mode(string mode)
        {
            SetProperty(app, "IsPlaying", mode == "gameplay");
            SetProperty(app, "IsTutorial", mode == "tutorial");
            input.MovementEnabled = mode != "home";
            if (mode == "home") Page("homePage");
            else if (app.homePage != null) app.homePage.SetActive(false);
        }

        private GameObject Page(string name)
        {
            GameObject page = Child(name, activeObjects.transform);
            typeof(DepthLabApp).GetField(name).SetValue(app, page);
            return page;
        }

        private XRGrabInteractable SeedGrab()
        {
            GameObject item = Child("Held object", activeObjects.transform);
            item.SetActive(false);
            item.AddComponent<BoxCollider>().enabled = false;
            Rigidbody body = item.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            XRGrabInteractable grab = item.AddComponent<XRGrabInteractable>();
            grab.interactionManager = root.AddComponent<XRInteractionManager>();
            item.SetActive(true);
            Set(input, "held", grab);
            Set(input, "heldBody", body);
            Set(input, "heldWasKinematic", false);
            Set(input, "heldUsedGravity", true);
            Set(input, "grabDistance", 2f);
            return grab;
        }

        private GameObject Solid(PrimitiveType type, Vector3 position, Vector3 scale)
        {
            GameObject solid = GameObject.CreatePrimitive(type);
            solid.transform.SetParent(activeObjects.transform, false);
            solid.transform.SetPositionAndRotation(position, Quaternion.identity);
            solid.transform.localScale = scale;
            Physics.SyncTransforms();
            return solid;
        }

        private XRGrabInteractable PhysicalGrab(PrimitiveType type, Vector3 position)
        {
            GameObject item = Solid(type, position, Vector3.one * .4f);
            item.SetActive(false);
            Rigidbody body = item.AddComponent<Rigidbody>();
            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            XRGrabInteractable grab = item.AddComponent<XRGrabInteractable>();
            XRInteractionManager manager = root.GetComponent<XRInteractionManager>();
            grab.interactionManager = manager != null ? manager : root.AddComponent<XRInteractionManager>();
            item.SetActive(true);
            Physics.SyncTransforms();
            return grab;
        }

        private void BeginPhysicalGrab(XRGrabInteractable grab)
        {
            Physics.SyncTransforms();
            Call(input, "BeginGrab", grab, grab.transform.position - Vector3.forward * .2f, 2f);
            Assert.That(Get<XRGrabInteractable>(input, "held"), Is.SameAs(grab));
        }

        private void AdvanceGrab(Vector3 destination, int ticks = 1)
        {
            Set(input, "grabPosition", destination);
            for (int i = 0; i < ticks; i++)
            {
                Call(input, "FixedUpdate");
                physicsScene.GetPhysicsScene().Simulate(.02f);
            }
            Physics.SyncTransforms();
        }

        private ChapterPiece SortingPiece()
        {
            DepthSortingChapter chapter = Child("Sorting", activeObjects.transform).AddComponent<DepthSortingChapter>();
            SetProperty(app, "ActiveChapter", 0);
            app.chapters = new DepthLabChapter[] { chapter };
            app.hudText = Child("Inactive HUD text", root.transform).AddComponent<TextMeshPro>();
            XRGrabInteractable grab = PhysicalGrab(PrimitiveType.Cube, new Vector3(2f, 1.3f, 0f));
            grab.transform.SetParent(chapter.transform, true);
            ChapterPiece piece = grab.gameObject.AddComponent<ChapterPiece>();
            piece.chapter = chapter;
            piece.body = grab.GetComponent<Rigidbody>();
            piece.grab = grab;
            piece.home = Child("Home", chapter.transform).transform;
            piece.home.position = grab.transform.position;
            Transform slot = Child("Slot", chapter.transform).transform;
            slot.position = new Vector3(2f, 1.3f, 2f);
            chapter.pieces = new[] { piece };
            chapter.slots = new[] { slot };
            chapter.interactables = new[] { grab };
            piece.ResetPiece();
            return piece;
        }

        private DepthLabInputTestRaycaster CreateUI()
        {
            GameObject canvas = Child("Test UI", activeObjects.transform);
            canvas.AddComponent<Canvas>();
            raycaster = canvas.AddComponent<DepthLabInputTestRaycaster>();
            Call(raycaster, "OnEnable");
            return raycaster;
        }

        private void Frame(bool right = false, Vector2 delta = default, bool left = false,
            bool overUI = false, params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.QueueStateEvent(mouse, new MouseState
            {
                position = Vector2.one * (overUI ? 30000f : 20000f),
                delta = delta
            }.WithButton(MouseButton.Right, right).WithButton(MouseButton.Left, left));
            InputSystem.Update();
            keyboard.MakeCurrent();
            mouse.MakeCurrent();
        }

        private void Tick(bool xr = false, bool focused = true) =>
            Call(input, "UpdateInput", app, xr, focused, .1f, 0f);

        private void Move(Vector2 value, bool xr, float dt, float time) => Call(input, "Move", value, xr, dt, time);
        private void Turn(float value, bool xr, float dt) => Call(input, "Turn", value, xr, dt);

        private void AssertStationary()
        {
            AssertVector(rig.position, Vector3.zero);
            AssertRotation(rig.rotation, Quaternion.identity);
            AssertRotation(head.localRotation, Quaternion.identity);
        }

        private static GameObject Child(string name, Transform parent)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void AssertVector(Vector3 actual, Vector3 expected) =>
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(Tolerance));

        private static void AssertRotation(Quaternion actual, Quaternion expected) =>
            Assert.That(Quaternion.Angle(actual, expected), Is.LessThan(.05f));

        private static PropertyInfo Property(System.Type type, string name) =>
            type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

        private static void SetProperty(object target, string name, object value) =>
            Property(target.GetType(), name).SetValue(target, value);

        private static FieldInfo Field(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
        private static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target);

        private static object Call(object target, string name, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            return method.Invoke(target, arguments);
        }
    }

    // Deterministic UI hit testing without a rendered canvas, input module, or scene physics.
    public sealed class DepthLabInputTestRaycaster : BaseRaycaster
    {
        public int Calls { get; private set; }
        public override Camera eventCamera => null;

        public override void Raycast(PointerEventData eventData, List<RaycastResult> results)
        {
            Calls++;
            if (eventData.position.x > 25000f)
                results.Add(new RaycastResult { gameObject = gameObject, module = this });
        }
    }
}
