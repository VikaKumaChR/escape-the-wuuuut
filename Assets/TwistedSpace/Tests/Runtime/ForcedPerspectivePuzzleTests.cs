using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TwistedSpace.Puzzles;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace TwistedSpace.Tests
{
    /// <summary>Tests real transforms, trigger geometry, projected bounds, latched events and cleanup.</summary>
    public sealed class ForcedPerspectivePuzzleTests
    {
        private GameObject root;
        private Camera head;
        private Collider headCollider;
        private Transform lookDirection;
        private PuzzleObservationVolume volume;
        private PerspectivePuzzleTarget[] targets;
        private ForcedPerspectivePuzzle puzzle;
        private Material material;
        private int solvedCount;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Puzzle Test");
            root.SetActive(false);
            root.AddComponent<XRInteractionManager>();
            GameObject headObject = Child("Head");
            head = headObject.AddComponent<Camera>();
            head.nearClipPlane = 0.05f;
            head.aspect = 1f;
            head.fieldOfView = 60f;
            headCollider = headObject.AddComponent<SphereCollider>();
            ((SphereCollider)headCollider).radius = 0.08f;
            headCollider.isTrigger = true;
            headObject.AddComponent<Rigidbody>().isKinematic = true;
            lookDirection = Child("Required Look").transform;
            lookDirection.rotation = Quaternion.Euler(0f, 90f, 0f);

            GameObject volumeObject = Child("Observation");
            BoxCollider trigger = volumeObject.AddComponent<BoxCollider>();
            trigger.size = Vector3.one;
            trigger.isTrigger = true;
            volume = volumeObject.AddComponent<PuzzleObservationVolume>();
            SetField(volume, "playerHeadColliders", new[] { headCollider });

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            material = new Material(shader);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.black);
            targets = new PerspectivePuzzleTarget[3];
            for (int i = 0; i < targets.Length; i++)
            {
                GameObject targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                targetObject.transform.SetParent(root.transform, false);
                float depth = 2f * (i + 1);
                targetObject.transform.localPosition = Vector3.forward * depth;
                targetObject.transform.localScale = Vector3.one * (0.2f * depth);
                Rigidbody body = targetObject.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.isKinematic = true;
                targetObject.AddComponent<XRGrabInteractable>();
                Renderer renderer = targetObject.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                targets[i] = targetObject.AddComponent<PerspectivePuzzleTarget>();
                SetField(targets[i], "targetRenderers", new[] { renderer });
            }

            puzzle = Child("Puzzle").AddComponent<ForcedPerspectivePuzzle>();
            SetField(puzzle, "headCamera", head);
            SetField(puzzle, "requiredLookDirection", lookDirection);
            SetField(puzzle, "observationVolumes", new[] { volume });
            SetField(puzzle, "targets", targets);
            SetField(puzzle, "stableHoldSeconds", 0.05f);
            SetField(puzzle, "successFadeSeconds", 0.05f);
            puzzle.OnPuzzleSolved.AddListener(() => solvedCount++);
            solvedCount = 0;
            root.SetActive(true);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(material);
        }

        [UnityTest]
        public IEnumerator LookDirectionGatesIdenticalProjectionsAndSolveLatchesOnce()
        {
            yield return null;
            yield return new WaitForSeconds(0.1f);
            Assert.That(puzzle.CurrentOverlap, Is.EqualTo(1f).Within(0.001f));
            Assert.That(solvedCount, Is.Zero);
            lookDirection.rotation = Quaternion.identity;
            yield return new WaitForSeconds(0.2f);
            Assert.That(puzzle.IsSolved, Is.True);
            Assert.That(puzzle.Progress, Is.EqualTo(1f));
            Assert.That(solvedCount, Is.EqualTo(1));
            Assert.That(targets[0].GetComponent<Renderer>().sharedMaterial, Is.Not.SameAs(material));
            yield return new WaitForSeconds(0.1f);
            Assert.That(solvedCount, Is.EqualTo(1));
            lookDirection.rotation = Quaternion.Euler(0f, 90f, 0f);
            puzzle.ResetPuzzle();
            yield return null;
            yield return null;
            yield return null;
            Assert.That(puzzle.IsSolved, Is.False);
            Assert.That(targets[0].IsResetPending, Is.False);
            Assert.That(targets[0].GetComponent<Renderer>().sharedMaterial, Is.SameAs(material));
        }

        [UnityTest]
        public IEnumerator RegionCannotBeKeptOccupiedByAnUnassignedCollider()
        {
            yield return null;
            Assert.That(volume.IsHeadInside, Is.True);
            GameObject decoy = Child("Hand Or Prop");
            decoy.AddComponent<SphereCollider>();
            head.transform.position = Vector3.right * 5f;
            lookDirection.rotation = Quaternion.identity;
            Physics.SyncTransforms();
            yield return null;
            yield return new WaitForSeconds(0.1f);
            Assert.That(volume.IsHeadInside, Is.False);
            Assert.That(puzzle.CurrentOverlap, Is.Zero);
            Assert.That(solvedCount, Is.Zero);
            head.transform.position = Vector3.zero;
            headCollider.enabled = false;
            yield return null;
            Assert.That(volume.IsHeadInside, Is.False);
        }

        [UnityTest]
        public IEnumerator AnyInvalidTargetPreventsSuccessAndResetRestoresItsPose()
        {
            yield return null;
            lookDirection.rotation = Quaternion.identity;
            Rigidbody body = targets[2].GetComponent<Rigidbody>();
            body.position = Vector3.back * 2f;
            targets[2].transform.position = body.position;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.1f);
            Assert.That(solvedCount, Is.Zero);
            Assert.That(puzzle.CurrentOverlap, Is.Zero);
            lookDirection.rotation = Quaternion.Euler(0f, 90f, 0f);
            puzzle.ResetPuzzle();
            yield return null;
            yield return null;
            yield return null;
            Assert.That(targets[2].transform.position.z, Is.EqualTo(6f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator ProjectionRejectsNearPlaneCrossingOffscreenAndHiddenRenderer()
        {
            yield return null;
            Renderer renderer = targets[0].GetComponent<Renderer>();
            Renderer[] renderers = { renderer };
            Assert.That(ProjectedBoundsUtility.TryProject(head, renderers, 0.002f, out _), Is.True);
            renderer.enabled = false;
            Assert.That(ProjectedBoundsUtility.TryProject(head, renderers, 0.002f, out _), Is.False);
            renderer.enabled = true;
            targets[0].transform.position = new Vector3(20f, 0f, 2f);
            Assert.That(ProjectedBoundsUtility.TryProject(head, renderers, 0.002f, out _), Is.False);
            targets[0].transform.position = new Vector3(0f, 0f, 0.1f);
            Assert.That(ProjectedBoundsUtility.TryProject(head, renderers, 0.002f, out _), Is.False);
        }

        private GameObject Child(string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            return child;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing serialized field: " + name);
            field.SetValue(target, value);
        }
    }
}
