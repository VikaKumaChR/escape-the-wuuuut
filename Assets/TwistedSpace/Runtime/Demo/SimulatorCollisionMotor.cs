using System;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using InputDevice = UnityEngine.InputSystem.InputDevice;

namespace TwistedSpace.Demo
{
    /// <summary>
    /// Keyboard simulator collision, not physical-HMD locomotion. Assign all references and a
    /// dedicated solid-environment mask before enabling. Place outside the XR Origin hierarchy.
    /// Real room-scale VR needs separate locomotion and comfort occlusion: software cannot stop
    /// a person walking through a physical wall. This component never writes tracked transforms.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(30000)]
    public sealed class SimulatorCollisionMotor : MonoBehaviour
    {
        [SerializeField] private XROrigin xrOrigin;
        [SerializeField] private Camera headCamera;
        [SerializeField] private XRDeviceSimulator deviceSimulator;
        [SerializeField, Tooltip("Only solid walls, floors and moving door leaves; exclude interactables.")]
        private LayerMask environmentLayers;
        [SerializeField, Min(0.01f)] private float bodyHeight = 1.75f;
        [SerializeField, Min(0.01f)] private float eyeHeight = 1.6f;
        [SerializeField, Min(0.01f)] private float bodyRadius = 0.18f;
        [SerializeField, Min(0.001f)] private float skinWidth = 0.02f;
        [SerializeField, Min(0.01f), Tooltip("Larger pose jumps are clipped, not teleported through geometry.")]
        private float maxMovementPerSample = 10f;

        private const float ContactTolerance = 0.0005f;
        private const int SolverIterations = 8;
        private const int MaxQueryResults = 1024;
        private Collider[] overlaps = new Collider[32];
        private RaycastHit[] hits = new RaycastHit[32];
        private readonly Vector3[] contactNormals = new Vector3[SolverIterations];
        private CapsuleCollider probe;
        private TrackedPoseDriver poseDriver;
        private Transform originTransform;
        private XRSimulatedHMD sampledDevice;
        private Vector3 feetPosition;
        private bool hasSample;
        private bool warned;
        private bool warnedSolver;

        /// <summary>True only while the last sample passed every simulator/tracking safety check.</summary>
        public bool IsSimulationActive { get; private set; }

        /// <summary>Whether the latest sample was corrected, including moving-door depenetration.</summary>
        public bool IsBlocked { get; private set; }

        private void OnEnable()
        {
            hasSample = false;
            Application.onBeforeRender += OnBeforeRender;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRender;
            IsSimulationActive = false;
            IsBlocked = false;
            hasSample = false;
            sampledDevice = null;
        }

        private void OnDestroy()
        {
            if (probe)
                Destroy(probe.gameObject);
        }

        private void LateUpdate() => SamplePose();

        // The Input System's before-render update applies TrackedPoseDriver's late pose.
        // Sample again after it, without changing the driver's update mode or tracking data.
        [BeforeRenderOrder(int.MaxValue)]
        private void OnBeforeRender() => SamplePose();

        private void SamplePose()
        {
            if (!isActiveAndEnabled)
                return;
            if (!CanSimulate(out XRSimulatedHMD device, out string reason))
            {
                IsSimulationActive = false;
                IsBlocked = false;
                hasSample = false;
                sampledDevice = null;
                if (!warned)
                {
                    Debug.LogWarning("SimulatorCollisionMotor is inactive: " + reason +
                        " Physical HMD poses are never constrained by this motor.", this);
                    warned = true;
                }
                return;
            }

            warned = false;
            EnsureProbe();
            Physics.SyncTransforms(); // Door coroutines move Transform colliders outside FixedUpdate.
            Vector3 target = headCamera.transform.position - Vector3.up * eyeHeight;
            if (!hasSample || sampledDevice != device)
            {
                feetPosition = target;
                sampledDevice = device;
                hasSample = true;
            }

            Vector3 desired = target - feetPosition;
            Vector3 resolved = feetPosition;
            bool clear = ResolveOverlaps(ref resolved);
            if (clear)
            {
                resolved = SweepAndSlide(resolved, Vector3.ClampMagnitude(desired, maxMovementPerSample));
                clear = ResolveOverlaps(ref resolved);
            }
            if (!clear)
            {
                // A crushed capsule or a saturated query must not admit untested movement.
                // No finite solver can promise separation when there is no capsule-sized free space.
                resolved = feetPosition;
                if (!warnedSolver)
                {
                    Debug.LogWarning("SimulatorCollisionMotor: no clear capsule position could be resolved; " +
                        "movement is held. Check spawn clearance and moving-door crushing.", this);
                    warnedSolver = true;
                }
            }

            Vector3 correction = resolved - target;
            Vector3 correctedOrigin = originTransform.position + correction;
            if (!Finite(resolved) || !Finite(correctedOrigin))
            {
                Debug.LogError("SimulatorCollisionMotor produced a non-finite collision correction; " +
                    "the XR Origin was left unchanged.", this);
                IsSimulationActive = false;
                hasSample = false;
                return;
            }
            originTransform.position = correctedOrigin;
            feetPosition = resolved;
            IsSimulationActive = true;
            IsBlocked = correction.sqrMagnitude > ContactTolerance * ContactTolerance;
        }

        private bool CanSimulate(out XRSimulatedHMD simulated, out string reason)
        {
            simulated = null;
            reason = "assign xrOrigin, headCamera, deviceSimulator and a nonempty environmentLayers mask.";
            if (!xrOrigin || !headCamera || !deviceSimulator || environmentLayers.value == 0)
                return false;
            reason = "the assigned XR Device Simulator must be active and tracking its HMD.";
            if (!deviceSimulator.isActiveAndEnabled || !deviceSimulator.hmdIsTracked ||
                (deviceSimulator.hmdTrackingState & InputTrackingState.Position) == 0)
                return false;
            reason = "a physical XR display is active.";
            if (XRSettings.isDeviceActive)
                return false;
            reason = "exactly one enabled simulated HMD, and no physical HMD, must be present.";
            foreach (InputDevice device in InputSystem.devices)
            {
                if (!(device is XRHMD))
                    continue;
                // Even a disabled hardware HMD makes ownership ambiguous; do not move a real rig.
                if (!(device is XRSimulatedHMD candidate))
                    return false;
                if (!candidate.enabled)
                    continue;
                if (simulated != null)
                    return false;
                simulated = candidate;
            }
            if (simulated == null || !simulated.isTracked.isPressed ||
                (simulated.trackingState.ReadValue() & (int)InputTrackingState.Position) == 0)
                return false;

            originTransform = xrOrigin.Origin ? xrOrigin.Origin.transform : xrOrigin.transform;
            reason = "the camera must belong to the origin, and the motor must be outside its hierarchy.";
            if (!headCamera.transform.IsChildOf(originTransform) || headCamera.transform == originTransform ||
                transform.IsChildOf(originTransform) || originTransform.IsChildOf(transform))
                return false;
            if (!poseDriver || poseDriver.gameObject != headCamera.gameObject)
                poseDriver = headCamera.GetComponent<TrackedPoseDriver>();
            reason = "the camera needs an enabled TrackedPoseDriver bound exclusively to the simulated HMD.";
            if (!poseDriver || !poseDriver.isActiveAndEnabled ||
                poseDriver.trackingType == TrackedPoseDriver.TrackingType.RotationOnly ||
                !UsesOnlyDevice(poseDriver.positionInput.action, simulated))
                return false;

            reason = "capsule dimensions and world positions must be finite and valid.";
            return Finite(bodyHeight) && Finite(bodyRadius) && Finite(eyeHeight) && Finite(skinWidth) &&
                Finite(maxMovementPerSample) && bodyRadius >= 0.01f && bodyHeight >= 2f * bodyRadius &&
                bodyHeight <= 100f &&
                eyeHeight >= bodyRadius && eyeHeight <= bodyHeight && skinWidth >= 0.001f &&
                skinWidth < bodyRadius && maxMovementPerSample > 0f && maxMovementPerSample <= 1000f &&
                Finite(headCamera.transform.position) && Finite(originTransform.position) &&
                headCamera.transform.position.sqrMagnitude < 1e12f &&
                originTransform.position.sqrMagnitude < 1e12f;
        }

        private static bool UsesOnlyDevice(InputAction action, InputDevice device)
        {
            if (action == null || !action.enabled || action.controls.Count == 0)
                return false;
            foreach (var control in action.controls)
                if (control.device != device)
                    return false;
            return action.activeControl == null || action.activeControl.device == device;
        }

        private void EnsureProbe()
        {
            if (!probe)
            {
                GameObject probeObject = new GameObject("Simulator Collision Query Capsule");
                probeObject.hideFlags = HideFlags.HideAndDontSave;
                probe = probeObject.AddComponent<CapsuleCollider>();
                probeObject.layer = 2; // Ignore Raycast; the temporary query is not an interactable.
                probe.isTrigger = true;
                // Enable only during a penetration query, never across a physics simulation step.
                probe.enabled = false;
            }
            probe.direction = 1;
            probe.radius = bodyRadius + skinWidth;
            probe.height = bodyHeight + 2f * skinWidth;
            probe.center = Vector3.up * (bodyHeight * 0.5f);
        }

        private void CapsulePoints(Vector3 feet, out Vector3 bottom, out Vector3 top)
        {
            bottom = feet + Vector3.up * bodyRadius;
            top = feet + Vector3.up * (bodyHeight - bodyRadius);
        }

        private bool IsSolid(Collider collider)
        {
            return collider && collider != probe && !collider.isTrigger &&
                (!originTransform || !collider.transform.IsChildOf(originTransform));
        }

        private int FindOverlaps(Vector3 feet)
        {
            CapsulePoints(feet, out Vector3 bottom, out Vector3 top);
            while (true)
            {
                int count = Physics.OverlapCapsuleNonAlloc(bottom, top, bodyRadius + skinWidth,
                    overlaps, environmentLayers, QueryTriggerInteraction.Ignore);
                if (count < overlaps.Length)
                    return count;
                if (overlaps.Length >= MaxQueryResults)
                    return -1;
                Array.Resize(ref overlaps, overlaps.Length * 2);
            }
        }

        private bool ResolveOverlaps(ref Vector3 feet)
        {
            for (int iteration = 0; iteration <= SolverIterations; ++iteration)
            {
                int count = FindOverlaps(feet);
                if (count < 0)
                    return false;
                float deepest = 0f;
                Vector3 separation = Vector3.zero;
                for (int i = 0; i < count; ++i)
                {
                    Collider other = overlaps[i];
                    if (!IsSolid(other) || !TryPenetration(feet, other,
                        out Vector3 direction, out float distance) || distance <= ContactTolerance)
                        continue;
                    if (!Finite(direction) || !Finite(distance))
                        return false;
                    if (distance > deepest)
                    {
                        deepest = distance;
                        separation = direction * (distance + ContactTolerance);
                    }
                }
                if (deepest == 0f)
                    return true;
                if (iteration == SolverIterations || separation.magnitude > maxMovementPerSample)
                    return false;
                feet += separation;
                if (!Finite(feet))
                    return false;
            }
            return false;
        }

        private bool TryPenetration(Vector3 feet, Collider other, out Vector3 direction, out float distance)
        {
            // Unity needs a live native collider shape for ComputePenetration. Disabled-only probes
            // can report no overlap. The explicit query pose avoids moving any tracked transform.
            probe.enabled = true;
            try
            {
                return Physics.ComputePenetration(probe, feet, Quaternion.identity,
                    other, other.transform.position, other.transform.rotation, out direction, out distance);
            }
            finally
            {
                probe.enabled = false;
            }
        }

        private Vector3 SweepAndSlide(Vector3 feet, Vector3 remaining)
        {
            int planeCount = 0;
            for (int iteration = 0; iteration < SolverIterations; ++iteration)
            {
                float distance = remaining.magnitude;
                if (!Finite(distance) || distance <= ContactTolerance)
                    break;
                Vector3 direction = remaining / distance;
                CapsulePoints(feet, out Vector3 bottom, out Vector3 top);
                int count;
                while (true)
                {
                    count = Physics.CapsuleCastNonAlloc(bottom, top, bodyRadius + skinWidth,
                        direction, hits, distance + ContactTolerance, environmentLayers,
                        QueryTriggerInteraction.Ignore);
                    if (count < hits.Length)
                        break;
                    if (hits.Length >= MaxQueryResults)
                        return feet;
                    Array.Resize(ref hits, hits.Length * 2);
                }

                float nearest = float.PositiveInfinity;
                Vector3 normal = Vector3.zero;
                for (int i = 0; i < count; ++i)
                {
                    RaycastHit hit = hits[i];
                    if (!IsSolid(hit.collider) || !Finite(hit.normal) || !Finite(hit.distance) ||
                        Vector3.Dot(direction, hit.normal) >= -0.00001f || hit.distance >= nearest)
                        continue;
                    nearest = hit.distance;
                    normal = hit.normal;
                }
                if (float.IsPositiveInfinity(nearest))
                    return feet + remaining;

                float travel = Mathf.Clamp(nearest - ContactTolerance, 0f, distance);
                feet += direction * travel;
                remaining -= direction * travel;
                contactNormals[planeCount++] = normal;
                remaining -= normal * Mathf.Min(0f, Vector3.Dot(remaining, normal));
                // Do not let a later slide project motion back into an earlier contact plane.
                for (int i = 0; i < planeCount - 1; ++i)
                {
                    if (Vector3.Dot(remaining, contactNormals[i]) >= -ContactTolerance)
                        continue;
                    Vector3 crease = Vector3.Cross(contactNormals[i], normal).normalized;
                    remaining = crease * Vector3.Dot(remaining, crease);
                }
                for (int i = 0; i < planeCount; ++i)
                    if (Vector3.Dot(remaining, contactNormals[i]) < -ContactTolerance)
                        return feet;
            }
            return feet;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
