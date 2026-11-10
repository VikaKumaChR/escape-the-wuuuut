using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using Object = UnityEngine.Object;

namespace DepthLab.Editor
{
    public static class DepthLabChapterEffectsUpgrade
    {
        private const string RootName = "第一章环境特效";
        private const string OwnershipMarker = "__DepthLabChapterEffectsUpgrade_v1";
        private const string Folder = "Assets/DepthLab/Generated";
        private const string Prefix = "CH01_EffectsV1_";
        private const string LegacyRoot = "Chapter1InteractionVisual";
        private const string LegacyPrefab = "Assets/DepthLab/Interaction/Prefabs/Chapter1InteractionVisual.prefab";
        private const string UndoName = "Author Chapter 1 effects";
        private static readonly Color Cyan = new Color(.05f, .82f, 1f, 1f);
        private static readonly string[] MaterialNames = { "Wall", "Beam", "Ring", "Highlight", "Particle", "Door" };
        private static readonly string[] MeshNames = { "Cylinder", "Ring", "Quad" };

        private sealed class Context
        {
            public DepthLabApp app;
            public DepthSortingChapter chapter;
            public Transform oldRoot;
            public ChapterPiece[] pieces = new ChapterPiece[3];
            public Renderer[][] sources = new Renderer[3][];
            public MeshRenderer[] walls;
            public Vector3[] doorPositions = new Vector3[3];
            public Vector3[] doorScales = new Vector3[3];
            public float[] launchTops = new float[3];
            public float[] targetTops = new float[3];
            public float[] ringScales = new float[3];
            public Shader wallShader, unlitShader, particleShader;
            public AudioClip success, error, grab, release;
            public readonly List<string> fallbacks = new List<string>();
        }

        [MenuItem("DEPTHLAB/创作第一章环境特效")]
        public static void Apply()
        {
            Context c = Validate();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);

            Material wall = MaterialAsset("Wall", c.wallShader);
            wall.SetFloat("_Intensity", .3f);
            wall.SetFloat("_FlowSpeed", .25f);
            wall.SetColor("_BaseColor", new Color(.0314f, .0745f, .1608f, 1f));
            wall.SetColor("_CyanColor", Cyan);
            wall.SetColor("_BlueColor", new Color(.0863f, .4667f, 1f, 1f));
            wall.SetColor("_AccentColor", new Color(.3f, .22f, .5f, 1f));
            EditorUtility.SetDirty(wall);
            Material beam = TransparentMaterial("Beam", c.unlitShader, .045f);
            Material ring = TransparentMaterial("Ring", c.unlitShader, .22f);
            Material highlight = TransparentMaterial("Highlight", c.unlitShader, .13f);
            highlight.SetFloat("_Cull", (float)CullMode.Front);
            Material particle = TransparentMaterial("Particle", c.particleShader, .3f);
            Material door = TransparentMaterial("Door", c.unlitShader, 0f);
            Mesh cylinder = MeshAsset("Cylinder");
            Mesh annulus = MeshAsset("Ring");
            Mesh quad = MeshAsset("Quad");
            BuildCylinder(cylinder);
            BuildRing(annulus);
            BuildQuad(quad);

            if (c.oldRoot != null) Undo.DestroyObjectImmediate(c.oldRoot.gameObject);
            Transform root = Node(RootName, c.chapter.transform, Vector3.zero);
            Node(OwnershipMarker, root, Vector3.zero);
            var effects = new InteractiveSocketVFX[3];
            var rings = new Renderer[3];
            for (int i = 0; i < 3; i++)
            {
                ChapterPiece piece = c.pieces[i];
                Vector3 home = c.chapter.transform.InverseTransformPoint(piece.home.position);
                Vector3 slot = c.chapter.transform.InverseTransformPoint(c.chapter.slots[i].position);
                RenderMesh("发射光束" + i, root, new Vector3(home.x, c.launchTops[i] + .006f, home.z),
                    new Vector3(.37f, .65f, .37f), cylinder, beam);
                RenderMesh("目标光束" + i, root, new Vector3(slot.x, c.targetTops[i] + .006f, slot.z),
                    new Vector3(.46f, .65f, .46f), cylinder, beam);
                rings[i] = RenderMesh("目标薄环" + i, root, new Vector3(slot.x, c.targetTops[i] + .012f, slot.z),
                    new Vector3(c.ringScales[i], 1f, c.ringScales[i]), annulus, ring);
                ParticleSystem snap = Particles("吸附粒子" + i, root, slot, particle, false);
                AudioSource audio = Audio("交互音效" + i, root, home);
                InteractiveSocketVFX effect = piece.GetComponent<InteractiveSocketVFX>();
                if (effect == null) effect = Undo.AddComponent<InteractiveSocketVFX>(piece.gameObject);
                Assign(effect, c, piece, audio);
                effect.sourceRenderers = c.sources[i];
                effect.highlightMaterial = highlight;
                effect.snapParticles = snap;
                effects[i] = effect;
                Dirty(effect);
            }

            // This sibling of the real door uses its frame, without reparenting any original objects.
            Transform doorFrame = Node("出口能量框", root, c.chapter.exitDoor.transform.localPosition);
            doorFrame.localRotation = c.chapter.exitDoor.transform.localRotation;
            var energy = new Renderer[3];
            string[] energyNames = { "左门柱能量", "右门柱能量", "门楣能量" };
            for (int i = 0; i < energy.Length; i++)
                energy[i] = RenderMesh(energyNames[i], doorFrame, c.doorPositions[i], c.doorScales[i], quad, door);
            // Leave renderers enabled: runtime initializes/fades _BaseColor alpha from OpenAmount.

            ParticleSystem dust = Particles("环境微尘", root, new Vector3(0f, 2.85f, 1.1f), particle, true);
            InteractiveSocketVFX controller = Undo.AddComponent<InteractiveSocketVFX>(root.gameObject);
            Assign(controller, c, null, Audio("环境反馈音效", root, Vector3.zero));
            controller.pieceEffects = effects;
            controller.ambientDust = dust;
            controller.doorEnergy = energy;
            controller.targetRings = rings;
            Dirty(controller);

            foreach (MeshRenderer renderer in c.walls)
            {
                Undo.RecordObject(renderer, UndoName);
                renderer.sharedMaterial = wall;
                Dirty(renderer);
            }
            foreach (MonoBehaviour behaviour in c.chapter.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().FullName != "DepthLab.DepthLabInteractionFeedback" ||
                    !behaviour.enabled) continue;
                // The old upgrade removed these gameplay callbacks and forwarded them from its VFX.
                foreach (ChapterPiece piece in c.pieces)
                {
                    Undo.RecordObject(piece.grab, UndoName);
                    EnsureCallback(piece.grab.selectEntered, piece.OnGrabbed);
                    EnsureCallback(piece.grab.selectExited, piece.OnReleased);
                    Dirty(piece.grab);
                }
                Undo.RecordObject(behaviour, UndoName);
                behaviour.enabled = false;
                Dirty(behaviour);
            }
            foreach (Component component in root.GetComponentsInChildren<Component>(true)) Dirty(component);
            EditorUtility.SetDirty(root.gameObject);
            EditorSceneManager.MarkSceneDirty(c.chapter.gameObject.scene);
            Undo.CollapseUndoOperations(undoGroup);
            if (c.fallbacks.Count > 0)
                Debug.LogWarning("Chapter 1 audio fallback: " + string.Join("; ", c.fallbacks) +
                    ". InteractiveSocketVFX must use distinct pitches for these cues.", c.chapter);
            Debug.Log("Chapter 1 effects authored: six beams, three rings, bounded world-space dust, " +
                "jamb energy and three piece effects plus one controller. Scene/assets are dirty; caller must save.", c.chapter);
        }

        private static Context Validate()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling &&
                !EditorApplication.isUpdating, "Apply requires stable Edit Mode, after script imports finish.");
            Scene scene = SceneManager.GetActiveScene();
            Require(scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene),
                "The active scene must be a loaded scene, not a prefab/preview.");
            DepthLabApp[] apps = scene.GetRootGameObjects()
                .SelectMany(go => go.GetComponentsInChildren<DepthLabApp>(true)).ToArray();
            Require(apps.Length == 1, "The active scene must contain exactly one DepthLabApp.");
            var c = new Context { app = apps[0] };
            Require(!EditorUtility.IsPersistent(c.app) && c.app.chapters != null && c.app.chapters.Length > 0 &&
                c.app.chapters[0] is DepthSortingChapter, "app.chapters[0] must be a scene-owned DepthSortingChapter.");
            c.chapter = (DepthSortingChapter)c.app.chapters[0];
            DepthSortingChapter chapter = c.chapter;
            Require(Owned(chapter.transform, c.app.transform) && chapter.chapterIndex == 0,
                "Chapter 1 must belong to this app and have chapterIndex 0.");
            Require(!PrefabUtility.IsPartOfPrefabInstance(chapter.gameObject),
                "Chapter 1 must be an authored scene hierarchy, not a prefab instance.");
            Require(Near(chapter.transform.position, new Vector3(0f, 0f, 24f)) &&
                Near(chapter.transform.lossyScale, Vector3.one) &&
                Quaternion.Angle(chapter.transform.rotation, Quaternion.identity) < .01f,
                "Expected the original Chapter 1 room at world (0,0,24), unrotated and unit scale.");
            Require(c.app.headCamera != null && c.app.headCamera.gameObject.scene == scene &&
                !EditorUtility.IsPersistent(c.app.headCamera), "app.headCamera must belong to the same scene.");
            Require(chapter.pieces != null && chapter.pieces.Length == 3 &&
                chapter.slots != null && chapter.slots.Length == 3 &&
                chapter.slotIndicators != null && chapter.slotIndicators.Length == 3,
                "Chapter 1 requires exactly three pieces, slots and indicators.");
            Require(chapter.emptySlotMaterials != null && chapter.emptySlotMaterials.Length == 3 &&
                chapter.emptySlotMaterials.All(m => m != null) && chapter.filledSlotMaterial != null,
                "Original empty/filled slot materials must be assigned.");
            Require(chapter.exitDoor != null && Owned(chapter.exitDoor.transform, chapter.transform) &&
                chapter.exitDoor.chapter == chapter && chapter.exitDoor.transform.parent == chapter.transform &&
                Near(chapter.exitDoor.transform.localPosition, new Vector3(0f, 0f, 6f)) &&
                Near(chapter.exitDoor.transform.localScale, Vector3.one) &&
                Quaternion.Angle(chapter.exitDoor.transform.localRotation, Quaternion.identity) < .01f,
                "Expected this chapter's exitDoor directly under the room at local (0,0,6).");
            Require(Owned(chapter.exitDoor.leftPanel, chapter.exitDoor.transform) &&
                Owned(chapter.exitDoor.rightPanel, chapter.exitDoor.transform) &&
                chapter.exitDoor.leftPanel != chapter.exitDoor.rightPanel,
                "The exit requires two distinct scene-owned sliding panels.");
            ValidateMesh(chapter.exitDoor.leftPanel.GetComponent<MeshRenderer>());
            ValidateMesh(chapter.exitDoor.rightPanel.GetComponent<MeshRenderer>());
            Require(chapter.exitRenderer != null && Owned(chapter.exitRenderer.transform, chapter.exitDoor.transform),
                "The original door readiness renderer is missing or belongs elsewhere.");
            ValidateDoorFrame(c);
            c.walls = new[]
            {
                BroadSurface(chapter, "Back wall", new Vector3(0f, 1.8f, 7f), new Vector3(12f, 3.6f, .2f)),
                BroadSurface(chapter, "Side wall", new Vector3(-6f, 1.8f, 0f), new Vector3(.2f, 3.6f, 14f)),
                BroadSurface(chapter, "Side wall", new Vector3(6f, 1.8f, 0f), new Vector3(.2f, 3.6f, 14f))
            };
            BroadSurface(chapter, "Lab floor", new Vector3(0f, -.12f, 0f), new Vector3(12f, .24f, 14f));
            Require(chapter.transform.Cast<Transform>().Count(t => t.name == "Back wall") == 1 &&
                chapter.transform.Cast<Transform>().Count(t => t.name == "Side wall") == 2,
                "Broad wall names are ambiguous; only the original one Back wall and two Side walls are supported.");
            var refs = new HashSet<Object>();
            foreach (ChapterPiece piece in chapter.pieces)
            {
                Require(piece != null && Owned(piece.transform, chapter.transform) && refs.Add(piece) &&
                    piece.chapter == chapter && piece.identity >= 0 && piece.identity < 3 &&
                    c.pieces[piece.identity] == null, "Pieces must be distinct, chapter-owned and have identities 0..2.");
                Require(Owned(piece.home, chapter.transform) && refs.Add(piece.home) &&
                    !piece.home.IsChildOf(piece.transform) && piece.body != null && piece.grab != null &&
                    piece.body.gameObject == piece.gameObject && piece.grab.gameObject == piece.gameObject &&
                    piece.GetComponentsInChildren<Collider>(true).Length > 0,
                    "Each piece needs an independent home, its own body/grab, and existing colliders.");
                Vector3 home = chapter.transform.InverseTransformPoint(piece.home.position);
                ValidatePedestalPosition(home, "home");
                Require(Mathf.Abs(home.z + 1.65f) < .01f, "Original launch homes must be at local z=-1.65.");
                Transform[] launchRims = chapter.transform.Cast<Transform>().Where(t => t.name == "Launch rim" &&
                    Mathf.Abs(t.localPosition.x - home.x) < .01f &&
                    Mathf.Abs(t.localPosition.z - home.z) < .01f).ToArray();
                Require(launchRims.Length == 1, "Missing or ambiguous original launch rim.");
                MeshRenderer launchRim = launchRims[0].GetComponent<MeshRenderer>();
                ValidateMesh(launchRim);
                c.launchTops[piece.identity] = MeshBoundsIn(launchRim, chapter.transform).max.y;
                Require(c.launchTops[piece.identity] > .85f && c.launchTops[piece.identity] < 1f,
                    "Launch effects require a support top below the piece.");
                Transform legacy = piece.transform.Find(LegacyRoot);
                Require(legacy == null || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(legacy.gameObject) == LegacyPrefab,
                    "An unrecognized object occupies the legacy visual name; refusing to treat it as an original mesh.");
                Renderer[] sources = piece.GetComponentsInChildren<Renderer>(true)
                    .Where(r => legacy == null || !r.transform.IsChildOf(legacy)).ToArray();
                Require(sources.Length > 0 && sources.All(r => r is MeshRenderer),
                    "Pieces require original static MeshRenderers only; skinned/other renderers are unsupported.");
                foreach (Renderer source in sources) ValidateMesh(source as MeshRenderer);
                c.pieces[piece.identity] = piece;
                c.sources[piece.identity] = sources;
            }
            for (int i = 0; i < 3; i++)
            {
                Transform slot = chapter.slots[i];
                Renderer indicator = chapter.slotIndicators[i];
                Require(Owned(slot, chapter.transform) && refs.Add(slot) && indicator != null &&
                    Owned(indicator.transform, chapter.transform) && refs.Add(indicator),
                    "Slots and indicators must be distinct scene-owned chapter references.");
                Vector3 position = chapter.transform.InverseTransformPoint(slot.position);
                ValidatePedestalPosition(position, "slot");
                Require(!c.pieces.Any(p => slot.IsChildOf(p.transform) || indicator.transform.IsChildOf(p.transform)),
                    "Slots and indicators cannot move with a piece.");
                Require(indicator.name == "Socket status ring" && indicator.transform.parent == chapter.transform,
                    "Expected the original assigned Socket status ring.");
                ValidateMesh(indicator as MeshRenderer);
                Bounds support = MeshBoundsIn((MeshRenderer)indicator, chapter.transform);
                Require(Mathf.Abs(support.center.x - position.x) < .02f &&
                    Mathf.Abs(support.center.z - position.z) < .02f &&
                    support.max.y > .85f && support.max.y < 1f &&
                    support.extents.x > .2f && support.extents.x < .7f &&
                    support.extents.z > .2f && support.extents.z < .7f,
                    "The target rim must remain a bounded pedestal below its slot.");
                c.targetTops[i] = support.max.y;
                c.ringScales[i] = (Mathf.Max(support.extents.x, support.extents.z) + .02f) / .49f;
                Require((position - chapter.transform.InverseTransformPoint(c.pieces[i].home.position)).sqrMagnitude > 1f,
                    "Launch and target effects must occupy separate pedestals.");
                for (int j = 0; j < i; j++)
                    Require((slot.position - chapter.slots[j].position).sqrMagnitude > 1.1f,
                        "Target pedestals overlap.");
            }
            ValidateOwnership(c);
            Require(AssetDatabase.IsValidFolder(Folder), "The existing Generated asset folder is required.");
            c.wallShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/DepthLab/Shaders/DepthLabHolographicWall.shader");
            c.unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            c.particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            Require(c.wallShader != null && c.wallShader.name == "DepthLab/Holographic Wall" &&
                c.wallShader.FindPropertyIndex("_Intensity") >= 0, "The existing holographic wall shader is required.");
            Require(c.unlitShader != null && c.particleShader != null, "Existing URP Unlit and URP Particles/Unlit shaders are required.");
            foreach (string name in MaterialNames) ValidateAsset<Material>(name, ".mat");
            foreach (string name in MeshNames) ValidateAsset<Mesh>(name, ".asset");
            string[] audioPaths = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            c.success = Cue(audioPaths, new[] { "Success", "SnapSuccess", "Snap" }, "Confirm", "success", c);
            c.error = Cue(audioPaths, new[] { "Failure", "Error", "SnapFailure" }, "Confirm", "failure", c);
            c.grab = Cue(audioPaths, new[] { "Grab", "Pickup" }, "Hover", "grab", c);
            c.release = Cue(audioPaths, new[] { "Release", "Drop" }, "Hover", "release", c);
            return c;
        }

        private static void ValidateOwnership(Context c)
        {
            Transform[] matches = c.chapter.GetComponentsInChildren<Transform>(true).Where(t => t.name == RootName).ToArray();
            Require(matches.Length <= 1, "Multiple reserved environment roots exist.");
            c.oldRoot = matches.Length == 1 ? matches[0] : null;
            var expectedEffects = new HashSet<InteractiveSocketVFX>();
            if (c.oldRoot != null)
            {
                Transform root = c.oldRoot;
                InteractiveSocketVFX[] controllers = root.GetComponents<InteractiveSocketVFX>();
                Require(root.parent == c.chapter.transform && !PrefabUtility.IsPartOfPrefabInstance(root.gameObject) &&
                    controllers.Length == 1 && controllers[0].environmentController && controllers[0].piece == null &&
                    controllers[0].chapter == c.chapter && controllers[0].app == c.app,
                    "Reserved environment name belongs to an unrelated object; nothing will be replaced.");
                expectedEffects.Add(controllers[0]);
                var names = new HashSet<string>
                {
                    RootName, OwnershipMarker, "出口能量框", "左门柱能量", "右门柱能量", "门楣能量", "环境微尘", "环境反馈音效"
                };
                for (int i = 0; i < 3; i++)
                    foreach (string prefix in new[] { "发射光束", "目标光束", "目标薄环", "吸附粒子", "交互音效" })
                        names.Add(prefix + i);
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    Require(names.Remove(child.name), "The owned root contains an unknown/duplicate object; refusing to delete it.");
                Require(names.Count == 0 && root.Find(OwnershipMarker) != null,
                    "The environment ownership marker/structure is incomplete; refusing to rebuild.");
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    Require(component != null && (component is Transform || component is MeshFilter ||
                        component is MeshRenderer || component is ParticleSystem || component is ParticleSystemRenderer ||
                        component is AudioSource || component == controllers[0]),
                        "The owned root contains foreign components; refusing to delete them.");
                    if (component is MeshFilter filter)
                        Require(filter.sharedMesh != null && MeshNames.Any(n =>
                            AssetDatabase.GetAssetPath(filter.sharedMesh) == AssetPath(n, ".asset")),
                            "The environment root contains a foreign mesh.");
                    if (component is Renderer renderer)
                        Require(renderer.sharedMaterials.Length == 1 && renderer.sharedMaterial != null &&
                            MaterialNames.Any(n => AssetDatabase.GetAssetPath(renderer.sharedMaterial) == AssetPath(n, ".mat")),
                            "The environment root contains a foreign material.");
                }
                Require(!c.pieces.Any(p => p.transform.IsChildOf(root) || p.home.IsChildOf(root)) &&
                    !c.chapter.slots.Any(t => t.IsChildOf(root)) &&
                    !c.chapter.slotIndicators.Any(r => r.transform.IsChildOf(root)) &&
                    !c.chapter.exitDoor.transform.IsChildOf(root),
                    "Original chapter objects cannot be nested under the replaceable effects root.");
            }
            foreach (ChapterPiece piece in c.pieces)
            {
                InteractiveSocketVFX[] effects = piece.GetComponents<InteractiveSocketVFX>();
                Require(effects.Length <= 1, "A piece has duplicate InteractiveSocketVFX components.");
                if (effects.Length == 0) continue;
                InteractiveSocketVFX effect = effects[0];
                Require(c.oldRoot != null && !effect.environmentController && effect.piece == piece &&
                    effect.chapter == c.chapter && effect.app == c.app && effect.snapParticles != null &&
                    effect.snapParticles.transform.IsChildOf(c.oldRoot),
                    "An existing piece effect is not owned by this authoring pass.");
                expectedEffects.Add(effect);
            }
            Require(c.chapter.GetComponentsInChildren<InteractiveSocketVFX>(true).All(expectedEffects.Contains),
                "Chapter 1 contains an additional/unowned InteractiveSocketVFX controller.");
        }

        private static MeshRenderer BroadSurface(DepthSortingChapter chapter, string name, Vector3 position, Vector3 scale)
        {
            Transform[] matches = chapter.transform.Cast<Transform>().Where(t => t.name == name &&
                Near(t.localPosition, position) && Near(t.localScale, scale) &&
                Quaternion.Angle(t.localRotation, Quaternion.identity) < .01f).ToArray();
            Require(matches.Length == 1, "Missing or ambiguous original broad surface: " + name + " at " + position);
            MeshRenderer renderer = matches[0].GetComponent<MeshRenderer>();
            ValidateMesh(renderer);
            Require(matches[0].GetComponent<Collider>() != null, "Original surface collider missing: " + name);
            return renderer;
        }

        private static void ValidateDoorFrame(Context c)
        {
            Transform door = c.chapter.exitDoor.transform;
            string[] names = { "Exit left jamb", "Exit right jamb" };
            for (int i = 0; i < 3; i++)
            {
                MeshRenderer renderer;
                if (i < 2)
                {
                    Transform[] matches = door.Cast<Transform>().Where(t => t.name == names[i]).ToArray();
                    Require(matches.Length == 1, "Missing or ambiguous door jamb: " + names[i]);
                    renderer = matches[0].GetComponent<MeshRenderer>();
                }
                else renderer = c.chapter.exitRenderer as MeshRenderer;
                ValidateMesh(renderer);
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Bounds meshBounds = filter.sharedMesh.bounds;
                Matrix4x4 toDoor = door.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                Bounds bounds = new Bounds(toDoor.MultiplyPoint3x4(meshBounds.center), Vector3.zero);
                for (int corner = 0; corner < 8; corner++)
                    bounds.Encapsulate(toDoor.MultiplyPoint3x4(meshBounds.center + Vector3.Scale(meshBounds.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f,
                            (corner & 4) == 0 ? -1f : 1f))));
                Require(Finite(bounds.center) && Finite(bounds.size) && bounds.size.x > .01f &&
                    bounds.size.y > .01f && bounds.min.z > -1f && bounds.max.z < 1f,
                    "Door frame mesh has invalid or unsupported bounds.");
                Require(i == 0 ? bounds.max.x <= -.825f : i == 1 ? bounds.min.x >= .825f : bounds.min.y >= 2.68f,
                    "Door energy must remain outside the clear passage.");
                // Derive the front face from the actual mesh, including presentation-upgraded frames.
                c.doorPositions[i] = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z - .012f);
                c.doorScales[i] = i < 2
                    ? new Vector3(Mathf.Min(.065f, bounds.size.x * .5f), bounds.size.y * .96f, 1f)
                    : new Vector3(bounds.size.x * .94f, Mathf.Min(.045f, bounds.size.y * .5f), 1f);
            }
        }

        private static void ValidateMesh(MeshRenderer renderer)
        {
            Require(renderer != null, "Expected an original static MeshRenderer.");
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Require(filter != null && filter.sharedMesh != null && filter.sharedMesh.vertexCount > 0 &&
                filter.sharedMesh.subMeshCount > 0 && renderer.sharedMaterials.Length >= filter.sharedMesh.subMeshCount &&
                renderer.sharedMaterials.All(m => m != null), "Original renderer needs a valid mesh and all submesh materials: " + renderer.name);
        }

        private static Bounds MeshBoundsIn(MeshRenderer renderer, Transform space)
        {
            Bounds mesh = renderer.GetComponent<MeshFilter>().sharedMesh.bounds;
            Matrix4x4 matrix = space.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            Bounds bounds = new Bounds(matrix.MultiplyPoint3x4(mesh.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
                bounds.Encapsulate(matrix.MultiplyPoint3x4(mesh.center + Vector3.Scale(mesh.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f))));
            Require(Finite(bounds.center) && Finite(bounds.size), "Invalid pedestal mesh bounds.");
            return bounds;
        }

        private static void EnsureCallback<T>(UnityEvent<T> callbacks, UnityAction<T> callback)
        {
            for (int i = 0; i < callbacks.GetPersistentEventCount(); i++)
                if (callbacks.GetPersistentTarget(i) == (Object)callback.Target &&
                    callbacks.GetPersistentMethodName(i) == callback.Method.Name)
                {
                    callbacks.SetPersistentListenerState(i, UnityEventCallState.RuntimeOnly);
                    return;
                }
            UnityEventTools.AddPersistentListener(callbacks, callback);
        }

        private static void ValidatePedestalPosition(Vector3 position, string label)
        {
            Require(Finite(position) && Mathf.Abs(position.y - 1.17f) < .01f &&
                Mathf.Abs(position.x) < 5f && position.z > -5f && position.z < 5f,
                "Expected " + label + " at local y=1.17 inside the room.");
            Vector2 guideDelta = new Vector2(position.x + 2f, position.z - .3f);
            Require(guideDelta.sqrMagnitude > 1.2f * 1.2f, "Pedestal effects would intrude on the reserved guide at (-2,1.3,0.3).");
        }

        private static bool Owned(Transform child, Transform owner)
        {
            return child != null && child != owner && !EditorUtility.IsPersistent(child) &&
                child.gameObject.scene == owner.gameObject.scene && child.IsChildOf(owner);
        }

        private static bool Finite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
                !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        private static bool Near(Vector3 a, Vector3 b) => Finite(a) && (a - b).sqrMagnitude < .0001f;

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Chapter 1 effects: " + message);
        }

        private static string AssetPath(string name, string extension) => Folder + "/" + Prefix + name + extension;

        private static void ValidateAsset<T>(string name, string extension) where T : Object
        {
            string path = AssetPath(name, extension);
            Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            Require(asset == null ? string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)) &&
                !File.Exists(path) && !Directory.Exists(path) && !File.Exists(path + ".meta") :
                asset is T && asset.name == Prefix + name, "Generated asset path collision: " + path);
        }

        private static Material MaterialAsset(string name, Shader shader)
        {
            string path = AssetPath(name, ".mat");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = Prefix + name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                Undo.RecordObject(material, UndoName);
                material.shader = shader;
            }
            return material;
        }

        private static Material TransparentMaterial(string name, Shader shader, float alpha)
        {
            Material material = MaterialAsset(name, shader);
            material.SetColor("_BaseColor", new Color(Cyan.r, Cyan.g, Cyan.b, alpha));
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_SrcBlendAlpha")) material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (material.HasProperty("_DstBlendAlpha")) material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_AlphaClip", 0f);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", null);
            if (material.HasProperty("_ColorMode")) material.SetFloat("_ColorMode", 0f);
            if (material.HasProperty("_SoftParticlesEnabled")) material.SetFloat("_SoftParticlesEnabled", 0f);
            if (material.HasProperty("_CameraFadingEnabled")) material.SetFloat("_CameraFadingEnabled", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            foreach (string keyword in new[] { "_ALPHATEST_ON", "_ALPHAPREMULTIPLY_ON", "_ALPHAMODULATE_ON",
                "_SOFTPARTICLES_ON", "_FADING_ON", "_COLOROVERLAY_ON", "_COLORCOLOR_ON", "_COLORADDSUBDIFF_ON" })
                material.DisableKeyword(keyword);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.renderQueue = (int)RenderQueue.Transparent;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh MeshAsset(string name)
        {
            string path = AssetPath(name, ".asset");
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = Prefix + name };
                AssetDatabase.CreateAsset(mesh, path);
            }
            else Undo.RecordObject(mesh, UndoName);
            mesh.Clear();
            return mesh;
        }

        private static void BuildCylinder(Mesh mesh)
        {
            const int segments = 48;
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = radial;
                vertices[i * 2 + 1] = radial + Vector3.up;
                uv[i * 2] = new Vector2((float)i / segments, 0f);
                uv[i * 2 + 1] = new Vector2((float)i / segments, 1f);
                if (i < segments) Strip(triangles, i, i * 2, i * 2 + 1, i * 2 + 2, i * 2 + 3);
            }
            FinishMesh(mesh, vertices, uv, triangles);
        }

        private static void BuildRing(Mesh mesh)
        {
            const int segments = 64;
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = radial * .49f;
                vertices[i * 2 + 1] = radial * .505f;
                uv[i * 2] = new Vector2((float)i / segments, 0f);
                uv[i * 2 + 1] = new Vector2((float)i / segments, 1f);
                if (i < segments) Strip(triangles, i, i * 2, i * 2 + 1, i * 2 + 2, i * 2 + 3);
            }
            FinishMesh(mesh, vertices, uv, triangles);
        }

        private static void Strip(int[] indices, int segment, int a, int b, int c, int d)
        {
            int offset = segment * 6;
            indices[offset] = a; indices[offset + 1] = b; indices[offset + 2] = c;
            indices[offset + 3] = c; indices[offset + 4] = b; indices[offset + 5] = d;
        }

        private static void BuildQuad(Mesh mesh)
        {
            FinishMesh(mesh, new[] { new Vector3(-.5f, -.5f, 0f), new Vector3(-.5f, .5f, 0f),
                new Vector3(.5f, -.5f, 0f), new Vector3(.5f, .5f, 0f) },
                new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.one }, new[] { 0, 1, 2, 2, 1, 3 });
        }

        private static void FinishMesh(Mesh mesh, Vector3[] vertices, Vector2[] uv, int[] triangles)
        {
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
        }

        private static Transform Node(string name, Transform parent, Vector3 position)
        {
            var go = new GameObject(name) { layer = 2 };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            Undo.RegisterCreatedObjectUndo(go, UndoName);
            return go.transform;
        }

        private static MeshRenderer RenderMesh(string name, Transform parent, Vector3 position,
            Vector3 scale, Mesh mesh, Material material)
        {
            Transform node = Node(name, parent, position);
            node.localScale = scale;
            Undo.AddComponent<MeshFilter>(node.gameObject).sharedMesh = mesh;
            MeshRenderer renderer = Undo.AddComponent<MeshRenderer>(node.gameObject);
            ConfigureRenderer(renderer, material);
            return renderer;
        }

        private static void ConfigureRenderer(Renderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        private static ParticleSystem Particles(string name, Transform parent, Vector3 position, Material material, bool ambient)
        {
            Transform node = Node(name, parent, position);
            ParticleSystem system = Undo.AddComponent<ParticleSystem>(node.gameObject);
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = false;
            main.loop = ambient;
            main.duration = ambient ? 10f : .45f;
            main.prewarm = ambient;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.maxParticles = ambient ? 40 : 24;
            main.startLifetime = ambient ? new ParticleSystem.MinMaxCurve(8f, 10f) : new ParticleSystem.MinMaxCurve(.45f);
            main.startSpeed = ambient ? .015f : 0f;
            main.startSize = ambient ? new ParticleSystem.MinMaxCurve(.012f, .025f) : new ParticleSystem.MinMaxCurve(.025f, .045f);
            main.startColor = Color.white;
            main.gravityModifier = 0f;
            main.useUnscaledTime = false;
            var emission = system.emission;
            emission.enabled = ambient;
            emission.rateOverTime = ambient ? 4.5f : 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
            var shape = system.shape;
            shape.enabled = ambient;
            shape.shapeType = ParticleSystemShapeType.Box;
            // Upper room only: leaves the guide and the exit passage visually clear.
            shape.scale = new Vector3(10.4f, .8f, 10.4f);
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(ambient ? .5f : 1f, .15f),
                    new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var collision = system.collision;
            collision.enabled = false;
            var trails = system.trails;
            trails.enabled = false;
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            ConfigureRenderer(renderer, material);
            return system;
        }

        private static AudioSource Audio(string name, Transform parent, Vector3 position)
        {
            AudioSource audio = Undo.AddComponent<AudioSource>(Node(name, parent, position).gameObject);
            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 1f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.minDistance = 1.2f;
            audio.maxDistance = 12f;
            audio.volume = .32f;
            audio.pitch = 1f;
            audio.dopplerLevel = 0f;
            return audio;
        }

        private static AudioClip Cue(string[] paths, string[] preferred, string fallback, string role, Context c)
        {
            foreach (string name in preferred)
                foreach (string path in paths)
                {
                    if (!string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase)) continue;
                    AudioClip candidate = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (candidate != null && candidate.length > 0f && candidate.length <= 2f) return candidate;
                }
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/" + fallback + ".wav");
            Require(clip != null && clip.length > 0f && clip.length <= 2f, "Missing short fallback cue: " + fallback);
            c.fallbacks.Add(role + " → " + fallback + ".wav");
            return clip;
        }

        private static void Assign(InteractiveSocketVFX effect, Context c, ChapterPiece piece, AudioSource audio)
        {
            Undo.RecordObject(effect, UndoName);
            effect.enabled = true;
            effect.piece = piece;
            effect.chapter = c.chapter;
            effect.app = c.app;
            effect.headCamera = c.app.headCamera;
            effect.environmentController = piece == null;
            effect.audioSource = audio;
            effect.successCue = c.success;
            effect.errorCue = c.error;
            effect.grabCue = c.grab;
            effect.releaseCue = c.release;
            effect.sourceRenderers = Array.Empty<Renderer>();
            effect.highlightMaterial = null;
            effect.snapParticles = null;
            effect.pieceEffects = Array.Empty<InteractiveSocketVFX>();
            effect.ambientDust = null;
            effect.doorEnergy = Array.Empty<Renderer>();
            effect.targetRings = Array.Empty<Renderer>();
        }

        private static void Dirty(Object target)
        {
            EditorUtility.SetDirty(target);
            if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
    }
}
