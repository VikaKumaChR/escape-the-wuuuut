using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace TwistedSpace.Puzzles
{
    /// <summary>
    /// Place on the Rigidbody / XRGrabInteractable root. Assign visible renderers explicitly.
    /// Normal XRI direct grabbing remains untouched: configure XRDirectInteractor colliders,
    /// interaction layers and the XR Interaction Manager in the rig as usual.
    /// One target/puzzle must exclusively own these renderers' material slots while glowing.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(XRGrabInteractable))]
    public sealed class PerspectivePuzzleTarget : MonoBehaviour
    {
        [SerializeField] private XRGrabInteractable grabInteractable;
        [SerializeField] private Rigidbody targetBody;
        [SerializeField, Tooltip("All renderers that make up this target's silhouette. Different targets must not share renderers.")]
        private Renderer[] targetRenderers = new Renderer[0];
        [SerializeField, ColorUsage(false, true)] private Color successEmission = new Color(0.3f, 1f, 0.6f);
        [SerializeField, Min(0f)] private float emissionStrength = 2f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private Transform initialParent;
        private Vector3 initialLocalScale;
        private Vector3 initialLinearVelocity;
        private Vector3 initialAngularVelocity;
        private bool initialized;
        private bool configurationValid;
        private bool resetPending;
        private int releasedFrame = -1;
        private RendererVisualState[] visualStates;

        private sealed class RendererVisualState
        {
            public Renderer Renderer;
            public Material[] Originals;
            public Material[] Instances;
            public MaterialPropertyBlock[] OriginalBlocks;
            public MaterialPropertyBlock[] WorkingBlocks;
            public Color[] BaseEmission;
        }

        public bool IsConfigured { get { Initialize(); return configurationValid; } }
        public bool IsResetPending => resetPending;
        public bool IsGrabbed => grabInteractable && grabInteractable.isSelected;

        private void Reset()
        {
            grabInteractable = GetComponent<XRGrabInteractable>();
            targetBody = GetComponent<Rigidbody>();
            targetRenderers = GetComponentsInChildren<Renderer>(true);
        }

        private void Awake() => Initialize();

        private void Initialize()
        {
            if (initialized)
                return;
            initialized = true;
            if (!grabInteractable)
                grabInteractable = GetComponent<XRGrabInteractable>();
            if (!targetBody)
                targetBody = GetComponent<Rigidbody>();

            configurationValid = grabInteractable && targetBody &&
                grabInteractable.gameObject == gameObject && targetBody.gameObject == gameObject &&
                targetRenderers != null && targetRenderers.Length > 0;
            if (configurationValid)
            {
                for (int i = 0; i < targetRenderers.Length; i++)
                {
                    if (!targetRenderers[i] || !targetRenderers[i].transform.IsChildOf(transform))
                        configurationValid = false;
                    for (int j = 0; j < i; j++)
                    {
                        if (targetRenderers[i] == targetRenderers[j])
                            configurationValid = false;
                    }
                }
            }

            if (!configurationValid)
            {
                Debug.LogError("Puzzle target: assign the root Rigidbody/XRGrabInteractable and " +
                    "a nonempty, unique list of renderers belonging to this target.", this);
                return;
            }

            // Capture once, before gameplay; do not recapture when the component is re-enabled.
            initialPosition = targetBody.position;
            initialRotation = targetBody.rotation;
            initialParent = transform.parent;
            initialLocalScale = transform.localScale;
            initialLinearVelocity = targetBody.isKinematic ? Vector3.zero : targetBody.linearVelocity;
            initialAngularVelocity = targetBody.isKinematic ? Vector3.zero : targetBody.angularVelocity;
        }

        public bool TryGetViewportBounds(Camera headCamera, float minimumViewportSize, out Rect bounds)
        {
            bounds = default;
            return IsConfigured && isActiveAndEnabled && !resetPending &&
                grabInteractable && grabInteractable.isActiveAndEnabled && targetBody &&
                ProjectedBoundsUtility.TryProject(headCamera, targetRenderers, minimumViewportSize, out bounds);
        }

        /// <summary>Used at setup to reject shared visual ownership between puzzle targets.</summary>
        public bool SharesRendererWith(PerspectivePuzzleTarget other)
        {
            if (!other || targetRenderers == null || other.targetRenderers == null)
                return false;
            for (int i = 0; i < targetRenderers.Length; i++)
                for (int j = 0; j < other.targetRenderers.Length; j++)
                    if (targetRenderers[i] && targetRenderers[i] == other.targetRenderers[j])
                        return true;
            return false;
        }

        /// <summary>
        /// Restore visuals immediately, queue world pose/scale and original velocities.
        /// NEVER move a selected object or the XR head/rig. Wait for every selector to release
        /// and then a full rendered frame, allowing XRI's deferred detach/throw to finish.
        /// If disabled, the pending reset survives and resumes on enable. Puzzle scoring
        /// rejects this target until restoration actually completes.
        /// </summary>
        public void ResetTarget()
        {
            RestoreVisuals();
            if (!IsConfigured)
                return;
            resetPending = true;
            releasedFrame = -1;
            if (IsGrabbed)
                Debug.LogWarning("Puzzle reset deferred: release this target with every hand. " +
                    "Its pose will be restored after XRI finishes detaching.", this);
            else if (!isActiveAndEnabled || !CanProcessGrab())
                Debug.LogWarning("Puzzle target reset is pending until the target, grab component " +
                    "and its registered XR Interaction Manager are active.", this);
        }

        private bool CanProcessGrab()
        {
            return grabInteractable && grabInteractable.isActiveAndEnabled &&
                grabInteractable.interactionManager && grabInteractable.interactionManager.isActiveAndEnabled &&
                grabInteractable.interactionManager.IsRegistered((IXRInteractable)grabInteractable);
        }

        private void LateUpdate()
        {
            if (!resetPending || !targetBody || !grabInteractable)
                return;
            // A disabled/unregistered grab can retain a pending throw until processing resumes.
            if (IsGrabbed || !CanProcessGrab())
            {
                releasedFrame = -1;
                return;
            }

            if (releasedFrame < 0)
            {
                releasedFrame = Time.frameCount;
                return;
            }
            if (Time.frameCount <= releasedFrame)
                return;

            // XRI may have unparented this object with Retain Transform Parent disabled.
            // Restore its authored parent before local scale, or scaled parents change its size.
            transform.SetParent(initialParent, true);
            targetBody.position = initialPosition;
            targetBody.rotation = initialRotation;
            transform.SetPositionAndRotation(initialPosition, initialRotation);
            transform.localScale = initialLocalScale;
            // Unity rejects velocity assignments to kinematic bodies. Leave XRI's current
            // kinematic/gravity/interpolation settings alone; they belong to the grab system.
            if (!targetBody.isKinematic)
            {
                targetBody.linearVelocity = initialLinearVelocity;
                targetBody.angularVelocity = initialAngularVelocity;
                targetBody.WakeUp();
            }
            resetPending = false;
            releasedFrame = -1;
        }

        /// <summary>0..1 glow weight; allocations occur only when acquiring visual ownership.</summary>
        public void SetSuccessBrightness(float weight)
        {
            if (!IsConfigured || !isActiveAndEnabled)
                return;
            weight = Mathf.Clamp01(weight);
            if (visualStates == null)
            {
                if (weight <= 0f)
                    return;
                AcquireVisuals();
            }
            for (int i = 0; i < visualStates.Length; i++)
            {
                RendererVisualState state = visualStates[i];
                if (!state.Renderer)
                    continue;
                for (int slot = 0; slot < state.Instances.Length; slot++)
                {
                    MaterialPropertyBlock block = state.WorkingBlocks[slot];
                    if (block == null)
                        continue;
                    block.SetColor(EmissionColorId, state.BaseEmission[slot] +
                        successEmission * (emissionStrength * weight));
                    state.Renderer.SetPropertyBlock(block, slot);
                }
            }
        }

        private void AcquireVisuals()
        {
            visualStates = new RendererVisualState[targetRenderers.Length];
            for (int i = 0; i < targetRenderers.Length; i++)
            {
                Renderer renderer = targetRenderers[i];
                RendererVisualState state = new RendererVisualState { Renderer = renderer };
                visualStates[i] = state;
                if (!renderer)
                {
                    state.Instances = new Material[0];
                    continue;
                }
                state.Originals = renderer.sharedMaterials;
                state.Instances = (Material[])state.Originals.Clone();
                int count = state.Originals.Length;
                state.OriginalBlocks = new MaterialPropertyBlock[count];
                state.WorkingBlocks = new MaterialPropertyBlock[count];
                state.BaseEmission = new Color[count];
                bool hasEmission = false;
                for (int slot = 0; slot < count; slot++)
                {
                    Material original = state.Originals[slot];
                    if (!original || !original.HasProperty(EmissionColorId) ||
                        !original.shader.keywordSpace.FindKeyword("_EMISSION").isValid)
                    {
                        Debug.LogWarning("Puzzle glow skipped a material slot: assign a URP/Lit " +
                            "material exposing _EmissionColor and supporting _EMISSION.", renderer);
                        continue;
                    }
                    hasEmission = true;
                    Material instance = new Material(original)
                    {
                        name = original.name + " (Puzzle Glow)",
                        hideFlags = HideFlags.DontSave
                    };
                    // A property block cannot enable a shader keyword. Only our owned material
                    // instances are modified; shared project assets are never changed.
                    instance.EnableKeyword("_EMISSION");
                    state.Instances[slot] = instance;
                    MaterialPropertyBlock saved = new MaterialPropertyBlock();
                    MaterialPropertyBlock working = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(saved, slot);
                    renderer.GetPropertyBlock(working, slot);
                    if (working.isEmpty)
                        renderer.GetPropertyBlock(working);
                    state.OriginalBlocks[slot] = saved;
                    state.WorkingBlocks[slot] = working;
                    state.BaseEmission[slot] = working.HasProperty(EmissionColorId)
                        ? working.GetColor(EmissionColorId) : original.GetColor(EmissionColorId);
                }
                if (hasEmission)
                    renderer.sharedMaterials = state.Instances;
            }
        }

        /// <summary>Restore exact per-slot blocks and original material references; release clones.</summary>
        public void RestoreVisuals()
        {
            if (visualStates == null)
                return;
            for (int i = 0; i < visualStates.Length; i++)
            {
                RendererVisualState state = visualStates[i];
                if (state.Renderer)
                {
                    state.Renderer.sharedMaterials = state.Originals;
                    for (int slot = 0; slot < state.OriginalBlocks.Length; slot++)
                    {
                        MaterialPropertyBlock saved = state.OriginalBlocks[slot];
                        if (saved != null)
                            state.Renderer.SetPropertyBlock(saved.isEmpty ? null : saved, slot);
                    }
                }
                for (int slot = 0; slot < state.Instances.Length; slot++)
                {
                    if (state.Instances[slot] && state.Instances[slot] != state.Originals[slot])
                        Destroy(state.Instances[slot]);
                }
            }
            visualStates = null;
        }

        private void OnDisable()
        {
            RestoreVisuals();
            releasedFrame = -1;
        }

        private void OnDestroy() => RestoreVisuals();
    }
}
