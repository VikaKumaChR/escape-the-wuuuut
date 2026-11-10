using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace DepthLab.Editor
{
    /// <summary>Bakes Nova's original low-poly instruments, UI and nonverbal tones in place.</summary>
    public static class DepthLabNovaBuilder
    {
        private const string Generated = "Assets/DepthLab/Generated/Nova";
        private const string PrefabPath = "Assets/DepthLab/Nova/Prefabs/Nova.prefab";
        private const string Labels = "诺娃研究助手 · 本地与诺娃交流请求提示（计入评分）";
        private static readonly Color Cyan = new Color(.12f, .91f, .86f, 1f);
        private static readonly Color Gold = new Color(1f, .64f, .24f, 1f);
        private static readonly Color Ink = new Color(.025f, .065f, .085f, 1f);
        private static readonly Color Paper = new Color(.86f, .92f, .91f, 1f);

        private sealed class Kit
        {
            public Mesh head, visor, bar, eye, mouth, smile, chest, joint, upperArm, forearm;
            public Mesh palm, finger, baseMesh, ring, reactorRing, reactorBlade, neck, pod;
            public Material shell, metal, dark, cyan, gold, white;
            public TMP_FontAsset font;
            public AudioClip[] tones;
        }

        [MenuItem("DEPTHLAB/Bake Nova Companion In Place")]
        public static void Apply()
        {
            if (Application.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("Stop Play Mode and finish script compilation before baking Nova.");
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open the installed DEPTHLAB scene.");
            DepthLabApp[] apps = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<DepthLabApp>(true)).ToArray();
            if (apps.Length != 1)
                throw new InvalidOperationException("Expected exactly one installed DepthLabApp.");
            DepthLabApp app = apps[0];
            if (app.hudText == null || app.hudText.font == null || app.headCamera == null ||
                app.headCamera.gameObject.scene != scene || app.settings == null ||
                app.settings.gameObject.scene != scene || app.chapters == null || app.chapters.Length == 0 ||
                app.chapters.Any(c => c == null))
                throw new InvalidOperationException("Nova requires the authored HUD font, scene camera, settings and intact chapters.");
            RequirePersistent(app.hudText.font, "Installed HUD font");
            RequirePersistent(app.hudText.font.material, "Installed HUD font material");
            int eventSystems = scene.GetRootGameObjects()
                .Sum(r => r.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true).Length);
            if (eventSystems != 1 || app.uiModule == null || app.uiModule.gameObject.scene != scene)
                throw new InvalidOperationException("Keep the installed EventSystem and XR UI input module; Nova creates neither.");
            Transform[] matches = app.transform.Cast<Transform>()
                .Where(t => t.name == "DepthLabCompanion").ToArray();
            if (matches.Length > 1)
                throw new InvalidOperationException("Duplicate DepthLabCompanion roots must be resolved before baking.");
            if (matches.Length == 1 && PrefabUtility.IsPartOfPrefabInstance(matches[0]) &&
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(matches[0].gameObject) != PrefabPath)
                throw new InvalidOperationException("The existing companion belongs to another prefab; it will not be replaced.");

            string appSnapshot = EditorJsonUtility.ToJson(app);
            EnsureFolder(Generated);
            EnsureFolder("Assets/DepthLab/Nova/Prefabs");
            Kit kit = CreateKit(app.hudText.font);
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Bake Nova companion in place");
            Transform root;
            if (matches.Length == 0)
            {
                root = new GameObject("DepthLabCompanion").transform;
                Undo.RegisterCreatedObjectUndo(root.gameObject, "Create Nova companion");
                root.SetParent(app.transform, false);
                root.localPosition = new Vector3(2.8f, 0f, 1.2f);
            }
            else
            {
                root = matches[0];
                Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Update Nova companion");
            }
            DepthLabNova nova = Component<DepthLabNova>(root);
            BuildVisual(nova, root, kit);
            BuildDialogue(nova, root, kit);
            AudioSource source = Component<AudioSource>(Node(root, "EmotionTone", new Vector3(0, 1.4f, 0)));
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = .6f;
            source.maxDistance = 7f;
            source.volume = .35f;
            source.dopplerLevel = 0f;
            source.priority = 160;
            source.clip = kit.tones[0];
            nova.toneSource = source;
            nova.emotionTones = kit.tones;
            ValidateOwnedReferences(nova);

            Vector3 position = root.localPosition;
            Quaternion rotation = root.localRotation;
            Vector3 scale = root.localScale;
            bool active = root.gameObject.activeSelf;
            try
            {
                // Prefabs cannot own references into the installed scene. Restore these as
                // explicit instance overrides only after the persistent prefab has been saved.
                SetSceneReferences(nova, null, null, null);
                root.localPosition = Vector3.zero;
                root.localRotation = Quaternion.identity;
                root.localScale = Vector3.one;
                root.gameObject.SetActive(true);
                EditorUtility.SetDirty(nova);
                GameObject saved = PrefabUtility.SaveAsPrefabAssetAndConnect(
                    root.gameObject, PrefabPath, InteractionMode.AutomatedAction, out bool success);
                if (!success || saved == null)
                    throw new InvalidOperationException("Nova prefab save failed.");
                DepthLabNova prefabNova = saved.GetComponent<DepthLabNova>();
                ValidateOwnedReferences(prefabNova);
                if (prefabNova.app != null || prefabNova.settings != null || prefabNova.dialogueCanvas.worldCamera != null)
                    throw new InvalidOperationException("Nova prefab must not serialize scene-only references.");
            }
            finally
            {
                root.localPosition = position;
                root.localRotation = rotation;
                root.localScale = scale;
                root.gameObject.SetActive(active);
                nova = root.GetComponent<DepthLabNova>();
                SetSceneReferences(nova, app, app.settings, app.headCamera);
                PrefabUtility.RecordPrefabInstancePropertyModifications(root);
                PrefabUtility.RecordPrefabInstancePropertyModifications(root.gameObject);
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undo);
            }
            if (EditorJsonUtility.ToJson(app) != appSnapshot)
                throw new InvalidOperationException("The installed app changed while baking Nova.");
            if (nova.app != app || nova.settings != app.settings || nova.dialogueCanvas.worldCamera != app.headCamera)
                throw new InvalidOperationException("Nova scene reference overrides could not be serialized.");
            SaveOwnedAssets();
            Debug.Log("Nova baked in place: original low-poly model, expressions, two buttons and six non-verbal tones. The main scene was not saved; the caller saves it.");
        }

        public static DepthLabNova BuildGuideModel(Transform root, TMP_FontAsset installedFont)
        {
            if (Application.isPlaying || EditorApplication.isCompiling || root == null)
                throw new InvalidOperationException("Guide model authoring requires a valid root in Edit Mode.");
            RequirePersistent(installedFont, "Guide font");
            EnsureFolder(Generated);
            // Reuse the sculpted kit without modifying fonts or installing the global companion.
            Kit kit = CreateKit(installedFont, false);
            DepthLabNova nova = Component<DepthLabNova>(root);
            nova.externallyDriven = true;
            BuildVisual(nova, root, kit);
            Transform oldDialogue = root.Find("DialogueCanvas");
            if (oldDialogue != null) oldDialogue.gameObject.SetActive(false);
            AudioSource source = Component<AudioSource>(Node(root, "EmotionTone", new Vector3(0, 1.4f, 0)));
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = .6f;
            source.maxDistance = 7f;
            source.volume = .35f;
            source.dopplerLevel = 0f;
            source.priority = 160;
            nova.toneSource = source;
            nova.emotionTones = kit.tones;
            EditorUtility.SetDirty(nova);
            return nova;
        }

        private static void BuildVisual(DepthLabNova nova, Transform root, Kit k)
        {
            var luminous = new List<Renderer>();
            Transform pedestal = Node(root, "Pedestal");
            Solid(pedestal, "FacetedFoot", Vector3.zero, k.baseMesh, k.metal);
            Solid(pedestal, "DeckRim", new Vector3(0, .115f, 0), k.ring, k.cyan);
            Solid(pedestal, "SuspensionSocket", new Vector3(0, .18f, 0), k.pod, k.dark,
                new Vector3(.72f, .55f, .72f));
            for (int i = 0; i < 3; i++)
            {
                Transform hoop = Solid(pedestal, "LiftHoop" + i, new Vector3(0, .27f + i * .105f, 0),
                    k.ring, i == 1 ? k.gold : k.cyan, Vector3.one * (.47f - i * .08f));
                hoop.localRotation = Quaternion.Euler(0, i * 20, 0);
            }
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                Transform notch = Solid(pedestal, "DeckIndex" + i,
                    new Vector3(Mathf.Sin(a) * .29f, .124f, Mathf.Cos(a) * .29f),
                    k.bar, k.white, new Vector3(.32f, .6f, 1f));
                notch.localRotation = Quaternion.Euler(90, i * 45, 0);
            }
            Transform visual = Node(root, "VisualRoot", new Vector3(0, .55f, 0));
            Transform torso = Node(visual, "Torso", new Vector3(0, .65f, 0));
            nova.visualRoot = visual;
            nova.torso = torso;
            Solid(torso, "LathedChest", Vector3.zero, k.chest, k.shell);
            Solid(torso, "LowerFloatingKeel", new Vector3(0, -.31f, 0), k.pod, k.metal,
                new Vector3(.8f, .8f, .8f));
            Solid(torso, "NeckGimbal", new Vector3(0, .25f, 0), k.neck, k.metal);
            Transform reactor = Node(torso, "Reactor", new Vector3(0, .005f, -.176f));
            Solid(reactor, "ReactorBezel", Vector3.zero, k.reactorRing, k.metal);
            Solid(reactor, "ReactorInnerLight", new Vector3(0, 0, -.011f),
                k.reactorRing, k.cyan, Vector3.one * .62f, luminous);
            Solid(reactor, "ReactorHub", new Vector3(0, 0, -.018f),
                k.joint, k.white, new Vector3(.45f, .45f, .22f), luminous);
            nova.reactorSegments = new Transform[8];
            for (int i = 0; i < 8; i++)
            {
                Transform segment = Solid(reactor, "ReactorPetal" + i, new Vector3(0, 0, -.025f),
                    k.reactorBlade, i % 2 == 0 ? k.cyan : k.gold, Vector3.one, luminous);
                segment.localRotation = Quaternion.Euler(0, 0, i * 45f);
                nova.reactorSegments[i] = segment;
            }
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                Transform rib = Solid(torso, "CollarInlay" + side, new Vector3(sign * .13f, .16f, -.135f),
                    k.bar, k.gold, new Vector3(1.05f, .75f, 1f));
                rib.localRotation = Quaternion.Euler(0, sign * -20f, sign * 14f);
                for (int i = 0; i < 3; i++)
                    Solid(torso, "Vent" + side + "_" + i,
                        new Vector3(sign * .135f, -.10f - i * .028f, -.136f),
                        k.bar, k.dark, new Vector3(.55f, .5f, 1f));
            }

            Transform head = Node(torso, "Head", new Vector3(0, .45f, 0));
            nova.head = head;
            Solid(head, "SculptedHelmet", Vector3.zero, k.head, k.shell);
            Solid(head, "RecessedVisor", new Vector3(0, .002f, -.153f), k.visor, k.dark);
            Transform crest = Solid(head, "CrownInlay", new Vector3(0, .172f, -.025f), k.bar, k.gold,
                new Vector3(.65f, .7f, 5f));
            crest.localRotation = Quaternion.Euler(90, 0, 0);
            Solid(head, "AntennaStem", new Vector3(.15f, .215f, .035f), k.neck, k.metal,
                new Vector3(.18f, .55f, .18f));
            Solid(head, "AntennaBeacon", new Vector3(.15f, .258f, .035f), k.joint, k.cyan,
                Vector3.one * .20f);
            nova.eyeCores = new Transform[2];
            nova.eyeSmiles = new Transform[2];
            nova.brows = new Transform[2];
            nova.mouthSegments = new Transform[3];
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                Solid(head, "TemplePod" + side, new Vector3(sign * .236f, .008f, .025f),
                    k.joint, k.metal, new Vector3(.62f, 1.2f, 1.2f));
                Solid(head, "TempleSignal" + side, new Vector3(sign * .27f, .008f, .025f),
                    k.joint, k.cyan, new Vector3(.14f, .65f, .65f));
                nova.eyeCores[side] = Solid(head, "EyeCore" + side,
                    new Vector3(sign * .098f, .018f, -.176f), k.eye, k.cyan, Vector3.one, luminous);
                nova.eyeSmiles[side] = Solid(head, "EyeSmile" + side,
                    new Vector3(sign * .098f, .018f, -.177f), k.smile, k.cyan, Vector3.one, luminous);
                nova.eyeSmiles[side].gameObject.SetActive(false);
                nova.brows[side] = Solid(head, "Brow" + side,
                    new Vector3(sign * .098f, .078f, -.175f), k.bar, k.white,
                    new Vector3(.8f, .8f, 1f), luminous);
                Transform cheek = Solid(head, "CheekTrim" + side,
                    new Vector3(sign * .165f, -.076f, -.157f), k.bar, k.gold,
                    new Vector3(.48f, .65f, 1f));
                cheek.localRotation = Quaternion.Euler(0, 0, sign * 24);
            }
            for (int i = 0; i < 3; i++)
                nova.mouthSegments[i] = Solid(head, "MouthSegment" + i,
                    new Vector3((i - 1) * .036f, -.073f, -.176f),
                    k.mouth, k.cyan, Vector3.one, luminous);

            nova.shoulders = new Transform[2];
            nova.elbows = new Transform[2];
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                Transform shoulder = Node(torso, side == 0 ? "LeftShoulder" : "RightShoulder",
                    new Vector3(sign * .285f, .16f, 0));
                shoulder.localRotation = Quaternion.Euler(0, 0, sign * 6f);
                nova.shoulders[side] = shoulder;
                Solid(shoulder, "ShoulderGimbal", Vector3.zero, k.joint, k.metal);
                Solid(shoulder, "ShoulderArmor", new Vector3(sign * .025f, .005f, 0),
                    k.palm, k.shell, new Vector3(1.1f, 1.2f, 1.2f));
                Solid(shoulder, "ShoulderStatus", new Vector3(0, .012f, -.076f),
                    k.bar, k.cyan, new Vector3(.55f, .65f, 1f));
                Solid(shoulder, "TaperedUpperArm", new Vector3(0, -.11f, 0), k.upperArm, k.shell);
                Solid(shoulder, "UpperArmInlay", new Vector3(0, -.11f, -.049f), k.bar, k.gold,
                    new Vector3(.33f, 4.5f, 1f));
                Transform elbow = Node(shoulder, "Elbow", new Vector3(0, -.22f, 0));
                elbow.localRotation = Quaternion.Euler(-12, 0, sign * -9f);
                nova.elbows[side] = elbow;
                Solid(elbow, "ElbowBearing", Vector3.zero, k.joint, k.dark, Vector3.one * .7f);
                Solid(elbow, "ElbowCap", new Vector3(0, 0, -.047f), k.joint, k.gold,
                    new Vector3(.4f, .4f, .18f));
                Solid(elbow, "TaperedForearm", new Vector3(0, -.10f, 0), k.forearm, k.metal);
                Solid(elbow, "ForearmShield", new Vector3(0, -.095f, -.026f),
                    k.forearm, k.shell, new Vector3(.72f, .80f, .60f));
                Transform wrist = Node(elbow, "Wrist", new Vector3(0, -.20f, 0));
                Solid(wrist, "WristBearing", Vector3.zero, k.joint, k.dark, Vector3.one * .4f);
                Solid(wrist, "ClawPalm", new Vector3(0, -.042f, 0), k.palm, k.shell,
                    new Vector3(.7f, .65f, .7f));
                for (int finger = 0; finger < 2; finger++)
                {
                    float s = finger == 0 ? -1f : 1f;
                    Transform digit = Solid(wrist, "ClawFinger" + finger,
                        new Vector3(s * .030f, -.086f, -.012f), k.finger, k.metal);
                    digit.localRotation = Quaternion.Euler(-16f, 0, s * -14f);
                    Solid(digit, "FingerPad", new Vector3(0, -.027f, -.014f),
                        k.mouth, k.dark, new Vector3(.7f, 1.3f, 1.2f));
                }
                Transform thumb = Solid(wrist, "OpposingThumb", new Vector3(0, -.05f, .040f),
                    k.finger, k.metal, new Vector3(.8f, .8f, .8f));
                thumb.localRotation = Quaternion.Euler(42, 0, 0);
            }
            nova.luminousRenderers = luminous.ToArray();
        }

        private static void BuildDialogue(DepthLabNova nova, Transform root, Kit k)
        {
            RectTransform rect = Rect(root, "DialogueCanvas", Vector2.zero, new Vector2(720, 460));
            rect.localPosition = new Vector3(0, 1.05f, -.39f);
            // Unity's standard world-space Canvas front points along local -Z.
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * .001f;
            Canvas canvas = Component<Canvas>(rect);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = null;
            canvas.sortingOrder = 25;
            Component<UnityEngine.UI.CanvasScaler>(rect).dynamicPixelsPerUnit = 1f;
            Component<UnityEngine.UI.GraphicRaycaster>(rect);
            TrackedDeviceGraphicRaycaster raycaster = Component<TrackedDeviceGraphicRaycaster>(rect);
            raycaster.checkFor3DOcclusion = false;
            raycaster.checkFor2DOcclusion = false;
            nova.dialogueCanvas = canvas;
            Text(rect, "Name", "诺娃", new Vector2(0, 184), new Vector2(700, 40), 32, Paper, k.font);
            Text(rect, "Identity", "研究助手 · 本地", new Vector2(0, 142),
                new Vector2(700, 32), 22, new Color(.57f, .79f, .79f), k.font);
            nova.talkButton = Button(rect, "TalkButton", "与诺娃交流", new Vector2(0, 55),
                Cyan, k.font, nova.Talk, "与诺娃交流，了解实验观察方法。");
            nova.hintButton = Button(rect, "HintButton", "请求提示（计入评分）", new Vector2(0, -32),
                Gold, k.font, nova.Hint, "请求本章提示，将计入本章评分。");
            var nav = new UnityEngine.UI.Navigation
            {
                mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnDown = nova.hintButton
            };
            nova.talkButton.navigation = nav;
            nav.selectOnDown = null;
            nav.selectOnUp = nova.talkButton;
            nova.hintButton.navigation = nav;
            RectTransform panel = Rect(rect, "SubtitlePanel", new Vector2(0, -153), new Vector2(700, 132));
            UnityEngine.UI.Image background = Component<UnityEngine.UI.Image>(panel);
            background.color = new Color(Ink.r, Ink.g, Ink.b, .94f);
            background.raycastTarget = false;
            nova.subtitle = Text(panel, "Subtitle", "", Vector2.zero, new Vector2(652, 114), 25, Paper, k.font);
            nova.subtitle.enableAutoSizing = false;
            nova.subtitle.overflowMode = TextOverflowModes.Ellipsis;
            nova.subtitlePanel = panel.gameObject;
            panel.gameObject.SetActive(false);
        }

        private static DepthLabButton Button(Transform parent, string name, string label, Vector2 position,
            Color accent, TMP_FontAsset font, UnityAction callback, string tooltip)
        {
            RectTransform rect = Rect(parent, name, position, new Vector2(700, 72));
            UnityEngine.UI.Image face = Component<UnityEngine.UI.Image>(rect);
            face.color = Ink;
            face.raycastTarget = true;
            RectTransform haloRect = Rect(rect, "InteractionHalo", Vector2.zero, new Vector2(712, 84));
            haloRect.SetAsFirstSibling();
            UnityEngine.UI.Image halo = Component<UnityEngine.UI.Image>(haloRect);
            halo.color = new Color(accent.r, accent.g, accent.b, .2f);
            halo.raycastTarget = false;
            // Put the opaque inset above the halo; the root graphic remains the hit target.
            UnityEngine.UI.Image inset = Component<UnityEngine.UI.Image>(
                Rect(rect, "Inset", Vector2.zero, new Vector2(698, 70)));
            inset.color = Ink;
            inset.raycastTarget = false;
            UnityEngine.UI.Image stripe = Component<UnityEngine.UI.Image>(
                Rect(rect, "Accent", new Vector2(-338, 0), new Vector2(5, 44)));
            stripe.color = accent;
            stripe.raycastTarget = false;
            Text(rect, "Label", label, Vector2.zero, new Vector2(656, 58), 27, Paper, font);
            DepthLabButton button = Component<DepthLabButton>(rect);
            button.targetGraphic = face;
            button.halo = halo;
            button.tooltip = tooltip;
            button.accentColor = accent;
            button.interactable = true;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(.72f, 1f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(.5f, .8f, .8f);
            colors.disabledColor = new Color(.5f, .5f, .5f, .6f);
            colors.fadeDuration = .12f;
            button.colors = colors;
            // Replace only this owned button's event so rebakes never accumulate listeners.
            button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            UnityEventTools.AddPersistentListener(button.onClick, callback);
            button.onClick.SetPersistentListenerState(0, UnityEventCallState.RuntimeOnly);
            EditorUtility.SetDirty(button);
            return button;
        }

        private static TMP_Text Text(Transform parent, string name, string value, Vector2 position,
            Vector2 size, float fontSize, Color color, TMP_FontAsset font)
        {
            TextMeshProUGUI text = Component<TextMeshProUGUI>(Rect(parent, name, position, size));
            text.font = font;
            text.fontSharedMaterial = font.material;
            text.text = value;
            text.color = color;
            text.fontSize = fontSize;
            text.enableAutoSizing = false;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        private static Kit CreateKit(TMP_FontAsset installedFont, bool bakeLabelFont = true)
        {
            var k = new Kit();
            k.shell = Material("PorcelainArmor", Paper, false, .12f, .36f);
            k.metal = Material("GraphiteTitanium", new Color(.16f, .23f, .25f), false, .55f, .4f);
            k.dark = Material("VisorInk", Ink, false, .15f, .28f);
            k.cyan = Material("ReactorJade", Cyan, true, 0, .1f);
            k.gold = Material("SignalAmber", Gold, true, 0, .1f);
            k.white = Material("SignalPearl", Paper, true, 0, .1f);
            k.head = SaveMesh("SculptedHelmet", BeveledHull(.46f, .33f, .30f, .057f, .024f));
            k.visor = SaveMesh("RecessedVisor", BeveledHull(.386f, .223f, .026f, .042f, .005f));
            k.eye = SaveMesh("BeveledEye", BeveledHull(.10f, .065f, .012f, .014f, .003f));
            k.bar = SaveMesh("BeveledSignalBar", BeveledHull(.10f, .012f, .012f, .004f, .002f));
            k.mouth = SaveMesh("BeveledMouthBar", BeveledHull(.03f, .009f, .009f, .003f, .0015f));
            k.smile = SaveMesh("HappyEyeArc", Arc(.046f, .011f, .012f, 22, 158, 8));
            k.chest = SaveMesh("LathedChest", Lathe(new[]
            {
                new Vector2(.10f, -.255f), new Vector2(.16f, -.20f),
                new Vector2(.22f, -.08f), new Vector2(.24f, .09f),
                new Vector2(.20f, .185f), new Vector2(.12f, .215f)
            }, 12, .72f));
            k.joint = SaveMesh("Gimbal", Lathe(new[]
            {
                new Vector2(.025f, -.062f), new Vector2(.057f, -.038f),
                new Vector2(.065f, 0), new Vector2(.057f, .038f), new Vector2(.025f, .062f)
            }, 10, 1f));
            k.upperArm = SaveMesh("TaperedUpperArm", TaperedHull(.087f, .068f, .078f, .054f, .195f));
            k.forearm = SaveMesh("TaperedForearm", TaperedHull(.10f, .06f, .084f, .052f, .178f));
            k.palm = SaveMesh("ClawPalm", BeveledHull(.10f, .11f, .10f, .025f, .012f));
            k.finger = SaveMesh("ClawDigit", TaperedHull(.032f, .022f, .033f, .021f, .065f));
            k.neck = SaveMesh("Neck", Lathe(new[]
            {
                new Vector2(.045f, -.055f), new Vector2(.054f, -.038f),
                new Vector2(.054f, .038f), new Vector2(.045f, .055f)
            }, 10, 1f));
            k.pod = SaveMesh("SuspensionPod", Lathe(new[]
            {
                new Vector2(.045f, -.10f), new Vector2(.10f, -.065f),
                new Vector2(.135f, .035f), new Vector2(.105f, .072f)
            }, 12, .9f));
            k.baseMesh = SaveMesh("Pedestal", Lathe(new[]
            {
                new Vector2(.31f, .005f), new Vector2(.36f, .025f),
                new Vector2(.36f, .064f), new Vector2(.31f, .11f)
            }, 24, 1f));
            Mesh ring = Arc(.315f, .018f, .014f, 0, 360, 32);
            RotateMesh(ring, Quaternion.Euler(90, 0, 0));
            k.ring = SaveMesh("LevitationRing", ring);
            k.reactorRing = SaveMesh("ReactorBezel", Arc(.098f, .013f, .024f, 0, 360, 24));
            k.reactorBlade = SaveMesh("ReactorPetal", Prism(new[]
            {
                new Vector2(-.009f, .047f), new Vector2(.014f, .037f),
                new Vector2(.032f, .073f), new Vector2(.009f, .088f)
            }, .008f));
            k.font = bakeLabelFont ? LabelFont(installedFont) : installedFont;
            k.tones = BakeTones();
            SaveOwnedAssets();
            return k;
        }

        private static TMP_FontAsset LabelFont(TMP_FontAsset installed)
        {
            var corpus = new StringBuilder(Labels)
                .Append("与诺娃交流，了解实验观察方法。请求本章提示，将计入本章评分。");
            // Include the companion's actual Chinese dialogue at bake time without depending
            // on private runtime implementation details or changing the installed font.
            foreach (string guid in AssetDatabase.FindAssets("DepthLabNova t:MonoScript", new[] { "Assets/DepthLab" }))
            {
                string runtime = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(runtime) == "DepthLabNova" && File.Exists(runtime))
                    corpus.Append(File.ReadAllText(runtime));
            }
            string required = new string(corpus.ToString().Where(c =>
                c >= '\u2e80' && c <= '\uffef' || c == '·').Distinct().OrderBy(c => c).ToArray());
            if (required.All(c => installed.HasCharacter(c, true, false))) return installed;

            Font source = FindSourceFont(installed, new HashSet<TMP_FontAsset>());
            if (source == null)
                source = AssetDatabase.LoadAssetAtPath<Font>("Assets/TwistedSpace/Demo/Fonts/NotoSansSC-Medium.otf");
            if (source == null)
                throw new InvalidOperationException("Missing Chinese glyphs and no installed source font is available.");
            string supplementPath = Generated + "/NovaChineseSupplement.asset";
            TMP_FontAsset supplement = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(supplementPath);
            if (supplement == null)
            {
                supplement = TMP_FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA,
                    1024, 1024, AtlasPopulationMode.Dynamic, true);
                supplement.name = "NovaChineseSupplement";
                AssetDatabase.CreateAsset(supplement, supplementPath);
            }
            try
            {
                supplement.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                var serialized = new SerializedObject(supplement);
                SerializedProperty file = serialized.FindProperty("m_SourceFontFile");
                SerializedProperty guid = serialized.FindProperty("m_SourceFontFileGUID");
                if (file == null || guid == null)
                    throw new InvalidOperationException("TMP source font serialization has changed.");
                file.objectReferenceValue = source;
                guid.stringValue = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
                serialized.ApplyModifiedPropertiesWithoutUndo();
                supplement.isMultiAtlasTexturesEnabled = true;
                if (!supplement.TryAddCharacters(required, out string missing) && !string.IsNullOrEmpty(missing))
                    throw new InvalidOperationException("Nova's installed font source cannot render: " + missing);
            }
            finally
            {
                supplement.atlasPopulationMode = AtlasPopulationMode.Static;
                foreach (Texture2D atlas in supplement.atlasTextures)
                {
                    if (atlas == null) continue;
                    if (!EditorUtility.IsPersistent(atlas))
                        AssetDatabase.AddObjectToAsset(atlas, supplement);
                    EditorUtility.SetDirty(atlas);
                }
                if (!EditorUtility.IsPersistent(supplement.material))
                    AssetDatabase.AddObjectToAsset(supplement.material, supplement);
                EditorUtility.SetDirty(supplement);
                EditorUtility.SetDirty(supplement.material);
            }
            string path = Generated + "/NovaLabelFont.asset";
            TMP_FontAsset wrapper = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (wrapper == null)
            {
                wrapper = UnityEngine.Object.Instantiate(installed);
                wrapper.name = "NovaLabelFont";
                wrapper.material = UnityEngine.Object.Instantiate(installed.material);
                wrapper.material.name = "NovaLabelFontMaterial";
                AssetDatabase.CreateAsset(wrapper, path);
                AssetDatabase.AddObjectToAsset(wrapper.material, wrapper);
            }
            wrapper.fallbackFontAssetTable = new List<TMP_FontAsset> { supplement, installed };
            wrapper.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(wrapper);
            if (!required.All(c => wrapper.HasCharacter(c, true, false)))
                throw new InvalidOperationException("Nova font fallback coverage could not be baked.");
            return wrapper;
        }

        private static Font FindSourceFont(TMP_FontAsset font, HashSet<TMP_FontAsset> visited)
        {
            if (font == null || !visited.Add(font)) return null;
            if (font.sourceFontFile != null) return font.sourceFontFile;
            var serialized = new SerializedObject(font);
            SerializedProperty reference = serialized.FindProperty("m_SourceFontFile_EditorRef");
            if (reference != null && reference.objectReferenceValue is Font source) return source;
            SerializedProperty guid = serialized.FindProperty("m_SourceFontFileGUID");
            if (guid != null && !string.IsNullOrEmpty(guid.stringValue))
            {
                Font found = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid.stringValue));
                if (found != null) return found;
            }
            if (font.fallbackFontAssetTable != null)
                foreach (TMP_FontAsset fallback in font.fallbackFontAssetTable)
                {
                    Font found = FindSourceFont(fallback, visited);
                    if (found != null) return found;
                }
            return null;
        }

        private static AudioClip[] BakeTones()
        {
            // Neutral, Happy, Curious, Thinking, Encouraging, Excited: gentle oscillator
            // phrases, never speech, recorded as mono PCM16 for inexpensive spatial playback.
            string[] names = { "Neutral", "Happy", "Curious", "Thinking", "Encouraging", "Excited" };
            double[][] notes =
            {
                new[] { 392.0, 523.25 },
                new[] { 523.25, 659.25, 783.99 },
                new[] { 440.0, 587.33, 554.37 },
                new[] { 349.23, 392.0, 349.23 },
                new[] { 392.0, 523.25, 659.25, 523.25 },
                new[] { 523.25, 659.25, 783.99, 1046.50 }
            };
            const int rate = 24000;
            var clips = new AudioClip[6];
            for (int emotion = 0; emotion < clips.Length; emotion++)
            {
                double spacing = emotion == 5 ? .105 : emotion == 3 ? .205 : .155;
                double length = (notes[emotion].Length - 1) * spacing + .32;
                int samples = (int)(rate * length);
                string path = Generated + "/Tone" + names[emotion] + ".wav";
                using (var stream = new MemoryStream())
                {
                    using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
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
                            double sample = 0;
                            double time = (double)i / rate;
                            for (int n = 0; n < notes[emotion].Length; n++)
                            {
                                double t = time - n * spacing;
                                if (t < 0 || t > .32) continue;
                                double envelope = Math.Min(1, t / .012) * Math.Exp(-t * 13)
                                    * Math.Min(1, (.32 - t) / .04);
                                double phase = 2 * Math.PI * notes[emotion][n] * t;
                                sample += envelope * (Math.Sin(phase) + .16 * Math.Sin(phase * 2)
                                    + .04 * Math.Sin(phase * 3)) * .16;
                            }
                            writer.Write((short)(Math.Max(-.8, Math.Min(.8, sample)) * short.MaxValue));
                        }
                    }
                    byte[] bytes = stream.ToArray();
                    if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes))
                        File.WriteAllBytes(path, bytes);
                }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) throw new InvalidOperationException("Nova tone import failed: " + path);
                importer.forceToMono = true;
                importer.loadInBackground = false;
                var settings = importer.defaultSampleSettings;
                settings.preloadAudioData = true;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
                clips[emotion] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                RequirePersistent(clips[emotion], "Emotion tone " + emotion);
            }
            return clips;
        }

        private static Material Material(string name, Color color, bool luminous, float metallic, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("The installed URP Lit shader is required.");
            string path = Generated + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Surface", 0);
            material.SetFloat("_Cull", 2);
            material.SetColor("_EmissionColor", luminous ? color * 1.8f : Color.black);
            if (luminous) material.EnableKeyword("_EMISSION");
            else material.DisableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh SaveMesh(string name, Mesh generated)
        {
            generated.name = name;
            string path = Generated + "/" + name + ".asset";
            Mesh persistent = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (persistent == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                persistent = generated;
            }
            else
            {
                EditorUtility.CopySerialized(generated, persistent);
                UnityEngine.Object.DestroyImmediate(generated);
                EditorUtility.SetDirty(persistent);
            }
            return persistent;
        }

        private static Transform Solid(Transform parent, string name, Vector3 position, Mesh mesh, Material material,
            Vector3? scale = null, List<Renderer> luminous = null)
        {
            Transform t = Node(parent, name, position);
            t.localScale = scale ?? Vector3.one;
            Component<MeshFilter>(t).sharedMesh = mesh;
            MeshRenderer renderer = Component<MeshRenderer>(t);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            luminous?.Add(renderer);
            return t;
        }

        private static Transform Node(Transform parent, string name, Vector3? position = null)
        {
            Transform t = parent.Find(name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(parent, false);
                Undo.RegisterCreatedObjectUndo(t.gameObject, "Add Nova part");
            }
            t.localPosition = position ?? Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            t.gameObject.SetActive(true);
            return t;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            Transform found = parent.Find(name);
            if (found != null && !(found is RectTransform))
                throw new InvalidOperationException("Nova UI node requires a RectTransform: " + name);
            RectTransform rect;
            if (found == null)
            {
                rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
                rect.SetParent(parent, false);
                Undo.RegisterCreatedObjectUndo(rect.gameObject, "Add Nova UI");
            }
            else rect = (RectTransform)found;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition3D = new Vector3(position.x, position.y, 0);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            rect.gameObject.SetActive(true);
            return rect;
        }

        private static T Component<T>(Transform t) where T : Component
        {
            T component = t.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(t.gameObject);
        }

        private static void SetSceneReferences(DepthLabNova nova, DepthLabApp app,
            DepthLabSettings settings, Camera camera)
        {
            var serialized = new SerializedObject(nova);
            serialized.FindProperty("app").objectReferenceValue = app;
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var canvas = new SerializedObject(nova.dialogueCanvas);
            canvas.FindProperty("m_Camera").objectReferenceValue = camera;
            canvas.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(nova);
            EditorUtility.SetDirty(nova.dialogueCanvas);
            PrefabUtility.RecordPrefabInstancePropertyModifications(nova);
            PrefabUtility.RecordPrefabInstancePropertyModifications(nova.dialogueCanvas);
        }

        private static void ValidateOwnedReferences(DepthLabNova nova)
        {
            if (nova == null || nova.visualRoot == null || nova.torso == null || nova.head == null ||
                nova.dialogueCanvas == null || nova.subtitle == null || nova.subtitlePanel == null ||
                nova.talkButton == null || nova.hintButton == null || nova.toneSource == null)
                throw new InvalidOperationException("Nova is missing an essential authored reference.");
            RequireArray(nova.shoulders, 2, "shoulders");
            RequireArray(nova.elbows, 2, "elbows");
            RequireArray(nova.eyeCores, 2, "eye cores");
            RequireArray(nova.eyeSmiles, 2, "happy eye arcs");
            RequireArray(nova.brows, 2, "brows");
            RequireArray(nova.mouthSegments, 3, "mouth segments");
            RequireArray(nova.reactorSegments, 8, "reactor petals");
            RequireArray(nova.emotionTones, 6, "emotion tones");
            RequireArray(nova.luminousRenderers, 19, "expressive emissive renderers");
            foreach (Transform part in nova.shoulders.Concat(nova.elbows).Concat(nova.eyeCores)
                .Concat(nova.eyeSmiles).Concat(nova.brows).Concat(nova.mouthSegments)
                .Concat(nova.reactorSegments).Concat(new[] { nova.visualRoot, nova.torso, nova.head }))
                if (!part.IsChildOf(nova.transform))
                    throw new InvalidOperationException("Nova pivot references must remain inside its own prefab.");
            foreach (MeshFilter filter in nova.GetComponentsInChildren<MeshFilter>(true))
                RequirePersistent(filter.sharedMesh, filter.name + " mesh");
            foreach (Renderer renderer in nova.GetComponentsInChildren<MeshRenderer>(true))
                RequirePersistent(renderer.sharedMaterial, renderer.name + " material");
            foreach (TMP_Text text in nova.GetComponentsInChildren<TMP_Text>(true))
            {
                RequirePersistent(text.font, text.name + " font");
                RequirePersistent(text.fontSharedMaterial, text.name + " font material");
            }
            foreach (AudioClip clip in nova.emotionTones) RequirePersistent(clip, "Emotion tone");
            ValidateClick(nova.talkButton, nova, "Talk");
            ValidateClick(nova.hintButton, nova, "Hint");
        }

        private static void ValidateClick(DepthLabButton button, DepthLabNova nova, string method)
        {
            if (button.onClick.GetPersistentEventCount() != 1 ||
                button.onClick.GetPersistentTarget(0) != nova ||
                button.onClick.GetPersistentMethodName(0) != method ||
                button.targetGraphic == null || !button.targetGraphic.raycastTarget || button.halo == null)
                throw new InvalidOperationException("Nova button is not persistently wired: " + method);
        }

        private static void RequireArray<T>(T[] values, int count, string name) where T : UnityEngine.Object
        {
            if (values == null || values.Length != count || values.Any(v => v == null))
                throw new InvalidOperationException("Nova requires " + count + " valid " + name + ".");
        }

        private static void RequirePersistent(UnityEngine.Object value, string description)
        {
            if (value == null || !EditorUtility.IsPersistent(value))
                throw new InvalidOperationException(description + " must be a persistent asset.");
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int slash = folder.LastIndexOf('/');
            string parent = folder.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
        }

        private static void SaveOwnedAssets()
        {
            foreach (string guid in AssetDatabase.FindAssets("", new[] { Generated }))
            {
                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
            }
        }

        private sealed class Faces
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<int> triangles = new List<int>();

            public void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Triangle(a, b, c);
                Triangle(a, c, d);
            }

            public Mesh Mesh()
            {
                var mesh = new Mesh();
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        private static Vector2[] Chamfer(float width, float height, float corner)
        {
            float x = width * .5f, y = height * .5f;
            float c = Mathf.Min(corner, Mathf.Min(x, y) * .8f);
            return new[]
            {
                new Vector2(-x + c, -y), new Vector2(x - c, -y),
                new Vector2(x, -y + c), new Vector2(x, y - c),
                new Vector2(x - c, y), new Vector2(-x + c, y),
                new Vector2(-x, y - c), new Vector2(-x, -y + c)
            };
        }

        private static Mesh BeveledHull(float width, float height, float depth, float corner, float bevel)
        {
            Vector2[] outer = Chamfer(width, height, corner);
            Vector2[] inset = Chamfer(width - bevel * 2, height - bevel * 2, Mathf.Max(.001f, corner - bevel));
            Vector3[][] rings =
            {
                inset.Select(p => new Vector3(p.x, p.y, -depth * .5f)).ToArray(),
                outer.Select(p => new Vector3(p.x, p.y, -depth * .5f + bevel)).ToArray(),
                outer.Select(p => new Vector3(p.x, p.y, depth * .5f - bevel)).ToArray(),
                inset.Select(p => new Vector3(p.x, p.y, depth * .5f)).ToArray()
            };
            var faces = new Faces();
            for (int layer = 0; layer < rings.Length - 1; layer++)
                for (int i = 0; i < outer.Length; i++)
                {
                    int next = (i + 1) % outer.Length;
                    faces.Quad(rings[layer][i], rings[layer][next], rings[layer + 1][next], rings[layer + 1][i]);
                }
            for (int i = 0; i < outer.Length; i++)
            {
                int next = (i + 1) % outer.Length;
                faces.Triangle(new Vector3(0, 0, -depth * .5f), rings[0][next], rings[0][i]);
                faces.Triangle(new Vector3(0, 0, depth * .5f), rings[3][i], rings[3][next]);
            }
            return faces.Mesh();
        }

        private static Mesh TaperedHull(float topWidth, float bottomWidth, float topDepth, float bottomDepth, float height)
        {
            Mesh mesh = BeveledHull(topWidth, height, topDepth, .012f, .004f);
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                float t = Mathf.InverseLerp(-height * .5f, height * .5f, vertices[i].y);
                vertices[i].x *= Mathf.Lerp(bottomWidth / topWidth, 1, t);
                vertices[i].z *= Mathf.Lerp(bottomDepth / topDepth, 1, t);
            }
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh Lathe(Vector2[] profile, int sides, float depthRatio)
        {
            var faces = new Faces();
            Func<int, int, Vector3> point = (row, column) =>
            {
                float a = (column + .5f) * Mathf.PI * 2 / sides;
                return new Vector3(Mathf.Sin(a) * profile[row].x, profile[row].y,
                    Mathf.Cos(a) * profile[row].x * depthRatio);
            };
            for (int row = 0; row < profile.Length - 1; row++)
                for (int side = 0; side < sides; side++)
                    faces.Quad(point(row, side), point(row, side + 1),
                        point(row + 1, side + 1), point(row + 1, side));
            for (int side = 0; side < sides; side++)
            {
                faces.Triangle(new Vector3(0, profile[0].y, 0), point(0, side + 1), point(0, side));
                int last = profile.Length - 1;
                faces.Triangle(new Vector3(0, profile[last].y, 0), point(last, side), point(last, side + 1));
            }
            return faces.Mesh();
        }

        private static Mesh Arc(float radius, float width, float depth, float start, float end, int segments)
        {
            var faces = new Faces();
            Func<int, float, float, Vector3> point = (i, r, z) =>
            {
                float angle = Mathf.Lerp(start, end, (float)i / segments) * Mathf.Deg2Rad;
                return new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, z);
            };
            float inner = radius - width * .5f, outer = radius + width * .5f;
            float front = -depth * .5f, back = depth * .5f;
            for (int i = 0; i < segments; i++)
            {
                int j = i + 1;
                faces.Quad(point(i, inner, front), point(j, inner, front), point(j, outer, front), point(i, outer, front));
                faces.Quad(point(i, outer, back), point(j, outer, back), point(j, inner, back), point(i, inner, back));
                faces.Quad(point(i, outer, front), point(j, outer, front), point(j, outer, back), point(i, outer, back));
                faces.Quad(point(j, inner, front), point(i, inner, front), point(i, inner, back), point(j, inner, back));
            }
            if (end - start < 359f)
            {
                faces.Quad(point(0, inner, back), point(0, inner, front), point(0, outer, front), point(0, outer, back));
                faces.Quad(point(segments, outer, back), point(segments, outer, front),
                    point(segments, inner, front), point(segments, inner, back));
            }
            return faces.Mesh();
        }

        private static Mesh Prism(Vector2[] polygon, float depth)
        {
            var faces = new Faces();
            Vector2 center = polygon.Aggregate(Vector2.zero, (sum, p) => sum + p) / polygon.Length;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                Vector3 af = new Vector3(a.x, a.y, -depth * .5f);
                Vector3 bf = new Vector3(b.x, b.y, -depth * .5f);
                Vector3 ab = new Vector3(a.x, a.y, depth * .5f);
                Vector3 bb = new Vector3(b.x, b.y, depth * .5f);
                faces.Quad(af, bf, bb, ab);
                faces.Triangle(new Vector3(center.x, center.y, -depth * .5f), bf, af);
                faces.Triangle(new Vector3(center.x, center.y, depth * .5f), ab, bb);
            }
            return faces.Mesh();
        }

        private static void RotateMesh(Mesh mesh, Quaternion rotation)
        {
            mesh.vertices = mesh.vertices.Select(v => rotation * v).ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
