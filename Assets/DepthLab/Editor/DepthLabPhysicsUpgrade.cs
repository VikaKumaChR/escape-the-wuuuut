using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DepthLab.Editor
{
    public static class DepthLabPhysicsUpgrade
    {
        [MenuItem("DEPTHLAB/Repair Physical Collisions")]
        public static void Apply()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Stop Play Mode before repairing the authored scene.");
            var app = UnityEngine.Object.FindAnyObjectByType<DepthLabApp>();
            if (app == null || app.input == null || app.playerRoot == null)
                throw new InvalidOperationException("The authored DEPTHLAB scene and player rig are required.");
            ApplyTo(app);
            EditorSceneManager.MarkSceneDirty(app.gameObject.scene);
            EditorSceneManager.SaveScene(app.gameObject.scene);
        }

        public static void ApplyTo(DepthLabApp app)
        {
            CharacterController motor = app.playerRoot.GetComponent<CharacterController>();
            if (motor == null)
                motor = Undo.AddComponent<CharacterController>(app.playerRoot.gameObject);
            Undo.RecordObject(motor, "Configure player collision");
            motor.height = 1.65f;
            motor.radius = .22f;
            motor.center = new Vector3(0f, .825f, 0f);
            motor.skinWidth = .015f;
            motor.stepOffset = .08f;
            motor.minMoveDistance = 0f;
            motor.slopeLimit = 45f;
            motor.enabled = true;
            Undo.RecordObject(app.input, "Assign player collision");
            app.input.motor = motor;
            DepthLabPlayerSafety safety = app.GetComponent<DepthLabPlayerSafety>();
            if (safety == null)
                safety = Undo.AddComponent<DepthLabPlayerSafety>(app.gameObject);
            safety.app = app;
            EditorUtility.SetDirty(safety);
            EditorUtility.SetDirty(motor);
            EditorUtility.SetDirty(app.input);

            // Unity's primitive cylinder has a capsule, whose rounded cap does not match a plinth.
            foreach (CapsuleCollider capsule in app.GetComponentsInChildren<CapsuleCollider>(true))
            {
                MeshFilter mesh = capsule.GetComponent<MeshFilter>();
                if (!IsAuthoredPlinth(capsule.transform) || mesh == null ||
                    mesh.sharedMesh == null || mesh.sharedMesh.name != "Cylinder")
                    continue;
                GameObject target = capsule.gameObject;
                MeshCollider collider = Undo.AddComponent<MeshCollider>(target);
                collider.sharedMesh = mesh.sharedMesh;
                collider.convex = true;
                collider.sharedMaterial = capsule.sharedMaterial;
                collider.isTrigger = capsule.isTrigger;
                collider.enabled = capsule.enabled;
                collider.contactOffset = capsule.contactOffset;
                collider.includeLayers = capsule.includeLayers;
                collider.excludeLayers = capsule.excludeLayers;
                collider.layerOverridePriority = capsule.layerOverridePriority;
                foreach (MonoBehaviour behaviour in app.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null)
                        continue;
                    var serialized = new SerializedObject(behaviour);
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference &&
                            property.objectReferenceValue == capsule)
                            property.objectReferenceValue = collider;
                    serialized.ApplyModifiedProperties();
                }
                Undo.DestroyObjectImmediate(capsule);
                EditorUtility.SetDirty(collider);
            }

            foreach (Transform target in app.GetComponentsInChildren<Transform>(true))
            {
                switch (target.name)
                {
                    case "Structural rib":
                    case "Sliding exit left panel":
                    case "Sliding exit right panel":
                    case "Exit left jamb":
                    case "Exit right jamb":
                    case "Gallery backplate":
                    case "Ponzo cube A 0.5m":
                    case "Ponzo cube B 0.5m":
                    case "Far wall":
                    case "Depth rib":
                        EnsureBox(target.gameObject);
                        break;
                    case "Launch rim":
                    case "Socket status ring":
                    case "Menu plinth":
                    case "Organic trunk":
                    case "Emitter housing":
                        EnsureMesh(target.gameObject);
                        break;
                    case "Receiver target":
                    case "POLISHED METAL\nM 1.00 / R 0.04":
                    case "ROUGH METAL\nM 1.00 / R 0.84":
                    case "MATTE CERAMIC\nM 0.00 / R 0.76":
                        if (target.GetComponent<Collider>() == null)
                            Undo.AddComponent<SphereCollider>(target.gameObject);
                        break;
                }
            }

            foreach (DepthLabChapter chapter in app.chapters)
            {
                Renderer back = chapter.transform.Find("Back wall").GetComponent<Renderer>();
                EnsureWall(chapter.transform, "Front wall", new Vector3(0f, 1.8f, -7f),
                    new Vector3(12f, 3.6f, .2f), back.sharedMaterial);
                if (chapter is LabChapter lab && lab.exitDoor != null)
                {
                    Transform sign = lab.exitDoor.transform.Find("Exit use panel");
                    if (sign == null)
                        throw new InvalidOperationException("The authored exit sign is missing.");
                    Undo.RecordObject(lab.exitDoor, "Connect physical exit status");
                    lab.exitDoor.statusLabel = sign.Find("Exit label").GetComponent<TMPro.TMP_Text>();
                    lab.exitDoor.conditionLabel = sign.Find("Exit condition").GetComponent<TMPro.TMP_Text>();
                    Undo.RecordObject(lab.exitButton.gameObject, "Use physical doorway instead of exit button");
                    lab.exitButton.gameObject.SetActive(false);
                    EditorUtility.SetDirty(lab.exitDoor);
                }
            }
            Transform arrival = app.menuSpace.transform;
            Material wall = arrival.Find("Far wall").GetComponent<Renderer>().sharedMaterial;
            EnsureWall(arrival, "Arrival left wall", new Vector3(-6.5f, 3f, 1.5f),
                new Vector3(.2f, 7f, 13f), wall);
            EnsureWall(arrival, "Arrival right wall", new Vector3(6.5f, 3f, 1.5f),
                new Vector3(.2f, 7f, 13f), wall);
            EnsureWall(arrival, "Arrival front wall", new Vector3(0f, 3f, -5f),
                new Vector3(13f, 7f, .2f), wall);

            foreach (XRGrabInteractable grab in app.GetComponentsInChildren<XRGrabInteractable>(true))
            {
                // The anchored mirror is rotation-only; movable props must obey solver contacts.
                if (!grab.trackPosition)
                    continue;
                Undo.RecordObject(grab, "Enable physical XR grabbing");
                grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
                Rigidbody body = grab.GetComponent<Rigidbody>();
                if (body != null)
                {
                    Undo.RecordObject(body, "Enable continuous prop collisions");
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                    body.interpolation = RigidbodyInterpolation.Interpolate;
                    EditorUtility.SetDirty(body);
                }
                EditorUtility.SetDirty(grab);
            }
            Physics.SyncTransforms();
        }

        private static void EnsureBox(GameObject target)
        {
            if (target.GetComponent<Collider>() == null)
                Undo.AddComponent<BoxCollider>(target);
        }

        private static bool IsAuthoredPlinth(Transform target)
        {
            if (target.parent == null)
                return false;
            if (target.parent.GetComponent<LabChapter>() != null)
                return target.name == "Launch pedestal" || target.name == "Socket plinth" ||
                    target.name == "Mirror pedestal" || target.name == "Receiver pedestal" ||
                    target.name == "Material sample plinth";
            if (target.parent.name == "Tutorial practice station")
                return target.name == "Practice plinth" || target.name == "Practice release target";
            return target.parent.name == "01 - Arrival Laboratory" &&
                target.name == "Central arrival platform";
        }

        private static void EnsureMesh(GameObject target)
        {
            if (target.GetComponent<Collider>() != null)
                return;
            MeshCollider collider = Undo.AddComponent<MeshCollider>(target);
            collider.sharedMesh = target.GetComponent<MeshFilter>().sharedMesh;
            collider.convex = true;
        }

        private static void EnsureWall(Transform parent, string name, Vector3 position,
            Vector3 scale, Material material)
        {
            if (parent.Find(name) != null)
                return;
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            Undo.RegisterCreatedObjectUndo(wall, "Add visible room boundary");
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = position;
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
