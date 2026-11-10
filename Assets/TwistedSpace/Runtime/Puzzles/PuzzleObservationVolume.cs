using UnityEngine;

namespace TwistedSpace.Puzzles
{
    /// <summary>
    /// A single trigger in a possibly compound observation region. Put one component and one
    /// BoxCollider on each child volume; assign all children to ForcedPerspectivePuzzle.
    /// Only the explicitly assigned head colliders count, never tags, hands or held props.
    /// At least one collider in each head/volume pair needs a Rigidbody (normally kinematic).
    /// Physics layers must allow the pair to interact.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class PuzzleObservationVolume : MonoBehaviour
    {
        [SerializeField, Tooltip("This object's only BoxCollider; Is Trigger must be enabled. Use several child objects for compound areas.")]
        private BoxCollider triggerVolume;

        [SerializeField, Tooltip("Assign only the player's head collider(s). Several head colliders are allowed; never add hands or props.")]
        private Collider[] playerHeadColliders = new Collider[0];

        private bool[] headOverlaps;
        private int lastCheckedFrame = -1;
        private bool configurationValid;
        private bool initialized;

        public bool IsConfigured { get { Initialize(); return configurationValid; } }

        /// <summary>
        /// OR across explicit head colliders. Contacts are geometrically reconciled each frame:
        /// sleeping bodies may stop sending Stay, and disabling can omit Exit entirely.
        /// </summary>
        public bool IsHeadInside
        {
            get
            {
                if (!configurationValid || !isActiveAndEnabled || !triggerVolume ||
                    !triggerVolume.enabled || !triggerVolume.isTrigger ||
                    !triggerVolume.gameObject.activeInHierarchy ||
                    (triggerVolume.attachedRigidbody && !triggerVolume.attachedRigidbody.detectCollisions))
                    return false;

                bool refreshGeometry = lastCheckedFrame != Time.frameCount;
                lastCheckedFrame = Time.frameCount;
                bool inside = false;
                for (int i = 0; i < playerHeadColliders.Length; i++)
                {
                    Collider head = playerHeadColliders[i];
                    if (!head || !head.enabled || !head.gameObject.activeInHierarchy ||
                        (head.attachedRigidbody && !head.attachedRigidbody.detectCollisions))
                    {
                        headOverlaps[i] = false;
                        continue;
                    }

                    if (Physics.GetIgnoreLayerCollision(gameObject.layer, head.gameObject.layer) ||
                        Physics.GetIgnoreCollision(triggerVolume, head))
                    {
                        headOverlaps[i] = false;
                        continue;
                    }

                    // The volume is a BoxCollider, so ComputePenetration supports this pair
                    // without an allocating Overlap query. This also handles enabling inside
                    // a stationary/sleeping head, teleporting out, and Rigidbody-root callbacks
                    // forwarded from a different child trigger in a compound body.
                    if (refreshGeometry)
                        headOverlaps[i] = Physics.ComputePenetration(triggerVolume,
                            triggerVolume.transform.position, triggerVolume.transform.rotation,
                            head, head.transform.position, head.transform.rotation, out _, out _);
                    inside |= headOverlaps[i];
                }

                return inside;
            }
        }

        private void Reset()
        {
            triggerVolume = GetComponent<BoxCollider>();
            triggerVolume.isTrigger = true;
        }

        private void Awake() => Initialize();

        private void Initialize()
        {
            if (initialized)
                return;
            initialized = true;
            if (!triggerVolume)
                triggerVolume = GetComponent<BoxCollider>();

            configurationValid = triggerVolume && triggerVolume.gameObject == gameObject &&
                triggerVolume.isTrigger && GetComponents<Collider>().Length == 1 &&
                playerHeadColliders != null && playerHeadColliders.Length > 0;

            if (configurationValid)
            {
                for (int i = 0; i < playerHeadColliders.Length; i++)
                {
                    Collider head = playerHeadColliders[i];
                    if (!head || head == triggerVolume)
                    {
                        configurationValid = false;
                        break;
                    }

                    if (!triggerVolume.attachedRigidbody && !head.attachedRigidbody)
                    {
                        Debug.LogError("Observation volume: each head/trigger pair needs a Rigidbody. " +
                            "Add a kinematic Rigidbody to the head collider or observation volume.", this);
                        configurationValid = false;
                    }
                }
            }

            if (!configurationValid)
            {
                Debug.LogError("Observation volume: assign explicit head colliders and exactly one " +
                    "trigger BoxCollider on this GameObject. Use separate child components for compound regions.", this);
                return;
            }

            headOverlaps = new bool[playerHeadColliders.Length];
            ClearOccupancy();
        }

        private void OnEnable() => ClearOccupancy();
        private void OnDisable() => ClearOccupancy();

        private void ClearOccupancy()
        {
            lastCheckedFrame = -1;
            if (headOverlaps == null)
                return;
            for (int i = 0; i < headOverlaps.Length; i++)
                headOverlaps[i] = false;
        }

        private void OnTriggerEnter(Collider other) => RecordHead(other, true);
        private void OnTriggerStay(Collider other) => RecordHead(other, true);
        private void OnTriggerExit(Collider other) => RecordHead(other, false);

        private void RecordHead(Collider other, bool inside)
        {
            // Unity can deliver physics messages to disabled behaviours.
            if (!configurationValid || !isActiveAndEnabled)
                return;
            for (int i = 0; i < playerHeadColliders.Length; i++)
            {
                if (other == playerHeadColliders[i])
                {
                    headOverlaps[i] = inside;
                    lastCheckedFrame = -1;
                }
            }
        }
    }
}
