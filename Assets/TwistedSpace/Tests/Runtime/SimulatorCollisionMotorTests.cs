using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TwistedSpace.Demo;
using UnityEngine;
using UnityEngine.TestTools;

namespace TwistedSpace.Tests
{
    /// <summary>
    /// Physics regressions independent of hardware and Input System globals. Rig/keyboard integration
    /// additionally needs the simulator prefab and the generated TrackedPoseDriver bindings.
    /// </summary>
    public sealed class SimulatorCollisionMotorTests
    {
        private const int SolidLayer = 31;
        private static readonly Vector3 Start = new Vector3(2000f, 2000f, 2000f);
        private readonly List<GameObject> objects = new List<GameObject>();
        private SimulatorCollisionMotor motor;

        [SetUp]
        public void SetUp()
        {
            GameObject owner = Create("Collision motor");
            owner.SetActive(false);
            motor = owner.AddComponent<SimulatorCollisionMotor>();
            Field("environmentLayers").SetValue(motor, (LayerMask)(1 << SolidLayer));
            Call("EnsureProbe");
        }

        [TearDown]
        public void TearDown()
        {
            CapsuleCollider probe = (CapsuleCollider)Field("probe").GetValue(motor);
            if (probe)
                Object.DestroyImmediate(probe.gameObject);
            for (int i = objects.Count - 1; i >= 0; --i)
                Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        [Test]
        public void LargeMovementCannotTunnelThroughThinWall()
        {
            Box(new Vector3(0f, 1f, 2f), new Vector3(10f, 5f, 0.05f));
            Vector3 end = Move(Start, Vector3.forward * 100f);
            Assert.That(end.z - Start.z, Is.InRange(1.7f, 1.8f));
            Assert.That(end.x, Is.EqualTo(Start.x).Within(0.002f));
        }

        [Test]
        public void DiagonalMovementSlidesAndReverseMovementHasNoBlockedDebt()
        {
            Box(new Vector3(2f, 1f, 0f), new Vector3(0.05f, 5f, 20f));
            Vector3 end = Move(Start, new Vector3(4f, 0f, 3f));
            Assert.That(end.x - Start.x, Is.InRange(1.7f, 1.8f));
            Assert.That(end.z - Start.z, Is.EqualTo(3f).Within(0.01f));
            Vector3 reversed = Move(end, Vector3.left * 0.1f);
            Assert.That(reversed.x, Is.EqualTo(end.x - 0.1f).Within(0.002f));
        }

        [Test]
        public void CornerRemainsFiniteUnderRepeatedPressure()
        {
            Box(new Vector3(2f, 1f, 0f), new Vector3(0.05f, 5f, 20f));
            Box(new Vector3(0f, 1f, 2f), new Vector3(20f, 5f, 0.05f));
            Vector3 end = Start;
            for (int i = 0; i < 100; ++i)
                end = Move(end, new Vector3(1f, 0f, 1f));
            Assert.That(end.x - Start.x, Is.InRange(1.7f, 1.8f));
            Assert.That(end.z - Start.z, Is.InRange(1.7f, 1.8f));
            Assert.That(end.y, Is.EqualTo(Start.y).Within(0.002f));
        }

        [Test]
        public void FloorDoesNotSinkAndVerticalKeyboardMotionRemainsAvailable()
        {
            Box(new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
            Vector3 feet = Start;
            Assert.That(Separate(ref feet), Is.True);
            for (int i = 0; i < 100; ++i)
                feet = Move(feet, Vector3.down);
            Assert.That(feet.y - Start.y, Is.InRange(0.015f, 0.03f));
            Vector3 raised = Move(feet, Vector3.up);
            Assert.That(raised.y, Is.EqualTo(feet.y + 1f).Within(0.002f));
            Assert.That(Move(raised, Vector3.zero), Is.EqualTo(raised));
        }

        [Test]
        public void ClosedDoorBlocksAndMovedOpenDoorAllowsPassage()
        {
            BoxCollider door = Box(new Vector3(0f, 1f, 2f), new Vector3(1.5f, 2.5f, 0.1f));
            Assert.That(Move(Start, Vector3.forward * 4f).z - Start.z, Is.LessThan(1.8f));
            door.transform.position += Vector3.right * 3f;
            Assert.That(Move(Start, Vector3.forward * 4f).z - Start.z,
                Is.EqualTo(4f).Within(0.002f));
        }

        [Test]
        public void MovingDoorDepenetratesStationaryPlayer()
        {
            BoxCollider door = Box(new Vector3(1f, 1f, 0f), new Vector3(0.1f, 3f, 2f));
            Vector3 feet = Start;
            Assert.That(Separate(ref feet), Is.True);
            door.transform.position = Start + new Vector3(0.15f, 1f, 0f);
            Assert.That(Separate(ref feet), Is.True);
            Assert.That(feet.x - Start.x, Is.InRange(-0.15f, -0.09f));
            Vector3 stable = feet;
            Assert.That(Separate(ref feet), Is.True);
            Assert.That(Vector3.Distance(stable, feet), Is.LessThan(0.002f));
        }

        [Test]
        public void TriggersAndOtherLayersDoNotBlockOrPush()
        {
            BoxCollider trigger = Box(new Vector3(0f, 1f, 1f), new Vector3(5f, 5f, 0.1f));
            trigger.isTrigger = true;
            BoxCollider target = Box(new Vector3(0f, 1f, 2f), new Vector3(5f, 5f, 0.1f));
            target.gameObject.layer = 0;
            Assert.That(Move(Start, Vector3.forward * 4f).z - Start.z,
                Is.EqualTo(4f).Within(0.002f));
            Assert.That(((CapsuleCollider)Field("probe").GetValue(motor)).enabled, Is.False);
        }

        [Test]
        public void MissingConfigurationIsInertAndWarnsOnlyOnce()
        {
            motor.gameObject.SetActive(true);
            LogAssert.Expect(LogType.Warning,
                "SimulatorCollisionMotor is inactive: assign xrOrigin, headCamera, deviceSimulator and " +
                "a nonempty environmentLayers mask. Physical HMD poses are never constrained by this motor.");
            Call("SamplePose");
            Call("SamplePose");
            Assert.That(motor.IsSimulationActive, Is.False);
            Assert.That(motor.IsBlocked, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        private GameObject Create(string name)
        {
            GameObject item = new GameObject(name);
            objects.Add(item);
            return item;
        }

        private BoxCollider Box(Vector3 offset, Vector3 size)
        {
            GameObject item = Create("Test solid");
            item.layer = SolidLayer;
            item.transform.position = Start + offset;
            BoxCollider collider = item.AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }

        private Vector3 Move(Vector3 feet, Vector3 motion)
        {
            Physics.SyncTransforms();
            return (Vector3)Call("SweepAndSlide", feet, motion);
        }

        private bool Separate(ref Vector3 feet)
        {
            Physics.SyncTransforms();
            object[] arguments = { feet };
            bool result = (bool)Call("ResolveOverlaps", arguments);
            feet = (Vector3)arguments[0];
            return result;
        }

        private object Call(string name, params object[] arguments) =>
            typeof(SimulatorCollisionMotor).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(motor, arguments);

        private static FieldInfo Field(string name) =>
            typeof(SimulatorCollisionMotor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    }
}
