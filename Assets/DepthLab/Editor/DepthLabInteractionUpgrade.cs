using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;

namespace DepthLab.Editor
{
    public static class DepthLabInteractionUpgrade
    {
        private const string Folder = "Assets/DepthLab/Interaction";
        private const string RuntimePath = Folder + "/DepthLabInteractionFeedback.cs";
        private const string PrefabPath = Folder + "/Prefabs/Chapter1InteractionVisual.prefab";
        private const string RuntimeName = "DepthLab.DepthLabInteractionFeedback";
        private const string PendingKey = "DepthLab.InteractionUpgrade.Pending";
        private const string AppKey = "DepthLab.InteractionUpgrade.App";
        private const string SceneKey = "DepthLab.InteractionUpgrade.Scene";
        private const string RootName = "Chapter1InteractionVisual";
        private static readonly Color Cyan = new Color32(0x00, 0xF0, 0xD0, 255);
        private static readonly Color Red = new Color32(0xFF, 0x4F, 0x6B, 255);
        private static readonly Color Violet = new Color32(0x8C, 0x65, 0xFF, 255);

        [MenuItem("DEPTHLAB/Upgrade Chapter 1 Interaction")]
        public static void Apply()
        {
            ApplyTo(UnityEngine.Object.FindAnyObjectByType<DepthLabApp>());
        }

        public static void ApplyTo(DepthLabApp app)
        {
            try
            {
                DepthSortingChapter chapter = Validate(app);
                Type runtime = ResolveRuntime();
                bool changed = !File.Exists(RuntimePath) ||
                    File.ReadAllText(RuntimePath) != RuntimeSource;
                if (changed || runtime == null || EditorApplication.isCompiling)
                {
                    // Capture a durable scene object ID before the script import replaces this domain.
                    if (!EditorSceneManager.SaveScene(app.gameObject.scene))
                        throw new InvalidOperationException("Could not save the scene before generating interaction feedback.");
                    SessionState.SetString(AppKey, GlobalObjectId.GetGlobalObjectIdSlow(app).ToString());
                    SessionState.SetString(SceneKey, app.gameObject.scene.path);
                    SessionState.SetBool(PendingKey, true);
                    Directory.CreateDirectory(Folder);
                    if (changed)
                        File.WriteAllText(RuntimePath, RuntimeSource, new UTF8Encoding(false));
                    AssetDatabase.ImportAsset(RuntimePath, ImportAssetOptions.ForceUpdate);
                    CompilationPipeline.RequestScriptCompilation();
                    Debug.Log("DEPTHLAB interaction source imported. Chapter 1 setup will resume after compilation.");
                    return;
                }
                Complete(app, chapter, runtime);
                ClearPending();
            }
            catch (Exception exception)
            {
                ClearPending();
                Debug.LogException(exception);
            }
        }

        [InitializeOnLoadMethod]
        private static void ResumeAfterReload()
        {
            CompilationPipeline.assemblyCompilationFinished -= CompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += CompilationFinished;
            if (SessionState.GetBool(PendingKey, false))
                EditorApplication.delayCall += Resume;
        }

        private static void CompilationFinished(string assembly, CompilerMessage[] messages)
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            if (messages.Any(message => message.type == CompilerMessageType.Error))
            {
                ClearPending();
                Debug.LogError("DEPTHLAB interaction upgrade cancelled because compilation failed. Fix errors and invoke the menu again.");
            }
        }

        private static void Resume()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Resume;
                return;
            }
            try
            {
                string scenePath = SessionState.GetString(SceneKey, "");
                if (!GlobalObjectId.TryParse(SessionState.GetString(AppKey, ""), out GlobalObjectId id))
                    throw new InvalidOperationException("The pending interaction upgrade has no valid app ID.");
                var app = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as DepthLabApp;
                if (app == null || app.gameObject.scene.path != scenePath)
                    throw new InvalidOperationException("The original saved scene and app must still be loaded. Invoke the menu again there.");
                DepthSortingChapter chapter = Validate(app);
                Type runtime = ResolveRuntime();
                if (runtime == null || !File.Exists(RuntimePath) || File.ReadAllText(RuntimePath) != RuntimeSource)
                    throw new InvalidOperationException("The generated interaction source did not compile as expected. Invoke the menu again after fixing compilation.");
                Complete(app, chapter, runtime);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                ClearPending();
            }
        }

        private static void ClearPending()
        {
            SessionState.EraseBool(PendingKey);
            SessionState.EraseString(AppKey);
            SessionState.EraseString(SceneKey);
        }

        private static Type ResolveRuntime()
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(RuntimeName, false);
                if (type != null && typeof(MonoBehaviour).IsAssignableFrom(type)) return type;
            }
            return null;
        }

        private static DepthSortingChapter Validate(DepthLabApp app)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before upgrading Chapter 1 interaction.");
            if (app == null || !app.gameObject.scene.IsValid() || !app.gameObject.scene.isLoaded ||
                string.IsNullOrEmpty(app.gameObject.scene.path) || EditorUtility.IsPersistent(app))
                throw new InvalidOperationException("A loaded, saved DEPTHLAB scene is required.");
            if (app.chapters == null || app.chapters.Length == 0 ||
                !(app.chapters[0] is DepthSortingChapter chapter) ||
                chapter.gameObject.scene != app.gameObject.scene || !chapter.transform.IsChildOf(app.transform))
                throw new InvalidOperationException("Chapter 0 must be this app's scene-owned DepthSortingChapter.");
            if (chapter.pieces == null || chapter.pieces.Length != 3 ||
                chapter.slots == null || chapter.slots.Length != 3 ||
                chapter.slotIndicators == null || chapter.slotIndicators.Length != 3)
                throw new InvalidOperationException("Chapter 1 must have exactly three pieces, slots and slot rims.");
            var refs = new HashSet<UnityEngine.Object>();
            var identities = new HashSet<int>();
            foreach (ChapterPiece piece in chapter.pieces)
            {
                if (piece == null || !refs.Add(piece) || piece.chapter != chapter ||
                    piece.identity < 0 || piece.identity > 2 || !identities.Add(piece.identity) ||
                    piece.gameObject.scene != app.gameObject.scene || !piece.transform.IsChildOf(chapter.transform) ||
                    piece.home == null || piece.body == null || piece.grab == null ||
                    piece.body.gameObject != piece.gameObject || piece.grab.gameObject != piece.gameObject ||
                    !piece.home.IsChildOf(chapter.transform) || piece.home.gameObject.scene != app.gameObject.scene ||
                    !refs.Add(piece.home) || !refs.Add(piece.body) || !refs.Add(piece.grab))
                    throw new InvalidOperationException("Piece references, ownership, identities 0..2, homes, bodies and grabs must be valid and distinct.");
                Transform existing = piece.transform.Find(RootName);
                if (existing != null && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(existing.gameObject) != PrefabPath)
                    throw new InvalidOperationException("An unrelated object occupies the reserved interaction visual name.");
                MeshFilter[] meshes = OriginalMeshes(piece, existing);
                if (meshes.Length == 0 || meshes.Any(mesh => mesh.sharedMesh == null || !mesh.sharedMesh.isReadable))
                    throw new InvalidOperationException("Every chapter piece needs a readable original MeshFilter sharedMesh.");
            }
            for (int i = 0; i < 3; i++)
            {
                Transform slot = chapter.slots[i];
                Renderer rim = chapter.slotIndicators[i];
                if (slot == null || rim == null || !refs.Add(slot) || !refs.Add(rim) ||
                    slot.gameObject.scene != app.gameObject.scene || rim.gameObject.scene != app.gameObject.scene ||
                    !slot.IsChildOf(chapter.transform) || !rim.transform.IsChildOf(chapter.transform) ||
                    rim.sharedMaterial == null)
                    throw new InvalidOperationException("All three assigned slots and rims must be distinct, scene-owned chapter objects.");
            }
            if (FindFont(app, chapter) == null)
                throw new InvalidOperationException("An existing chapter/app Chinese TMP font is required.");
            if (app.headCamera == null || app.headCamera.gameObject.scene != app.gameObject.scene)
                throw new InvalidOperationException("The app's scene camera is required.");
            return chapter;
        }

        private static TMP_FontAsset FindFont(DepthLabApp app, DepthSortingChapter chapter)
        {
            TMP_Text[] preferred = { chapter.hintLabel, chapter.objectiveLabel, app.hintText,
                app.detailBody, app.tutorialText, app.hudText };
            foreach (TMP_Text text in preferred)
                if (text != null && text.font != null) return text.font;
            foreach (TMP_Text text in app.GetComponentsInChildren<TMP_Text>(true))
                if (text.font != null && text.text.Any(character => character >= '\u4e00' && character <= '\u9fff'))
                    return text.font;
            return null;
        }

        private static MeshFilter[] OriginalMeshes(ChapterPiece piece, Transform visual)
        {
            return piece.GetComponentsInChildren<MeshFilter>(true)
                .Where(mesh => (visual == null || !mesh.transform.IsChildOf(visual)) &&
                    mesh.GetComponent<MeshRenderer>() != null).ToArray();
        }

        private static void Complete(DepthLabApp app, DepthSortingChapter chapter, Type runtime)
        {
            string[] required = { "piece", "viewCamera", "objectRenderers", "outlineRenderers",
                "settleVisual", "slotRim", "scanRing", "scanRenderer", "label", "audioSource",
                "hoverCue", "grabCue", "releaseCue", "successCue", "failureCue", "objectName",
                "successColor", "failureColor", "guidanceColor" };
            if (required.Any(name => runtime.GetField(name, BindingFlags.Instance | BindingFlags.Public) == null))
                throw new InvalidOperationException("The compiled feedback component does not match the generated source.");
            EnsureFolder(Folder);
            EnsureFolder(Folder + "/Audio");
            EnsureFolder(Folder + "/Prefabs");
            EnsureFolder(Folder + "/Meshes");
            EnsureFolder(Folder + "/Materials");
            Material wire = WireMaterial();
            Mesh ringMesh = StoreMesh(Folder + "/Meshes/ScanRing.asset", CreateRing());
            AudioClip[] cues =
            {
                Cue("Hover", new[] { 440f }, .085f, .12f),
                Cue("Grab", new[] { 330f }, .12f, .26f),
                Cue("Release", new[] { 294f }, .13f, .22f),
                Cue("Success", new[] { 440f, 660f }, .14f, .30f),
                Cue("Failure", new[] { 330f, 220f }, .15f, .25f)
            };
            GameObject prefab = VisualPrefab(wire, ringMesh, FindFont(app, chapter));
            foreach (ChapterPiece piece in chapter.pieces)
            {
                Transform root = piece.transform.Find(RootName);
                MeshFilter[] originals = OriginalMeshes(piece, root);
                if (root == null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, piece.transform);
                    Undo.RegisterCreatedObjectUndo(instance, "Add chapter interaction visual");
                    root = instance.transform;
                }
                Undo.RecordObject(root, "Configure interaction visual");
                root.localPosition = Vector3.zero;
                root.localRotation = Quaternion.identity;
                root.localScale = Vector3.one;
                Transform settle = root.Find("InteractionVisual");
                Transform ring = root.Find("ScanRing");
                Transform labelRoot = root.Find("Label");
                if (settle == null || ring == null || labelRoot == null)
                    throw new InvalidOperationException("The interaction prefab is missing its owned children.");
                Undo.RecordObject(settle, "Configure decorative group");
                settle.localPosition = Vector3.zero;
                settle.localRotation = Quaternion.identity;
                settle.localScale = Vector3.one;
                // Never rebuild or reparent a physics mesh; mirror each complete local TRS chain.
                foreach (Transform child in settle)
                    if (child.name.StartsWith("Outline_", StringComparison.Ordinal))
                    {
                        Undo.RecordObject(child.gameObject, "Retire owned outline");
                        child.gameObject.SetActive(false);
                    }
                var outlines = new List<Renderer>();
                for (int index = 0; index < originals.Length; index++)
                {
                    MeshFilter original = originals[index];
                    Transform branch = Child(settle, "Outline_" + index);
                    branch.gameObject.SetActive(true);
                    branch.localPosition = Vector3.zero;
                    branch.localRotation = Quaternion.identity;
                    branch.localScale = Vector3.one;
                    var chain = new List<Transform>();
                    for (Transform node = original.transform; node != piece.transform; node = node.parent)
                        chain.Add(node);
                    chain.Reverse();
                    Transform mapped = branch;
                    for (int depth = 0; depth < chain.Count; depth++)
                    {
                        mapped = Child(mapped, "Local_" + depth);
                        Undo.RecordObject(mapped, "Mirror original mesh transform");
                        mapped.localPosition = chain[depth].localPosition;
                        mapped.localRotation = chain[depth].localRotation;
                        mapped.localScale = chain[depth].localScale;
                    }
                    Transform leaf = Child(mapped, "Wire");
                    leaf.localPosition = Vector3.zero;
                    leaf.localRotation = Quaternion.identity;
                    leaf.localScale = Vector3.one;
                    Mesh mesh = StoreMesh(Folder + "/Meshes/Piece_" + piece.identity + "_" + index + ".asset",
                        EdgeMesh(original.sharedMesh));
                    ConfigureMesh(leaf, mesh, wire);
                    MeshRenderer renderer = leaf.GetComponent<MeshRenderer>();
                    renderer.enabled = false;
                    outlines.Add(renderer);
                    // Older mirrored paths are owned decorations, not original meshes.
                    foreach (Renderer old in branch.GetComponentsInChildren<Renderer>(true))
                        if (old != renderer) old.enabled = false;
                }
                foreach (AudioSource generic in root.GetComponentsInChildren<AudioSource>(true))
                {
                    Undo.RecordObject(generic, "Disable generic prefab audio");
                    generic.enabled = false;
                    generic.playOnAwake = false;
                }
                AudioSource audio = piece.GetComponent<AudioSource>();
                if (audio == null) audio = Undo.AddComponent<AudioSource>(piece.gameObject);
                Undo.RecordObject(audio, "Configure piece interaction audio");
                ConfigureAudio(audio);
                audio.enabled = true;
                Component feedback = piece.GetComponent(runtime);
                if (feedback == null) feedback = Undo.AddComponent(piece.gameObject, runtime);
                Undo.RecordObject(feedback, "Assign chapter interaction feedback");
                var serialized = new SerializedObject(feedback);
                Set(serialized, "piece", piece);
                Set(serialized, "viewCamera", app.headCamera);
                SetArray(serialized, "objectRenderers", originals.Select(mesh => mesh.GetComponent<Renderer>()).ToArray());
                SetArray(serialized, "outlineRenderers", outlines.ToArray());
                Set(serialized, "settleVisual", settle);
                Set(serialized, "slotRim", chapter.slotIndicators[piece.identity]);
                Set(serialized, "scanRing", ring);
                Set(serialized, "scanRenderer", ring.GetComponent<Renderer>());
                Set(serialized, "label", labelRoot.GetComponentInChildren<TMP_Text>(true));
                Set(serialized, "audioSource", audio);
                string[] cueNames = { "hoverCue", "grabCue", "releaseCue", "successCue", "failureCue" };
                for (int i = 0; i < cues.Length; i++) Set(serialized, cueNames[i], cues[i]);
                serialized.FindProperty("objectName").stringValue =
                    piece.identity == 0 ? "蓝色立方体" : piece.identity == 1 ? "紫色球体" : "青色三棱柱";
                serialized.FindProperty("successColor").colorValue = Cyan;
                serialized.FindProperty("failureColor").colorValue = Red;
                serialized.FindProperty("guidanceColor").colorValue = Violet;
                serialized.ApplyModifiedProperties();
                TMP_Text label = labelRoot.GetComponentInChildren<TMP_Text>(true);
                Undo.RecordObject(label, "Assign existing Chinese font");
                label.font = FindFont(app, chapter);
                label.raycastTarget = false;
                labelRoot.gameObject.SetActive(false);
                ring.gameObject.SetActive(false);
                RemovePieceCallbacks(piece.grab.selectEntered, piece, "OnGrabbed");
                RemovePieceCallbacks(piece.grab.selectExited, piece, "OnReleased");
                PrefabUtility.RecordPrefabInstancePropertyModifications(root);
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                    if (component != null)
                    {
                        EditorUtility.SetDirty(component);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    }
                EditorUtility.SetDirty(audio);
                EditorUtility.SetDirty(feedback);
                EditorUtility.SetDirty(piece.grab);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(app.gameObject.scene);
            if (!EditorSceneManager.SaveScene(app.gameObject.scene))
                throw new InvalidOperationException("Interaction assets were saved, but the scene could not be saved.");
            Debug.Log("DEPTHLAB Chapter 1 interaction upgraded: three pieces, reusable visuals and five quiet spatial cues.", app);
        }

        private static void RemovePieceCallbacks(UnityEventBase callbacks, ChapterPiece piece, string method)
        {
            for (int i = callbacks.GetPersistentEventCount() - 1; i >= 0; i--)
                if (callbacks.GetPersistentTarget(i) == piece && callbacks.GetPersistentMethodName(i) == method)
                    UnityEventTools.RemovePersistentListener(callbacks, i);
        }

        private static void Set(SerializedObject target, string field, UnityEngine.Object value)
        {
            target.FindProperty(field).objectReferenceValue = value;
        }

        private static void SetArray(SerializedObject target, string field, Renderer[] values)
        {
            SerializedProperty array = target.FindProperty(field);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static Transform Child(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child == null)
            {
                var created = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(created, "Create interaction decoration");
                child = created.transform;
                child.SetParent(parent, false);
            }
            child.gameObject.layer = 2;
            return child;
        }

        private static void ConfigureAudio(AudioSource source)
        {
            source.spatialBlend = .8f;
            source.playOnAwake = false;
            source.volume = .2f;
            source.loop = false;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = .5f;
            source.maxDistance = 8f;
        }

        private static Material WireMaterial()
        {
            const string path = Folder + "/Materials/InteractionWire.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader == null) throw new InvalidOperationException("An unlit wire shader is required.");
            if (material == null)
            {
                material = new Material(shader) { name = "InteractionWire" };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Cyan);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Cyan);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureMesh(Transform target, Mesh mesh, Material material)
        {
            MeshFilter filter = target.GetComponent<MeshFilter>();
            if (filter == null) filter = Undo.AddComponent<MeshFilter>(target.gameObject);
            MeshRenderer renderer = target.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = Undo.AddComponent<MeshRenderer>(target.gameObject);
            Undo.RecordObject(filter, "Assign interaction mesh");
            Undo.RecordObject(renderer, "Configure unlit decoration");
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            target.gameObject.layer = 2;
        }

        private static GameObject VisualPrefab(Material wire, Mesh ringMesh, TMP_FontAsset font)
        {
            bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            GameObject root = existing ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject(RootName);
            try
            {
                root.layer = 2;
                Child(root.transform, "InteractionVisual");
                Transform ring = Child(root.transform, "ScanRing");
                ConfigureMesh(ring, ringMesh, wire);
                ring.gameObject.SetActive(false);
                Transform label = root.transform.Find("Label");
                if (label == null)
                {
                    var created = new GameObject("Label", typeof(RectTransform), typeof(Canvas));
                    created.transform.SetParent(root.transform, false);
                    label = created.transform;
                }
                label.gameObject.layer = 2;
                Canvas canvas = label.GetComponent<Canvas>();
                if (canvas == null) canvas = label.gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.sortingOrder = 15;
                RectTransform rect = label.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(600f, 150f);
                rect.localScale = Vector3.one * .0015f;
                Transform textNode = label.Find("Text");
                if (textNode == null)
                {
                    var created = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                    created.transform.SetParent(label, false);
                    textNode = created.transform;
                }
                textNode.gameObject.layer = 2;
                var text = textNode.GetComponent<TextMeshProUGUI>();
                text.font = font;
                text.fontSize = 34f;
                text.alignment = TextAlignmentOptions.Center;
                text.color = Cyan;
                text.raycastTarget = false;
                text.text = "抓取 / 对齐 / 松手";
                RectTransform textRect = text.rectTransform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;
                label.gameObject.SetActive(false);
                AudioSource audio = root.GetComponent<AudioSource>();
                if (audio == null) audio = root.AddComponent<AudioSource>();
                ConfigureAudio(audio);
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                if (existing) PrefabUtility.UnloadPrefabContents(root);
                else UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Mesh StoreMesh(string path, Mesh generated)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }
            EditorUtility.CopySerialized(generated, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(generated);
            return existing;
        }

        private static Mesh EdgeMesh(Mesh source)
        {
            Vector3[] original = source.vertices;
            var vertices = new List<Vector3>();
            var welded = new Dictionary<Vector3, int>();
            var map = new int[original.Length];
            for (int i = 0; i < original.Length; i++)
            {
                if (!welded.TryGetValue(original[i], out int index))
                {
                    index = vertices.Count;
                    welded.Add(original[i], index);
                    vertices.Add(source.bounds.center + (original[i] - source.bounds.center) * 1.035f);
                }
                map[i] = index;
            }
            var edges = new HashSet<ulong>();
            var indices = new List<int>();
            Action<int, int> add = (a, b) =>
            {
                a = map[a];
                b = map[b];
                if (a == b) return;
                int low = Math.Min(a, b), high = Math.Max(a, b);
                ulong key = ((ulong)(uint)low << 32) | (uint)high;
                if (edges.Add(key)) { indices.Add(low); indices.Add(high); }
            };
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                int[] triangles = source.GetIndices(submesh);
                MeshTopology topology = source.GetTopology(submesh);
                if (topology == MeshTopology.Triangles)
                    for (int i = 0; i + 2 < triangles.Length; i += 3)
                    {
                        add(triangles[i], triangles[i + 1]);
                        add(triangles[i + 1], triangles[i + 2]);
                        add(triangles[i + 2], triangles[i]);
                    }
                else if (topology == MeshTopology.Quads)
                    for (int i = 0; i + 3 < triangles.Length; i += 4)
                        for (int j = 0; j < 4; j++) add(triangles[i + j], triangles[i + (j + 1) % 4]);
            }
            if (indices.Count == 0) throw new InvalidOperationException("An original piece mesh has no polygon edges.");
            var mesh = new Mesh { name = source.name + "_InteractionEdges", indexFormat =
                vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateRing()
        {
            const int segments = 96;
            var vertices = new Vector3[segments * 2];
            var indices = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = radial * .48f;
                vertices[i * 2 + 1] = radial * .5f;
                int next = (i + 1) % segments * 2, offset = i * 6;
                indices[offset] = i * 2;
                indices[offset + 1] = next;
                indices[offset + 2] = i * 2 + 1;
                indices[offset + 3] = i * 2 + 1;
                indices[offset + 4] = next;
                indices[offset + 5] = next + 1;
            }
            var mesh = new Mesh { name = "Chapter1FlatScanRing" };
            mesh.vertices = vertices;
            mesh.triangles = indices;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static AudioClip Cue(string name, float[] notes, float noteDuration, float gain)
        {
            const int rate = 22050;
            int noteSamples = Mathf.RoundToInt(rate * noteDuration);
            int gap = Mathf.RoundToInt(rate * .035f);
            int samples = noteSamples * notes.Length + gap * (notes.Length - 1);
            byte[] bytes;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + samples * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(rate);
                writer.Write(rate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(samples * 2);
                for (int i = 0; i < samples; i++)
                {
                    int note = i / (noteSamples + gap);
                    int local = i % (noteSamples + gap);
                    float sample = 0f;
                    if (note < notes.Length && local < noteSamples)
                    {
                        float t = (float)local / rate;
                        float u = (float)local / (noteSamples - 1);
                        float envelope = Mathf.Sin(Mathf.PI * u);
                        envelope *= envelope;
                        sample = Mathf.Sin(2f * Mathf.PI * notes[note] * t) * envelope * gain;
                    }
                    writer.Write((short)Mathf.RoundToInt(sample * short.MaxValue));
                }
                bytes = stream.ToArray();
            }
            string path = Folder + "/Audio/" + name + ".wav";
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes))
                File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        // This is deliberately generated outside Editor only when Apply is explicitly invoked.
        private const string RuntimeSource = @"using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DepthLab
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class DepthLabInteractionFeedback : MonoBehaviour
    {
        public ChapterPiece piece;
        public Camera viewCamera;
        public Renderer[] objectRenderers, outlineRenderers;
        public Transform settleVisual;
        public Renderer slotRim;
        public Transform scanRing;
        public Renderer scanRenderer;
        public TMP_Text label;
        public AudioSource audioSource;
        public AudioClip hoverCue, grabCue, releaseCue, successCue, failureCue;
        public string objectName;
        public Color successColor = new Color32(0x00, 0xF0, 0xD0, 255);
        public Color failureColor = new Color32(0xFF, 0x4F, 0x6B, 255);
        public Color guidanceColor = new Color32(0x8C, 0x65, 0xFF, 255);

        private static readonly int Emission = Shader.PropertyToID(""_EmissionColor"");
        private static readonly int BaseColor = Shader.PropertyToID(""_BaseColor"");
        private static readonly int MainColor = Shader.PropertyToID(""_Color"");
        private DepthLabApp app;
        private XRGrabInteractable subscribedGrab;
        private readonly List<Subscription> subscriptions = new List<Subscription>();
        private readonly List<RendererState> rendererStates = new List<RendererState>();
        private RendererState rimState;
        private MaterialPropertyBlock visualBlock;
        private FieldInfo heldField, releaseField;
        private Vector3 settleScale, lastPosition;
        private bool lastHeld, lastPlaced, pendingRelease, wasAllowed, xrHover, hover;
        private int lastMistakes, lastRetries, lastHints;
        private float effectUntil, guidanceUntil, pendingUntil;
        private bool successEffect;

        private sealed class Subscription
        {
            public EventInfo info;
            public Delegate callback;
        }

        private sealed class RendererState
        {
            public Renderer renderer;
            public Material[] materials;
            public MaterialPropertyBlock[] original, working;
            public Color[] originalEmissions;
            public bool active;

            public void Capture()
            {
                if (renderer == null || active) return;
                materials = renderer.sharedMaterials;
                original = new MaterialPropertyBlock[materials.Length];
                working = new MaterialPropertyBlock[materials.Length];
                originalEmissions = new Color[materials.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    original[i] = new MaterialPropertyBlock();
                    working[i] = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(original[i], i);
                    renderer.GetPropertyBlock(working[i], i);
                    if (working[i].isEmpty) renderer.GetPropertyBlock(working[i]);
                    Material material = materials[i];
                    originalEmissions[i] = working[i].HasProperty(Emission)
                        ? working[i].GetColor(Emission)
                        : material != null && material.HasProperty(Emission) ? material.GetColor(Emission) : Color.black;
                }
                active = true;
            }

            public void Tint(Color color, float strength, bool unlit)
            {
                if (renderer == null) return;
                Capture();
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = renderer.sharedMaterial;
                    // Chapter.Begin/PieceReleased may change a rim material while its effect is active.
                    if (materials.Length == 1 && material != materials[i])
                    {
                        materials[i] = material;
                        originalEmissions[i] = original[i].HasProperty(Emission)
                            ? original[i].GetColor(Emission)
                            : material != null && material.HasProperty(Emission) ? material.GetColor(Emission) : Color.black;
                    }
                    MaterialPropertyBlock block = working[i];
                    if (materials[i] != null && materials[i].HasProperty(Emission))
                        block.SetColor(Emission, originalEmissions[i] + color * strength);
                    if (unlit)
                    {
                        block.SetColor(BaseColor, color);
                        block.SetColor(MainColor, color);
                    }
                    renderer.SetPropertyBlock(block, i);
                }
            }

            public void Restore()
            {
                if (!active) return;
                if (renderer != null)
                    for (int i = 0; i < original.Length; i++)
                        renderer.SetPropertyBlock(original[i].isEmpty ? null : original[i], i);
                active = false;
            }
        }

        private void Awake()
        {
            if (piece == null) piece = GetComponent<ChapterPiece>();
            visualBlock = new MaterialPropertyBlock();
            if (settleVisual != null) settleScale = settleVisual.localScale;
            if (piece != null)
            {
                heldField = piece.GetType().GetField(""held"", BindingFlags.Instance | BindingFlags.NonPublic);
                releaseField = piece.GetType().GetField(""releasePending"", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            if (objectRenderers != null)
                foreach (Renderer renderer in objectRenderers)
                    if (renderer != null) rendererStates.Add(new RendererState { renderer = renderer });
            if (slotRim != null) rimState = new RendererState { renderer = slotRim };
        }

        private void OnEnable()
        {
            app = piece != null && piece.chapter != null
                ? piece.chapter.GetComponentInParent<DepthLabApp>() : null;
            if (app == null) app = DepthLabApp.Instance;
            if (viewCamera == null && app != null) viewCamera = app.headCamera;
            subscribedGrab = piece != null ? piece.grab : null;
            if (subscribedGrab != null)
            {
                subscribedGrab.hoverEntered.AddListener(HoverEntered);
                subscribedGrab.hoverExited.AddListener(HoverExited);
                subscribedGrab.selectEntered.AddListener(Selected);
                subscribedGrab.selectExited.AddListener(Deselected);
            }
            Subscribe(""Grabbed"", ""EventGrab"");
            Subscribe(""Released"", ""EventRelease"");
            Subscribe(""Reset"", ""EventReset"");
            Subscribe(""ResetOccurred"", ""EventReset"");
            Subscribe(""ResetPerformed"", ""EventReset"");
            Snapshot();
            wasAllowed = Allowed;
            ClearVisuals();
        }

        private void OnDisable()
        {
            if (subscribedGrab != null)
            {
                subscribedGrab.hoverEntered.RemoveListener(HoverEntered);
                subscribedGrab.hoverExited.RemoveListener(HoverExited);
                subscribedGrab.selectEntered.RemoveListener(Selected);
                subscribedGrab.selectExited.RemoveListener(Deselected);
            }
            foreach (Subscription subscription in subscriptions)
                if (piece != null) subscription.info.RemoveEventHandler(piece, subscription.callback);
            subscriptions.Clear();
            subscribedGrab = null;
            ClearVisuals();
            if (audioSource != null) audioSource.Stop();
        }

        private void Subscribe(string eventName, string callbackName)
        {
            if (piece == null) return;
            EventInfo info = piece.GetType().GetEvent(eventName, BindingFlags.Instance | BindingFlags.Public);
            if (info == null) return;
            Type handler = info.EventHandlerType;
            if (handler != typeof(Action) && handler != typeof(Action<ChapterPiece>)) return;
            Type[] arguments = handler == typeof(Action) ? Type.EmptyTypes : new[] { typeof(ChapterPiece) };
            MethodInfo method = GetType().GetMethod(callbackName, BindingFlags.Instance | BindingFlags.NonPublic,
                null, arguments, null);
            if (method == null) return;
            Delegate callback = Delegate.CreateDelegate(handler, this, method);
            info.AddEventHandler(piece, callback);
            subscriptions.Add(new Subscription { info = info, callback = callback });
        }

        private bool Allowed => piece != null && piece.chapter != null && piece.chapter.InteractionAllowed &&
            app != null && app.IsPlaying && !app.IsPaused && app.ActiveChapter == 0;

        private bool Held => piece != null && !piece.IsPlaced &&
            ((piece.grab != null && piece.grab.isSelected) ||
             (piece.body != null && piece.body.isKinematic) ||
             (heldField != null && heldField.FieldType == typeof(bool) && (bool)heldField.GetValue(piece)));

        private bool HasPersistent(UnityEventBase callbacks, string method)
        {
            for (int i = 0; i < callbacks.GetPersistentEventCount(); i++)
                if (callbacks.GetPersistentTarget(i) == piece && callbacks.GetPersistentMethodName(i) == method)
                    return true;
            return false;
        }

        private void Selected(SelectEnterEventArgs args)
        {
            if (!Allowed || piece.IsPlaced) return;
            if (!HasPersistent(piece.grab.selectEntered, ""OnGrabbed"")) piece.NotifyGrab();
            BeginGrab();
        }

        private void Deselected(SelectExitEventArgs args)
        {
            if (piece == null || !Allowed || args.isCanceled || piece.IsPlaced) return;
            if (piece.grab != null && piece.grab.isSelected) return;
            if (!HasPersistent(piece.grab.selectExited, ""OnReleased"")) piece.NotifyRelease();
            EndGrab();
        }

        private void HoverEntered(HoverEnterEventArgs args) { xrHover = true; }
        private void HoverExited(HoverExitEventArgs args)
        {
            xrHover = piece != null && piece.grab != null && piece.grab.isHovered;
        }
        private void EventGrab() { if (Allowed) BeginGrab(); }
        private void EventGrab(ChapterPiece target) { if (target == piece) EventGrab(); }
        private void EventRelease() { if (Allowed) EndGrab(); }
        private void EventRelease(ChapterPiece target) { if (target == piece) EventRelease(); }
        private void EventReset()
        {
            // Outcomes are deferred to LateUpdate so pause/retry resets cannot play failure cues.
            xrHover = false;
        }
        private void EventReset(ChapterPiece target) { if (target == piece) EventReset(); }

        private void BeginGrab()
        {
            if (lastHeld) return;
            lastHeld = true;
            pendingRelease = false;
            effectUntil = 0f;
            Play(grabCue);
        }

        private void EndGrab()
        {
            if (pendingRelease) return;
            lastHeld = false;
            pendingRelease = true;
            pendingUntil = Time.unscaledTime + 1.2f;
            Play(releaseCue);
        }

        private void Snapshot()
        {
            lastPlaced = piece != null && piece.IsPlaced;
            lastHeld = Held;
            lastPosition = piece != null ? piece.transform.position : transform.position;
            lastMistakes = app != null ? app.Mistakes : 0;
            lastRetries = app != null ? app.Retries : 0;
            lastHints = app != null ? app.Hints : 0;
        }

        private void LateUpdate()
        {
            if (app == null) app = DepthLabApp.Instance;
            bool allowed = Allowed;
            if (!allowed || !wasAllowed || (app != null &&
                (app.Retries != lastRetries || app.Mistakes < lastMistakes || app.Hints < lastHints)) ||
                (piece != null && lastPlaced && !piece.IsPlaced))
            {
                ClearVisuals();
                Snapshot();
                wasAllowed = allowed;
                return;
            }
            wasAllowed = true;
            float now = Time.unscaledTime;
            bool held = Held;
            bool placed = piece.IsPlaced;
            bool wasHeld = lastHeld;
            bool internalRelease = releaseField != null && releaseField.FieldType == typeof(bool) &&
                (bool)releaseField.GetValue(piece);
            if (held && !lastHeld) BeginGrab();
            if ((!held && lastHeld) || internalRelease) EndGrab();
            if (app.Hints > lastHints) guidanceUntil = now + 4f;
            bool atHome = piece.home != null &&
                (piece.transform.position - piece.home.position).sqrMagnitude < .0025f;
            bool teleportedHome = atHome &&
                (lastPosition - piece.transform.position).sqrMagnitude > .0025f;
            bool ownReset = atHome && (teleportedHome || pendingRelease || wasHeld);
            if (placed && !lastPlaced)
            {
                if (wasHeld && !pendingRelease) EndGrab();
                Outcome(true);
            }
            else if (app.Mistakes > lastMistakes && ownReset)
            {
                Outcome(false);
            }
            else if (pendingRelease && (now > pendingUntil || held))
                pendingRelease = false;
            bool hovered = !placed && (xrHover || DesktopHover());
            if (hovered && !hover && !held) Play(hoverCue);
            hover = hovered;
            bool effect = now < effectUntil;
            bool guidance = !placed && (held || pendingRelease || now < guidanceUntil);
            bool visible = hover || held || guidance || effect;
            Color color = effect ? (successEffect ? successColor : failureColor) :
                held || guidance ? guidanceColor : successColor;
            float pulse = .5f + .5f * Mathf.Sin(now * (held ? 8f : 5f));
            foreach (RendererState state in rendererStates)
                if (visible) state.Tint(color, .25f + pulse * .55f, false);
                else state.Restore();
            if (rimState != null)
                if (guidance || effect) rimState.Tint(color, .5f + pulse, false);
                else rimState.Restore();
            if (outlineRenderers != null)
                foreach (Renderer renderer in outlineRenderers)
                    if (renderer != null)
                    {
                        renderer.enabled = visible;
                        if (visible) TintVisual(renderer, color);
                        else renderer.SetPropertyBlock(null);
                    }
            if (settleVisual != null)
                settleVisual.localScale = settleScale * (effect ? 1f + .025f * Mathf.Sin(
                    Mathf.Clamp01((effectUntil - now) / .8f) * Mathf.PI) : 1f);
            UpdateScan(guidance || effect, color, pulse, effect);
            UpdateLabel(visible, color, held, effect);
            lastHeld = held;
            lastPlaced = placed;
            lastPosition = piece.transform.position;
            lastMistakes = app.Mistakes;
            lastRetries = app.Retries;
            lastHints = app.Hints;
        }

        private bool DesktopHover()
        {
            if (DepthLabInput.IsXRActive || viewCamera == null || Mouse.current == null) return false;
            Ray ray = viewCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            return gameObject.scene.GetPhysicsScene().Raycast(ray.origin, ray.direction, out RaycastHit hit,
                8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponentInParent<ChapterPiece>() == piece;
        }

        private void Outcome(bool success)
        {
            pendingRelease = false;
            successEffect = success;
            effectUntil = Time.unscaledTime + .8f;
            Play(success ? successCue : failureCue);
        }

        private void Play(AudioClip clip)
        {
            if (!Allowed || audioSource == null || !audioSource.isActiveAndEnabled || clip == null) return;
            float sfx = app.settings != null ? Mathf.Clamp01(app.settings.sfx) : 1f;
            audioSource.PlayOneShot(clip, sfx);
        }

        private void TintVisual(Renderer renderer, Color color)
        {
            renderer.GetPropertyBlock(visualBlock);
            visualBlock.SetColor(BaseColor, color);
            visualBlock.SetColor(MainColor, color);
            renderer.SetPropertyBlock(visualBlock);
        }

        private static void WorldScale(Transform target, Vector3 desired)
        {
            target.localScale = Vector3.one;
            Vector3 inherited = target.lossyScale;
            target.localScale = new Vector3(
                Mathf.Abs(inherited.x) > .00001f ? desired.x / Mathf.Abs(inherited.x) : 0f,
                Mathf.Abs(inherited.y) > .00001f ? desired.y / Mathf.Abs(inherited.y) : 0f,
                Mathf.Abs(inherited.z) > .00001f ? desired.z / Mathf.Abs(inherited.z) : 0f);
        }

        private void UpdateScan(bool visible, Color color, float pulse, bool effect)
        {
            if (scanRing == null || scanRenderer == null || slotRim == null) return;
            scanRing.gameObject.SetActive(visible);
            if (!visible)
            {
                scanRenderer.SetPropertyBlock(null);
                return;
            }
            Bounds bounds = slotRim.bounds;
            scanRing.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.max.y + .008f,
                bounds.center.z), Quaternion.identity);
            float diameter = Mathf.Max(bounds.size.x, bounds.size.z);
            WorldScale(scanRing, Vector3.one * diameter * (effect ? 1f + pulse * .15f : 1.015f));
            TintVisual(scanRenderer, color);
        }

        private void UpdateLabel(bool visible, Color color, bool held, bool effect)
        {
            if (label == null) return;
            Canvas canvas = label.GetComponentInParent<Canvas>(true);
            Transform root = canvas != null ? canvas.transform : label.transform;
            root.gameObject.SetActive(visible);
            if (!visible) return;
            Vector3 center = piece.transform.position;
            float top = center.y + .35f;
            if (objectRenderers != null)
                foreach (Renderer renderer in objectRenderers)
                    if (renderer != null) top = Mathf.Max(top, renderer.bounds.max.y + .15f);
            root.position = new Vector3(center.x, top + .08f, center.z);
            if (viewCamera != null)
            {
                Vector3 forward = root.position - viewCamera.transform.position;
                if (forward.sqrMagnitude > .0001f)
                    root.rotation = Quaternion.LookRotation(forward, viewCamera.transform.up);
            }
            WorldScale(root, Vector3.one * .0015f);
            label.raycastTarget = false;
            label.color = color;
            label.text = objectName + ""\n"" + (effect ? (successEffect ? ""匹配成功"" : ""重新对齐目标"") :
                held ? ""对齐同形线框 · 松手放置"" : ""抓取 · 对齐 · 松手"");
        }

        private void ClearVisuals()
        {
            foreach (RendererState state in rendererStates) state.Restore();
            if (rimState != null) rimState.Restore();
            if (outlineRenderers != null)
                foreach (Renderer renderer in outlineRenderers)
                    if (renderer != null)
                    {
                        renderer.enabled = false;
                        renderer.SetPropertyBlock(null);
                    }
            if (scanRing != null) scanRing.gameObject.SetActive(false);
            if (scanRenderer != null) scanRenderer.SetPropertyBlock(null);
            if (label != null)
            {
                Canvas canvas = label.GetComponentInParent<Canvas>(true);
                (canvas != null ? canvas.gameObject : label.gameObject).SetActive(false);
            }
            if (settleVisual != null) settleVisual.localScale = settleScale;
            pendingRelease = false;
            xrHover = hover = false;
            effectUntil = guidanceUntil = 0f;
        }
    }
}
";
    }
}
