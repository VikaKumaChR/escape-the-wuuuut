using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DepthLab
{
    // Desktop holds translate without rotating. Sweep the actual compound body, not its visual bounds.
    internal sealed class DepthLabGrabMotion : IDisposable
    {
        private const float Skin = .005f;
        private const float ContactTolerance = .00001f;
        private const int SlideIterations = 6;
        private readonly Rigidbody body;
        private readonly Collider[] shapes;
        private readonly List<Contact> contacts = new List<Contact>();
        private readonly List<Contact> destinationContacts = new List<Contact>();
        private Collider[] overlaps = new Collider[16];
        private readonly CharacterController[] players;
        private RaycastHit[] playerHits = new RaycastHit[16];
        private CapsuleCollider playerProbe;

        private struct Contact
        {
            public Collider shape;
            public Collider obstacle;
            public Vector3 normal;
            public float depth;
        }

        public bool CanSweep { get; }

        public DepthLabGrabMotion(Rigidbody body)
        {
            this.body = body;
            shapes = body.GetComponentsInChildren<Collider>();
            var controllers = new List<CharacterController>();
            foreach (GameObject root in body.gameObject.scene.GetRootGameObjects())
                controllers.AddRange(root.GetComponentsInChildren<CharacterController>(true));
            players = controllers.ToArray();
            foreach (Collider shape in shapes)
            {
                if (!IsShape(shape)) continue;
                // PhysX rigidbody sweeps require primitives or convex meshes.
                if (!(shape is BoxCollider || shape is SphereCollider || shape is CapsuleCollider ||
                    shape is MeshCollider mesh && mesh.convex))
                {
                    CanSweep = false;
                    return;
                }
                CanSweep = true;
            }
        }

        public float PickupLift()
        {
            GatherContacts(Vector3.zero, contacts);
            foreach (Contact contact in contacts)
                if (contact.normal.y > .5f) return .06f;
            foreach (RaycastHit hit in body.SweepTestAll(Vector3.down, Skin * 4f, QueryTriggerInteraction.Ignore))
                if (CollidesWithBody(hit.collider) && hit.normal.y > .5f) return .06f;
            return 0f;
        }

        public Vector3 Constrain(Vector3 destination)
        {
            Vector3 start = body.position;
            Vector3 delta = destination - start;
            float maxDistance = delta.magnitude;
            if (!CanSweep || !body.detectCollisions || maxDistance < ContactTolerance) return start;
            bool hasShape = false;
            foreach (Collider shape in shapes)
                if (IsShape(shape)) { hasShape = true; break; }
            if (!hasShape) return start;
            GatherContacts(Vector3.zero, contacts);
            Vector3 safeDelta = Vector3.zero;
            for (int iteration = 0; iteration < SlideIterations; iteration++)
            {
                // A resting penetration blocks only its inward component, not lifting or sliding.
                for (int pass = 0; pass < SlideIterations; pass++)
                    foreach (Contact contact in contacts)
                    {
                        float inward = Vector3.Dot(delta, contact.normal);
                        if (inward < 0f) delta -= contact.normal * inward;
                    }
                delta = Vector3.ClampMagnitude(delta, maxDistance);
                bool escaping = true;
                foreach (Contact contact in contacts)
                    if (Vector3.Dot(delta, contact.normal) < -ContactTolerance) { escaping = false; break; }
                float distance = delta.magnitude;
                if (!escaping || distance < ContactTolerance) break;

                Vector3 direction = delta / distance;
                float travel = distance;
                Vector3 normal = Vector3.zero;
                foreach (RaycastHit hit in body.SweepTestAll(direction, distance + Skin, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider is CharacterController player && Array.IndexOf(players, player) >= 0) continue;
                    if (!CollidesWithBody(hit.collider) || IsInitialContact(hit, direction)) continue;
                    LimitTravel(hit, ref travel, ref normal);
                }
                SweepPlayers(direction, distance, ref travel, ref normal);
                Vector3 step = direction * travel;
                if (HasSafeEndpoint(step)) safeDelta = step;
                if (travel >= distance) break;

                // Re-sweep the projected displacement from the actual body pose, never an unswept
                // second MovePosition or a temporary Transform position.
                float blocked = Vector3.Dot(delta - step, normal);
                if (blocked >= -ContactTolerance) break;
                delta -= normal * blocked;
            }
            return start + safeDelta;
        }

        private static void LimitTravel(RaycastHit hit, ref float travel, ref Vector3 normal)
        {
            float allowed = Mathf.Max(0f, hit.distance - Skin);
            if (allowed >= travel) return;
            travel = allowed;
            normal = hit.normal;
        }

        private bool IsInitialContact(RaycastHit hit, Vector3 direction)
        {
            if (hit.distance > ContactTolerance) return false;
            bool touching = false;
            foreach (Contact contact in contacts)
            {
                if (contact.obstacle != hit.collider) continue;
                if (Vector3.Dot(direction, contact.normal) < -ContactTolerance) return false;
                touching = true;
            }
            return touching;
        }

        private bool HasSafeEndpoint(Vector3 delta)
        {
            GatherContacts(delta, destinationContacts);
            foreach (Contact end in destinationContacts)
            {
                float initialDepth = 0f;
                foreach (Contact start in contacts)
                    if (start.shape == end.shape && start.obstacle == end.obstacle)
                        initialDepth = Mathf.Max(initialDepth, start.depth);
                if (end.depth > initialDepth + ContactTolerance) return false;
            }
            return true;
        }

        private void GatherContacts(Vector3 offset, List<Contact> result)
        {
            result.Clear();
            PhysicsScene scene = body.gameObject.scene.GetPhysicsScene();
            foreach (Collider shape in shapes)
            {
                if (!IsShape(shape)) continue;
                PhysicsPose(shape, out Vector3 position, out Quaternion rotation);
                Bounds bounds = shape.bounds;
                Quaternion adjustment = rotation * Quaternion.Inverse(shape.transform.rotation);
                Vector3 x = adjustment * Vector3.right * bounds.extents.x;
                Vector3 y = adjustment * Vector3.up * bounds.extents.y;
                Vector3 z = adjustment * Vector3.forward * bounds.extents.z;
                Vector3 extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                    Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                    Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
                Vector3 center = position + adjustment * (bounds.center - shape.transform.position) + offset;
                int count;
                while ((count = scene.OverlapBox(center, extents + Vector3.one * Skin,
                    overlaps, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) == overlaps.Length)
                    Array.Resize(ref overlaps, overlaps.Length * 2);
                for (int i = 0; i < count; i++)
                {
                    Collider obstacle = overlaps[i];
                    if (obstacle is CharacterController || !CanCollide(shape, obstacle)) continue;
                    PhysicsPose(obstacle, out Vector3 obstaclePosition, out Quaternion obstacleRotation);
                    AddContact(shape, position + offset, rotation, obstacle, obstacle,
                        obstaclePosition, obstacleRotation, result);
                }
            }
            foreach (CharacterController player in players)
            {
                if (!IsPlayerObstacle(player)) continue;
                PlayerCapsule(player, out Vector3 center, out _, out float radius, out float halfLine);
                EnsurePlayerProbe(radius, halfLine);
                playerProbe.enabled = true;
                try
                {
                    foreach (Collider shape in shapes)
                    {
                        if (!IsShape(shape) || !CanCollide(shape, player)) continue;
                        PhysicsPose(shape, out Vector3 position, out Quaternion rotation);
                        AddContact(shape, position + offset, rotation, playerProbe, player,
                            center, player.transform.rotation, result);
                    }
                }
                finally
                {
                    playerProbe.enabled = false;
                }
            }
        }

        private static void AddContact(Collider shape, Vector3 position, Quaternion rotation,
            Collider query, Collider obstacle, Vector3 obstaclePosition, Quaternion obstacleRotation, List<Contact> result)
        {
            if (Physics.ComputePenetration(shape, position, rotation, query, obstaclePosition, obstacleRotation,
                out Vector3 normal, out float depth) && depth > 0f)
                result.Add(new Contact { shape = shape, obstacle = obstacle, normal = normal, depth = depth });
        }

        private static void PhysicsPose(Collider shape, out Vector3 position, out Quaternion rotation)
        {
            position = shape.transform.position;
            rotation = shape.transform.rotation;
            Rigidbody owner = shape.attachedRigidbody;
            if (owner == null) return;
            // Interpolated Transforms can be a physics tick behind Rigidbody sweeps.
            Quaternion adjustment = owner.rotation * Quaternion.Inverse(owner.transform.rotation);
            position = owner.position + adjustment * (position - owner.transform.position);
            rotation = adjustment * rotation;
        }

        private void SweepPlayers(Vector3 direction, float distance, ref float travel, ref Vector3 normal)
        {
            PhysicsScene scene = body.gameObject.scene.GetPhysicsScene();
            // Native rigidbody sweeps can omit CharacterControllers. Reverse-cast their capsule.
            foreach (CharacterController player in players)
            {
                if (!IsPlayerObstacle(player)) continue;
                PlayerCapsule(player, out Vector3 center, out Vector3 axis, out float radius, out float halfLine);
                int count;
                while ((count = scene.CapsuleCast(center - axis, center + axis, radius, -direction,
                    playerHits, distance + Skin, ~0, QueryTriggerInteraction.Ignore)) == playerHits.Length)
                    Array.Resize(ref playerHits, playerHits.Length * 2);
                for (int i = 0; i < count; i++)
                    if (IsShape(playerHits[i].collider) &&
                        CanCollide(playerHits[i].collider, player))
                    {
                        RaycastHit hit = playerHits[i];
                        Vector3 outward = -hit.normal;
                        if (hit.distance <= ContactTolerance)
                        {
                            // An initial-overlap cast normal is synthetic (-cast direction). A slightly
                            // expanded query capsule also supplies a real normal at exact touching.
                            if (PlayerContactNormal(hit.collider, player, center, radius, halfLine, out Vector3 contactNormal))
                            {
                                outward = contactNormal;
                                if (Vector3.Dot(direction, outward) >= -ContactTolerance) continue;
                            }
                        }
                        hit.normal = outward;
                        LimitTravel(hit, ref travel, ref normal);
                    }
            }
        }

        private bool PlayerContactNormal(Collider shape, CharacterController player, Vector3 center,
            float radius, float halfLine, out Vector3 normal)
        {
            foreach (Contact contact in contacts)
                if (contact.shape == shape && contact.obstacle == player)
                {
                    normal = contact.normal;
                    return true;
                }
            EnsurePlayerProbe(radius + Skin, halfLine);
            playerProbe.enabled = true;
            try
            {
                PhysicsPose(shape, out Vector3 position, out Quaternion rotation);
                return Physics.ComputePenetration(shape, position, rotation, playerProbe, center,
                    player.transform.rotation, out normal, out _);
            }
            finally
            {
                playerProbe.enabled = false;
            }
        }

        private bool IsPlayerObstacle(CharacterController player) =>
            player != null && player.enabled && player.gameObject.activeInHierarchy &&
            player.detectCollisions && CollidesWithBody(player);

        private static void PlayerCapsule(CharacterController player, out Vector3 center, out Vector3 axis,
            out float radius, out float halfLine)
        {
            Vector3 scale = player.transform.lossyScale;
            radius = player.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            halfLine = Mathf.Max(0f, player.height * Mathf.Abs(scale.y) * .5f - radius);
            center = player.transform.TransformPoint(player.center);
            axis = player.transform.up * halfLine;
        }

        private bool CollidesWithBody(Collider obstacle)
        {
            foreach (Collider shape in shapes)
                if (IsShape(shape) && CanCollide(shape, obstacle)) return true;
            return false;
        }

        private bool IsShape(Collider shape) =>
            shape != null && shape.enabled && shape.gameObject.activeInHierarchy &&
            !shape.isTrigger && shape.attachedRigidbody == body;

        private bool CanCollide(Collider shape, Collider obstacle)
        {
            if (obstacle == null || obstacle.attachedRigidbody == body || obstacle.isTrigger ||
                Physics.GetIgnoreCollision(shape, obstacle))
                return false;
            bool defaultContact = !Physics.GetIgnoreLayerCollision(shape.gameObject.layer, obstacle.gameObject.layer);
            bool a = LayerDecision(shape, obstacle.gameObject.layer, defaultContact, out bool overrideA);
            bool b = LayerDecision(obstacle, shape.gameObject.layer, defaultContact, out bool overrideB);
            if (!overrideA) return b;
            if (!overrideB) return a;
            if (a == b) return a;
            return shape.layerOverridePriority == obstacle.layerOverridePriority ? false :
                shape.layerOverridePriority > obstacle.layerOverridePriority ? a : b;
        }

        private static bool LayerDecision(Collider collider, int otherLayer, bool defaultContact,
            out bool overridden)
        {
            int include = collider.includeLayers.value;
            int exclude = collider.excludeLayers.value;
            if (collider.attachedRigidbody != null)
            {
                include |= collider.attachedRigidbody.includeLayers.value;
                exclude |= collider.attachedRigidbody.excludeLayers.value;
            }
            int bit = 1 << otherLayer;
            overridden = ((include | exclude) & bit) != 0;
            if ((exclude & bit) != 0) return false;
            return (include & bit) != 0 || defaultContact;
        }

        private void EnsurePlayerProbe(float radius, float halfLine)
        {
            if (playerProbe == null)
            {
                var query = new GameObject("Held object player collision query");
                query.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(query, body.gameObject.scene);
                playerProbe = query.AddComponent<CapsuleCollider>();
                playerProbe.isTrigger = true;
                playerProbe.enabled = false;
            }
            playerProbe.radius = radius;
            playerProbe.height = 2f * (halfLine + radius);
            playerProbe.center = Vector3.zero;
            playerProbe.direction = 1;
        }

        public void Dispose()
        {
            if (playerProbe == null) return;
            playerProbe.enabled = false;
            if (Application.isPlaying) UnityEngine.Object.Destroy(playerProbe.gameObject);
            else UnityEngine.Object.DestroyImmediate(playerProbe.gameObject);
            playerProbe = null;
        }
    }
}
