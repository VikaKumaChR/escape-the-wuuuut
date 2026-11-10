using System;
using System.Collections.Generic;
using DepthLab;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace DepthLab.Editor
{
    public static class DepthLabChapterBuilder
    {
        private const string AssetFolder = "Assets/DepthLab/Generated";
        private static readonly Color Background = Hex("050914");
        private static readonly Color Secondary = Hex("0A1424");
        private static readonly Color Panel = Hex("101E30");
        private static readonly Color Blue = Hex("00C8FF");
        private static readonly Color Cyan = Hex("00F0D0");
        private static readonly Color Violet = Hex("8C65FF");
        private static readonly Color Warning = Hex("FFB547");
        private static readonly Color Error = Hex("FF4F6B");
        private static readonly Color Success = Hex("4DFFC3");
        private static readonly Color Text = Hex("EAF6FF");
        private static readonly Color Muted = Hex("8CA7C4");

        private sealed class Palette
        {
            public string prefix;
            public Material floor, wall, panel, blue, cyan, violet, warning, error, success, text, muted;
        }

        public static DepthLabChapter[] Build(Transform parent, TMP_FontAsset font, Camera camera)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (font == null) throw new ArgumentNullException(nameof(font));
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            EnsureFolder();
            var chapters = new DepthLabChapter[4];
            chapters[0] = BuildSorting(parent, font, camera);
            chapters[1] = BuildForest(parent, font, camera);
            chapters[2] = BuildIllusions(parent, font, camera);
            chapters[3] = BuildReflection(parent, font, camera);
            AssetDatabase.SaveAssets();
            return chapters;
        }

        private static DepthSortingChapter BuildSorting(Transform parent, TMP_FontAsset font, Camera camera)
        {
            Palette p = CreatePalette("CH01");
            DepthSortingChapter chapter = Room<DepthSortingChapter>(parent, 0, "STEREO / DEPTH SORT", p, font, camera);
            Transform console = Console(chapter, "01   /   BINOCULAR LAB", "THREE SHAPES.\nTHREE DEPTHS.", p, font, camera);
            Label(console, "Technique", "01  GRAB\n02  ALIGN IN THREE DIMENSIONS\n03  RELEASE\n\nMatch both shape and color.\nA near miss returns the piece safely.\n\nCUBE  /  SPHERE  /  TRIANGULAR PRISM",
                font, 40, 565, 820, 440, 30, Muted);

            chapter.pieces = new ChapterPiece[3];
            chapter.slots = new Transform[3];
            chapter.slotIndicators = new Renderer[3];
            chapter.emptySlotMaterials = new[] { p.blue, p.violet, p.cyan };
            chapter.filledSlotMaterial = p.success;
            chapter.interactables = new XRGrabInteractable[3];
            chapter.interactionButtons = Array.Empty<Button>();
            string[] names = { "BLUE CUBE", "VIOLET SPHERE", "CYAN TRIANGULAR PRISM" };
            float[] xs = { -.45f, 1.15f, 2.85f };
            float[] depths = { .6f, 2.2f, 3.8f };
            Mesh prism = PrismMesh(p.prefix);
            for (int i = 0; i < 3; i++)
            {
                Material color = chapter.emptySlotMaterials[i];
                Vector3 home = new Vector3(xs[i], 1.17f, -1.65f);
                Transform homeMarker = Node("Home " + names[i], chapter.transform, home);
                Primitive("Launch pedestal", PrimitiveType.Cylinder, chapter.transform,
                    new Vector3(xs[i], .45f, -1.65f), new Vector3(.8f, .45f, .8f), p.panel, true);
                Primitive("Launch rim", PrimitiveType.Cylinder, chapter.transform,
                    new Vector3(xs[i], .913f, -1.65f), new Vector3(.82f, .012f, .82f), color);

                Material surface = MaterialAsset($"CH01_ShapeSurface_{i}",
                    i == 0 ? Blue : i == 1 ? Violet : Cyan, false, .18f, .55f);
                GameObject shape = Shape(i, names[i], chapter.transform, home, surface, prism, true);
                Rigidbody body = shape.AddComponent<Rigidbody>();
                body.mass = .3f;
                body.linearDamping = 1.2f;
                body.angularDamping = 2f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                XRGrabInteractable grab = shape.AddComponent<XRGrabInteractable>();
                grab.movementType = XRBaseInteractable.MovementType.Kinematic;
                grab.throwOnDetach = false;
                grab.useDynamicAttach = true;
                ChapterPiece piece = shape.AddComponent<ChapterPiece>();
                piece.chapter = chapter;
                piece.identity = i;
                piece.home = homeMarker;
                piece.body = body;
                piece.grab = grab;
                UnityEventTools.AddPersistentListener<SelectEnterEventArgs>(grab.selectEntered, piece.OnGrabbed);
                UnityEventTools.AddPersistentListener<SelectExitEventArgs>(grab.selectExited, piece.OnReleased);
                chapter.pieces[i] = piece;
                chapter.interactables[i] = grab;

                Vector3 slotPosition = new Vector3(xs[i], 1.17f, depths[i]);
                chapter.slots[i] = Node("Slot " + names[i], chapter.transform, slotPosition);
                Primitive("Socket plinth", PrimitiveType.Cylinder, chapter.transform,
                    new Vector3(xs[i], .45f, depths[i]), new Vector3(.92f, .45f, .92f), p.panel, true);
                chapter.slotIndicators[i] = Primitive("Socket status ring", PrimitiveType.Cylinder, chapter.transform,
                    new Vector3(xs[i], .915f, depths[i]), new Vector3(.97f, .018f, .97f), color).GetComponent<Renderer>();
                SocketOutline(chapter.slots[i], i, color);
                Transform plaque = WorldCanvas("Depth plaque", chapter.transform,
                    new Vector3(xs[i], 1.95f, depths[i] + .1f), new Vector2(640, 165), .0015f, camera);
                Label(plaque, "Shape name", names[i], font, 12, 12, 616, 65, 33, Text, TextAlignmentOptions.Center);
                Label(plaque, "Depth measurement", $"{depths[i] + 3f:0.0} m FROM START", font,
                    12, 86, 616, 55, 27, Muted, TextAlignmentOptions.Center);
                for (int tick = 0; tick < i + 1; tick++)
                    Primitive("Depth tick", PrimitiveType.Cube, chapter.transform,
                        new Vector3(xs[i] + (tick - i * .5f) * .17f, .012f, depths[i] - .65f),
                        new Vector3(.1f, .015f, .28f), color);
            }
            return chapter;
        }

        private static ParallaxForestChapter BuildForest(Transform parent, TMP_FontAsset font, Camera camera)
        {
            Palette p = CreatePalette("CH02");
            ParallaxForestChapter chapter = Room<ParallaxForestChapter>(parent, 1, "PARALLAX / ALIEN FOREST", p, font, camera);
            Transform console = Console(chapter, "02   /   MOTION PARALLAX", "MOVE YOUR HEAD.\nREVEAL THE PATH.", p, font, camera);
            chapter.discoveryLabel = Label(console, "Discovery meter", "Inspect sideways: 0.00 / 0.22 m",
                font, 40, 570, 820, 120, 35, Cyan);
            Label(console, "Movement technique",
                "Near foliage shifts more than distant foliage.\n\nVR: gently lean sideways.\nDesktop: inspect from both sides below.\n\nSelect cyan landing pads after discovery.\nMovement between pads is an instant teleport.",
                font, 40, 710, 820, 250, 27, Muted);
            chapter.safePlatforms = new Transform[4];
            chapter.hiddenPlatformVisuals = new GameObject[4];
            chapter.safeButtons = new Button[4];
            chapter.interactables = Array.Empty<XRGrabInteractable>();
            chapter.revealSpan = .22f;
            UnityAction[] steps = { chapter.StepOne, chapter.StepTwo, chapter.StepThree, chapter.StepFour };
            var wrongButtons = new List<Button>
            {
                Button(console, "Desktop inspect left", "LOOK LEFT", font, 40, 975, 395, 90, Cyan, chapter.LookLeft),
                Button(console, "Desktop inspect right", "LOOK RIGHT", font, 465, 975, 395, 90, Cyan, chapter.LookRight)
            };
            float[] xs = { -.5f, .8f, -.4f, .8f };
            float[] zs = { -1.15f, .45f, 2.05f, 3.65f };
            for (int i = 0; i < 4; i++)
            {
                Vector3 destination = new Vector3(xs[i], 0f, zs[i]);
                chapter.safePlatforms[i] = Node("Checkpoint " + (i + 1), chapter.transform, destination);
                Transform visual = Node("Hidden landing pad " + (i + 1), chapter.transform, destination);
                chapter.hiddenPlatformVisuals[i] = visual.gameObject;
                Primitive("Safe platform", PrimitiveType.Cylinder, visual, new Vector3(0f, -.018f, 0f),
                    new Vector3(1.1f, .025f, 1.1f), p.panel);
                Primitive("Discovered rim", PrimitiveType.Cylinder, visual, new Vector3(0f, .009f, 0f),
                    new Vector3(1.14f, .009f, 1.14f), p.cyan);
                Primitive("Safe walking surface", PrimitiveType.Cylinder, visual, new Vector3(0f, .023f, 0f),
                    new Vector3(.98f, .009f, .98f), p.panel);
                Transform padUI = WorldCanvas("Landing selector", visual, new Vector3(0f, .85f, .42f),
                    new Vector2(560, 190), .002f, camera);
                chapter.safeButtons[i] = Button(padUI, $"Checkpoint {i + 1}",
                    $"0{i + 1}  /  LAND HERE", font, 15, 20, 530, 140, Cyan, steps[i]);
                visual.gameObject.SetActive(false);

                float wrongX = xs[i] > 0f ? -1.25f : 1.4f;
                Transform wrong = Node("Unstable island " + (i + 1), chapter.transform, new Vector3(wrongX, 0f, zs[i] + .3f));
                Primitive("Unstable island", PrimitiveType.Cylinder, wrong, new Vector3(0f, -.01f, 0f),
                    new Vector3(.78f, .026f, .78f), p.violet);
                Primitive("Broken marker", PrimitiveType.Cube, wrong, new Vector3(0f, .03f, 0f),
                    new Vector3(.4f, .015f, .05f), p.warning).transform.localRotation = Quaternion.Euler(0f, 30f, 0f);
                Transform wrongUI = WorldCanvas("Unverified selector", wrong, new Vector3(0f, .65f, .32f),
                    new Vector2(520, 200), .0017f, camera);
                wrongButtons.Add(Button(wrongUI, "Unverified landing", "?  /  UNVERIFIED\nLAND HERE", font,
                    10, 10, 500, 180, Warning, chapter.WrongPlatform));
            }
            chapter.interactionButtons = wrongButtons.ToArray();

            Material bark = MaterialAsset("CH02_AlienBark", Hex("153040"), false, .18f, .3f);
            Material nearLeaves = MaterialAsset("CH02_NearFoliage", Hex("135A66"), false, .1f, .55f);
            Material midLeaves = MaterialAsset("CH02_MidFoliage", Hex("243F68"), false, .05f, .4f);
            Material farLeaves = MaterialAsset("CH02_FarFoliage", Hex("342955"), false, 0f, .25f);
            Material[] leaves = { nearLeaves, midLeaves, farLeaves };
            for (int layer = 0; layer < 3; layer++)
            {
                float z = -.45f + layer * 2.65f;
                for (int side = -1; side <= 1; side += 2)
                    for (int tree = 0; tree < 3; tree++)
                    {
                        float x = side * (2.1f + tree * 1.05f);
                        float height = 1.65f + .3f * ((tree + layer) % 3);
                        Transform plant = Node($"Layer {layer + 1} alien tree", chapter.transform,
                            new Vector3(x, 0f, z + tree * .23f));
                        Primitive("Organic trunk", PrimitiveType.Cylinder, plant, new Vector3(0f, height * .5f, 0f),
                            new Vector3(.14f + layer * .025f, height * .5f, .14f), bark);
                        for (int branch = 0; branch < 3; branch++)
                        {
                            float branchSide = branch % 2 == 0 ? 1f : -1f;
                            Vector3 crown = new Vector3(branchSide * (.28f + branch * .09f),
                                height - .25f + branch * .29f, branch * .12f);
                            Rod("Branch", plant, new Vector3(0f, height * .65f, 0f), crown, .055f, bark);
                            Primitive("Alien leaf", PrimitiveType.Sphere, plant, crown,
                                new Vector3(.72f + branch * .12f, .16f, .42f), leaves[layer])
                                .transform.localRotation = Quaternion.Euler(0f, 20f * branch, branchSide * 14f);
                            Primitive("Bioluminescent seed", PrimitiveType.Sphere, plant, crown + Vector3.up * .13f,
                                Vector3.one * .09f, layer == 1 ? p.violet : p.cyan);
                        }
                        Primitive("Root bulb", PrimitiveType.Sphere, plant, new Vector3(0f, .12f, 0f),
                            new Vector3(.38f, .24f, .38f), leaves[layer]);
                    }
                Transform depthLabel = WorldCanvas("Forest layer sign", chapter.transform,
                    new Vector3(4.5f, 2.9f, z), new Vector2(650, 140), .0018f, camera);
                Label(depthLabel, "Layer", new[] { "NEAR / 2.6 m", "MID / 5.2 m", "FAR / 7.9 m" }[layer],
                    font, 12, 20, 626, 100, 38, layer == 1 ? Violet : Cyan, TextAlignmentOptions.Center);
            }
            return chapter;
        }

        private static IllusionChapter BuildIllusions(Transform parent, TMP_FontAsset font, Camera camera)
        {
            Palette p = CreatePalette("CH03");
            IllusionChapter chapter = Room<IllusionChapter>(parent, 2, "PERCEPTION / CONTEXT", p, font, camera, -3.8f);
            Transform console = Console(chapter, "03   /   PERCEPTUAL INFERENCE", "MEASURE REALITY.\nQUESTION CONTEXT.", p, font, camera);
            chapter.explanationLabel = Label(console, "Answer explanation", "Are the two cubes equal in actual size?",
                font, 40, 560, 820, 250, 31, Text);
            chapter.ponzoAnswerButtons = new[]
            {
                Button(console, "Equal cubes", "EQUAL SIZE", font, 40, 840, 395, 100, Cyan, chapter.AnswerPonzoEqual),
                Button(console, "Different cubes", "DIFFERENT", font, 465, 840, 395, 100, Violet, chapter.AnswerPonzoDifferent)
            };
            chapter.mullerAnswerButtons = new[]
            {
                Button(console, "Equal shafts", "EQUAL LENGTH", font, 40, 840, 395, 100, Cyan, chapter.AnswerMullerEqual),
                Button(console, "Different shafts", "DIFFERENT", font, 465, 840, 395, 100, Violet, chapter.AnswerMullerDifferent)
            };
            chapter.continueButton = Button(console, "Continue lesson", "CONTINUE  →", font,
                40, 970, 820, 95, Blue, chapter.ContinueLesson);
            chapter.interactionButtons = Array.Empty<Button>();
            chapter.interactables = Array.Empty<XRGrabInteractable>();
            foreach (Button button in chapter.mullerAnswerButtons) button.gameObject.SetActive(false);
            chapter.continueButton.gameObject.SetActive(false);

            Transform display = Node("Comparison gallery", chapter.transform, new Vector3(1.05f, 1.85f, 3.4f));
            Primitive("Gallery backplate", PrimitiveType.Cube, display, new Vector3(0f, 0f, .16f),
                new Vector3(5.5f, 3.7f, .13f), p.panel);
            Rod("Top gallery accent", display, new Vector3(-2.75f, 1.86f, .075f),
                new Vector3(2.75f, 1.86f, .075f), .025f, p.violet);
            Transform ponzo = Node("Ponzo equal physical cubes", display, Vector3.zero);
            chapter.ponzoDisplay = ponzo.gameObject;
            Rod("Left converging rail", ponzo, new Vector3(-1.7f, -1.45f, 0f), new Vector3(-.38f, 1.5f, 0f), .035f, p.muted);
            Rod("Right converging rail", ponzo, new Vector3(1.7f, -1.45f, 0f), new Vector3(.38f, 1.5f, 0f), .035f, p.muted);
            for (int i = 0; i < 8; i++)
            {
                float t = i / 7f;
                float y = Mathf.Lerp(-1.4f, 1.45f, t);
                float halfWidth = Mathf.Lerp(1.68f, .4f, t);
                Rod("Perspective crossbar", ponzo, new Vector3(-halfWidth, y, .025f),
                    new Vector3(halfWidth, y, .025f), .018f, p.muted);
            }
            Primitive("Ponzo cube A 0.5m", PrimitiveType.Cube, ponzo, new Vector3(0f, 1.02f, -.3f),
                Vector3.one * .5f, p.blue);
            Primitive("Ponzo cube B 0.5m", PrimitiveType.Cube, ponzo, new Vector3(0f, -.87f, -.3f),
                Vector3.one * .5f, p.blue);
            SmallPlaque(ponzo, new Vector3(1.7f, 1.05f, -.1f), "A", p, font, camera);
            SmallPlaque(ponzo, new Vector3(1.7f, -.87f, -.1f), "B", p, font, camera);

            Transform muller = Node("Muller Lyer equal shafts", display, Vector3.zero);
            chapter.mullerDisplay = muller.gameObject;
            for (int row = 0; row < 2; row++)
            {
                float y = row == 0 ? .7f : -.7f;
                Material material = row == 0 ? p.cyan : p.violet;
                Rod("Equal 2m shaft " + row, muller, new Vector3(-1f, y, -.04f), new Vector3(1f, y, -.04f), .035f, material);
                for (int side = -1; side <= 1; side += 2)
                    for (int vertical = -1; vertical <= 1; vertical += 2)
                    {
                        float finX = side * (row == 0 ? .62f : 1.38f);
                        Rod("Opposite arrow fin", muller, new Vector3(side, y, -.04f),
                            new Vector3(finX, y + vertical * .35f, -.04f), .035f, material);
                    }
                SmallPlaque(muller, new Vector3(-2.05f, y, -.05f), row == 0 ? "A" : "B", p, font, camera);
            }
            muller.gameObject.SetActive(false);
            Transform caption = WorldCanvas("Gallery caption", chapter.transform,
                new Vector3(1.05f, .5f, 2.7f), new Vector2(1250, 170), .0025f, camera);
            Label(caption, "Comparison rule", "COMPARE THE OBJECTS — NOT THEIR SURROUNDINGS",
                font, 20, 25, 1210, 120, 33, Muted, TextAlignmentOptions.Center);
            return chapter;
        }

        private static ReflectionChapter BuildReflection(Transform parent, TMP_FontAsset font, Camera camera)
        {
            Palette p = CreatePalette("CH04");
            ReflectionChapter chapter = Room<ReflectionChapter>(parent, 3, "LIGHT / SURFACE RESPONSE", p, font, camera);
            Transform console = Console(chapter, "04   /   OPTICAL MATERIALS", "CONTROL LIGHT.\nCLOSE THE CIRCUIT.", p, font, camera);
            chapter.instrumentLabel = Label(console, "Optical instrument", "LIGHT 0°  /  MIRROR −15°",
                font, 40, 565, 820, 130, 32, Cyan);
            Label(console, "Light control label", "DIRECTIONAL LIGHT / AZIMUTH", font, 40, 715, 820, 45, 25, Muted);
            var controls = new List<Button>
            {
                Button(console, "Light minus 25", "−25°", font, 40, 775, 255, 95, Blue, chapter.LightLeft),
                Button(console, "Light zero", "0°", font, 322, 775, 255, 95, Blue, chapter.LightCenter),
                Button(console, "Light plus 25", "+25°", font, 605, 775, 255, 95, Blue, chapter.LightRight),
                Button(console, "Mirror counterclockwise", "MIRROR −5°", font, 40, 940, 395, 95, Violet, chapter.RotateLeft),
                Button(console, "Mirror clockwise", "MIRROR +5°", font, 465, 940, 395, 95, Violet, chapter.RotateRight)
            };
            Label(console, "Mirror control label", "GRAB MIRROR OR USE FINE ROTATION", font, 40, 885, 820, 45, 25, Muted);
            chapter.interactionButtons = controls.ToArray();

            Vector3 mirrorPosition = new Vector3(1f, 1.35f, .8f);
            chapter.reflectorAnchor = Node("Fixed reflector axis", chapter.transform, mirrorPosition);
            chapter.reflector = Node("Rotatable reflector", chapter.transform, mirrorPosition);
            chapter.reflector.localRotation = Quaternion.Euler(0f, -15f, 0f);
            Material mirrorMaterial = MaterialAsset("CH04_PolishedReflector", Hex("CADAE4"), false, 1f, .98f);
            Primitive("Physical mirror panel", PrimitiveType.Cube, chapter.reflector, Vector3.zero,
                new Vector3(1.25f, 1.35f, .075f), mirrorMaterial, true);
            Rod("Reflector edge left", chapter.reflector, new Vector3(-.65f, -.7f, 0f), new Vector3(-.65f, .7f, 0f), .04f, p.violet);
            Rod("Reflector edge right", chapter.reflector, new Vector3(.65f, -.7f, 0f), new Vector3(.65f, .7f, 0f), .04f, p.violet);
            Rod("Reflector edge top", chapter.reflector, new Vector3(-.65f, .7f, 0f), new Vector3(.65f, .7f, 0f), .04f, p.violet);
            Primitive("Mirror pedestal", PrimitiveType.Cylinder, chapter.transform, new Vector3(1f, .28f, .8f),
                new Vector3(.35f, .28f, .35f), p.panel, true);
            chapter.reflectorBody = chapter.reflector.gameObject.AddComponent<Rigidbody>();
            chapter.reflectorBody.isKinematic = true;
            chapter.reflectorBody.useGravity = false;
            chapter.reflectorBody.constraints = RigidbodyConstraints.FreezePosition |
                RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            chapter.reflectorGrab = chapter.reflector.gameObject.AddComponent<XRGrabInteractable>();
            chapter.reflectorGrab.trackPosition = false;
            chapter.reflectorGrab.trackRotation = true;
            chapter.reflectorGrab.throwOnDetach = false;
            chapter.reflectorGrab.movementType = XRBaseInteractable.MovementType.Kinematic;
            chapter.interactables = new[] { chapter.reflectorGrab };

            chapter.receiver = Node("Receiver center", chapter.transform, new Vector3(3.4f, 1.35f, 4f));
            chapter.receiverRenderer = Primitive("Receiver target", PrimitiveType.Sphere, chapter.receiver, Vector3.zero,
                Vector3.one * .68f, p.warning).GetComponent<Renderer>();
            Primitive("Receiver pedestal", PrimitiveType.Cylinder, chapter.transform, new Vector3(3.4f, .48f, 4f),
                new Vector3(.48f, .48f, .48f), p.panel, true);
            chapter.receiverIdleMaterial = p.warning;
            chapter.receiverHitMaterial = p.success;
            chapter.receiverRadius = .34f;
            SmallPlaque(chapter.transform, new Vector3(3.4f, 2.12f, 4f), "RECEIVER", p, font, camera);
            chapter.emitter = Node("Steerable light emitter", chapter.transform, mirrorPosition - Vector3.forward * 2.8f);
            Primitive("Emitter core", PrimitiveType.Sphere, chapter.emitter, Vector3.zero, Vector3.one * .17f, p.blue);
            Primitive("Emitter housing", PrimitiveType.Cylinder, chapter.emitter, Vector3.down * .3f,
                new Vector3(.12f, .26f, .12f), p.panel);
            chapter.incomingBeam = Beam("Incident beam", chapter.transform, p.prefix, Blue);
            chapter.reflectedBeam = Beam("Reflected beam", chapter.transform, p.prefix, Warning);
            chapter.incomingBeam.SetPosition(0, chapter.emitter.position);
            chapter.incomingBeam.SetPosition(1, chapter.reflector.position);
            chapter.reflectedBeam.SetPosition(0, chapter.reflector.position);
            chapter.reflectedBeam.SetPosition(1, chapter.reflector.position +
                Vector3.Reflect(Vector3.forward, chapter.reflector.forward) * 6f);

            Transform light = Node("Adjustable directional light", chapter.transform, new Vector3(0f, 3.5f, 0f));
            chapter.keyLight = light.gameObject.AddComponent<Light>();
            chapter.keyLight.type = LightType.Directional;
            chapter.keyLight.color = new Color(.86f, .94f, 1f);
            chapter.keyLight.intensity = 1.65f;
            chapter.keyLight.shadows = LightShadows.None;
            Transform probeNode = Node("Material gallery reflection probe", chapter.transform, new Vector3(0f, 1.8f, 1f));
            ReflectionProbe probe = probeNode.gameObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.resolution = 128;
            probe.size = new Vector3(11f, 4f, 12f);
            probe.boxProjection = true;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = Secondary;
            probe.nearClipPlane = .1f;
            probe.farClipPlane = 20f;
            Material[] samples =
            {
                MaterialAsset("CH04_SampleMetalPolished", Hex("B2C7D9"), false, 1f, .96f),
                MaterialAsset("CH04_SampleMetalRough", Hex("B2C7D9"), false, 1f, .16f),
                MaterialAsset("CH04_SampleCeramic", Hex("00C8FF"), false, 0f, .24f)
            };
            string[] sampleNames = { "POLISHED METAL\nM 1.00 / R 0.04", "ROUGH METAL\nM 1.00 / R 0.84", "MATTE CERAMIC\nM 0.00 / R 0.76" };
            for (int i = 0; i < samples.Length; i++)
            {
                float x = -2.35f + i * 1.05f;
                Primitive("Material sample plinth", PrimitiveType.Cylinder, chapter.transform,
                    new Vector3(x, .48f, 2.7f), new Vector3(.72f, .48f, .72f), p.panel, true);
                Primitive(sampleNames[i], PrimitiveType.Sphere, chapter.transform,
                    new Vector3(x, 1.28f, 2.7f), Vector3.one * .62f, samples[i]);
                Transform label = WorldCanvas("PBR sample label", chapter.transform, new Vector3(x, 1.95f, 2.7f),
                    new Vector2(650, 210), .00145f, camera);
                Label(label, "Material properties", sampleNames[i], font, 8, 15, 634, 180, 34, Text, TextAlignmentOptions.Center);
            }
            return chapter;
        }

        private static T Room<T>(Transform parent, int index, string title, Palette p, TMP_FontAsset font, Camera camera,
            float exitX = 0f) where T : LabChapter
        {
            Transform root = Node($"Chapter {index + 1:00} - {title}", parent, Vector3.zero);
            root.gameObject.SetActive(false);
            root.position = new Vector3(0f, 0f, 24f * (index + 1));
            root.rotation = Quaternion.identity;
            T chapter = root.gameObject.AddComponent<T>();
            chapter.chapterIndex = index;
            chapter.spawnPoint = Node("Spawn - ground level", root, new Vector3(0f, 0f, -3f));
            chapter.threeStarSeconds = index == 1 ? 150f : 180f;
            chapter.twoStarSeconds = 360f;
            chapter.threeStarMistakes = 0;
            chapter.twoStarMistakes = 3;
            Primitive("Lab floor", PrimitiveType.Cube, root, new Vector3(0f, -.12f, 0f),
                new Vector3(12f, .24f, 14f), p.floor, true);
            Primitive("Back wall", PrimitiveType.Cube, root, new Vector3(0f, 1.8f, 7f),
                new Vector3(12f, 3.6f, .2f), p.wall, true);
            for (int side = -1; side <= 1; side += 2)
            {
                Primitive("Side wall", PrimitiveType.Cube, root, new Vector3(side * 6f, 1.8f, 0f),
                    new Vector3(.2f, 3.6f, 14f), p.wall, true);
                Primitive("Floor perimeter light", PrimitiveType.Cube, root, new Vector3(side * 5.7f, .016f, 0f),
                    new Vector3(.025f, .025f, 13.4f), p.blue);
                Primitive("Wall horizon", PrimitiveType.Cube, root, new Vector3(side * 5.88f, 1.05f, 0f),
                    new Vector3(.025f, .025f, 13f), p.muted);
                for (int rib = 0; rib < 5; rib++)
                {
                    Primitive("Structural rib", PrimitiveType.Cube, root, new Vector3(side * 5.8f, 1.8f, -5.5f + rib * 2.75f),
                        new Vector3(.23f, 3.6f, .2f), p.panel);
                    Primitive("Rib status", PrimitiveType.Cube, root, new Vector3(side * 5.66f, 2.5f, -5.5f + rib * 2.75f),
                        new Vector3(.025f, .4f, .08f), index % 2 == 0 ? p.cyan : p.violet);
                }
            }
            for (int x = -4; x <= 4; x += 2)
                Primitive("Floor longitudinal seam", PrimitiveType.Cube, root, new Vector3(x, .005f, 0f),
                    new Vector3(.012f, .006f, 13.6f), p.panel);
            for (int z = -6; z <= 6; z += 2)
                Primitive("Floor transverse seam", PrimitiveType.Cube, root, new Vector3(0f, .005f, z),
                    new Vector3(11.6f, .006f, .012f), p.panel);
            for (int i = 0; i < 2; i++)
            {
                Transform lamp = Node("Soft laboratory light", root, new Vector3(i == 0 ? -3f : 3f, 3f, i == 0 ? -2.5f : 3f));
                Light light = lamp.gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = i == 0 ? new Color(.5f, .72f, 1f) : new Color(.45f, 1f, .85f);
                light.range = 10f;
                light.intensity = 2f;
                light.shadows = LightShadows.None;
                Primitive("Light housing", PrimitiveType.Cube, lamp, Vector3.zero, new Vector3(.6f, .07f, .5f), p.text);
            }
            Transform exit = Node("Puzzle gated exit", root, new Vector3(exitX, 0f, 6f));
            Primitive("Exit left jamb", PrimitiveType.Cube, exit, new Vector3(-.9f, 1.4f, 0f), new Vector3(.14f, 2.8f, .18f), p.panel);
            Primitive("Exit right jamb", PrimitiveType.Cube, exit, new Vector3(.9f, 1.4f, 0f), new Vector3(.14f, 2.8f, .18f), p.panel);
            chapter.exitRenderer = Primitive("Exit readiness lintel", PrimitiveType.Cube, exit, new Vector3(0f, 2.82f, 0f),
                new Vector3(1.95f, .12f, .18f), p.error).GetComponent<Renderer>();
            Primitive("Exit threshold", PrimitiveType.Cube, exit, new Vector3(0f, .02f, 0f), new Vector3(1.8f, .025f, .4f), p.panel);
            Transform exitUI = WorldCanvas("Exit use panel", exit, new Vector3(0f, 1.55f, -.14f),
                new Vector2(700, 500), .002f, camera);
            Label(exitUI, "Exit label", "CHAPTER COMPLETE", font, 20, 30, 660, 70, 38, Text, TextAlignmentOptions.Center);
            Label(exitUI, "Exit condition", "SOLVE THE EXPERIMENT\nTHEN USE THIS EXIT", font, 25, 120, 650, 125, 31, Muted, TextAlignmentOptions.Center);
            chapter.exitButton = Button(exitUI, "Use chapter exit", "USE EXIT  →", font, 50, 295, 600, 135, Success, chapter.UseExit);
            chapter.exitButton.interactable = false;
            chapter.lockedExitMaterial = p.error;
            chapter.openExitMaterial = p.success;
            EnsureExitDoor(chapter);
            return chapter;
        }

        public static void EnsureExitDoor(LabChapter chapter)
        {
            if (chapter.exitDoor != null) return;
            Transform exit = chapter.exitRenderer.transform.parent;
            var door = exit.gameObject.AddComponent<DepthLabExitDoor>();
            door.chapter = chapter;
            Material surface = chapter.transform.Find("Lab floor").GetComponent<Renderer>().sharedMaterial;
            door.leftPanel = Primitive("Sliding exit left panel", PrimitiveType.Cube, exit,
                new Vector3(-.44f, 1.35f, .08f), new Vector3(.86f, 2.65f, .08f), surface).transform;
            door.rightPanel = Primitive("Sliding exit right panel", PrimitiveType.Cube, exit,
                new Vector3(.44f, 1.35f, .08f), new Vector3(.86f, 2.65f, .08f), surface).transform;
            chapter.exitDoor = door;
        }

        private static Transform Console(LabChapter chapter, string eyebrow, string title, Palette p, TMP_FontAsset font, Camera camera)
        {
            Transform canvas = WorldCanvas("Chapter instrument console", chapter.transform, new Vector3(-3.55f, 1.8f, -.7f),
                new Vector2(900, 1100), .0024f, camera);
            canvas.localRotation = Quaternion.Euler(0f, -18f, 0f);
            Image background = canvas.gameObject.AddComponent<Image>();
            background.color = Panel;
            background.raycastTarget = true;
            Label(canvas, "Chapter eyebrow", eyebrow, font, 40, 25, 820, 48, 25, Cyan);
            Label(canvas, "Chapter title", title, font, 40, 88, 820, 145, 49, Text);
            chapter.objectiveLabel = Label(canvas, "Objective", chapter.Objective, font, 40, 255, 820, 125, 30, Text);
            chapter.hintLabel = Label(canvas, "Hint", chapter.Hint, font, 40, 393, 820, 125, 26, Muted);
            chapter.progressLabel = Label(canvas, "Progress", $"00 / {chapter.Goal:00}", font, 40, 520, 820, 42, 30, Cyan);
            return canvas;
        }

        private static Transform WorldCanvas(string name, Transform parent, Vector3 localPosition, Vector2 size, float scale, Camera camera)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.localPosition = localPosition;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * scale;
            rect.sizeDelta = size;
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.sortingOrder = 5;
            return rect;
        }

        private static TMP_Text Label(Transform parent, string name, string text, TMP_FontAsset font,
            float x, float y, float width, float height, float fontSize, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = alignment;
            label.enableAutoSizing = true;
            label.fontSizeMin = fontSize * .78f;
            label.fontSizeMax = fontSize;
            label.raycastTarget = false;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        private static Button Button(Transform parent, string name, string text, TMP_FontAsset font,
            float x, float y, float width, float height, Color accent, UnityAction callback)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            Image image = go.GetComponent<Image>();
            image.color = Color.Lerp(Panel, accent, .22f);
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f);
            colors.pressedColor = new Color(.65f, .9f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(.28f, .32f, .38f, .6f);
            colors.fadeDuration = .08f;
            button.colors = colors;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            Label(rect, "Button label", text, font, 15, 8, width - 30, height - 16, 33, Text, TextAlignmentOptions.Center);
            UnityEventTools.AddPersistentListener(button.onClick, callback);
            return button;
        }

        private static void SmallPlaque(Transform parent, Vector3 position, string text, Palette p, TMP_FontAsset font, Camera camera)
        {
            Transform canvas = WorldCanvas(text + " plaque", parent, position, new Vector2(700, 180), .0015f, camera);
            Label(canvas, "Label", text, font, 15, 20, 670, 140, 45, Text, TextAlignmentOptions.Center);
        }

        private static Transform Node(string name, Transform parent, Vector3 position)
        {
            Transform node = new GameObject(name).transform;
            node.SetParent(parent, false);
            node.localPosition = position;
            return node;
        }

        private static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position,
            Vector3 scale, Material material, bool collider = false)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            Renderer renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static void Rod(string name, Transform parent, Vector3 from, Vector3 to, float thickness, Material material)
        {
            Vector3 direction = to - from;
            GameObject rod = Primitive(name, PrimitiveType.Cylinder, parent, (from + to) * .5f,
                new Vector3(thickness, direction.magnitude * .5f, thickness), material);
            rod.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
        }

        private static GameObject Shape(int identity, string name, Transform parent, Vector3 position,
            Material material, Mesh prism, bool collider)
        {
            if (identity < 2)
                return Primitive(name, identity == 0 ? PrimitiveType.Cube : PrimitiveType.Sphere, parent,
                    position, Vector3.one * .46f, material, collider);
            Transform node = Node(name, parent, position);
            node.localScale = Vector3.one * .46f;
            node.gameObject.AddComponent<MeshFilter>().sharedMesh = prism;
            MeshRenderer renderer = node.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            if (collider)
            {
                MeshCollider meshCollider = node.gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = prism;
                meshCollider.convex = true;
            }
            return node.gameObject;
        }

        private static void SocketOutline(Transform parent, int identity, Material material)
        {
            const float half = .28f;
            if (identity == 0)
            {
                for (int face = -1; face <= 1; face += 2)
                {
                    float z = face * half;
                    Rod("Cube outline", parent, new Vector3(-half, -half, z), new Vector3(half, -half, z), .018f, material);
                    Rod("Cube outline", parent, new Vector3(-half, half, z), new Vector3(half, half, z), .018f, material);
                    Rod("Cube outline", parent, new Vector3(-half, -half, z), new Vector3(-half, half, z), .018f, material);
                    Rod("Cube outline", parent, new Vector3(half, -half, z), new Vector3(half, half, z), .018f, material);
                }
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        Rod("Cube depth edge", parent, new Vector3(x * half, y * half, -half),
                            new Vector3(x * half, y * half, half), .018f, material);
            }
            else if (identity == 1)
            {
                for (int axis = 0; axis < 2; axis++)
                    for (int segment = 0; segment < 24; segment++)
                    {
                        float a = segment * Mathf.PI / 12f;
                        float b = (segment + 1) * Mathf.PI / 12f;
                        Vector3 from = axis == 0 ? new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        Vector3 to = axis == 0 ? new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0f) : new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b));
                        Rod("Sphere outline", parent, from * half, to * half, .014f, material);
                    }
            }
            else
            {
                Vector3[] triangle = { new Vector3(-half, -half, 0f), new Vector3(half, -half, 0f), new Vector3(0f, half, 0f) };
                for (int i = 0; i < 3; i++)
                {
                    int next = (i + 1) % 3;
                    Rod("Prism front outline", parent, triangle[i] + Vector3.back * half, triangle[next] + Vector3.back * half, .018f, material);
                    Rod("Prism back outline", parent, triangle[i] + Vector3.forward * half, triangle[next] + Vector3.forward * half, .018f, material);
                    Rod("Prism depth edge", parent, triangle[i] + Vector3.back * half, triangle[i] + Vector3.forward * half, .018f, material);
                }
            }
        }

        private static LineRenderer Beam(string name, Transform parent, string prefix, Color color)
        {
            Transform node = Node(name, parent, Vector3.zero);
            LineRenderer line = node.gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = BeamMaterial(prefix);
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = line.endWidth = .022f;
            line.numCapVertices = 6;
            line.startColor = line.endColor = color;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private static Material BeamMaterial(string prefix)
        {
            string path = $"{AssetFolder}/{prefix}_Beam.asset";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("A vertex-color beam shader is required.");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Palette CreatePalette(string prefix)
        {
            return new Palette
            {
                prefix = prefix,
                floor = MaterialAsset(prefix + "_Floor", Background, false, .1f, .35f),
                wall = MaterialAsset(prefix + "_Wall", Secondary, false, .15f, .25f),
                panel = MaterialAsset(prefix + "_Panel", Panel, false, .3f, .5f),
                blue = MaterialAsset(prefix + "_Blue", Blue, true),
                cyan = MaterialAsset(prefix + "_Cyan", Cyan, true),
                violet = MaterialAsset(prefix + "_Violet", Violet, true),
                warning = MaterialAsset(prefix + "_Warning", Warning, true),
                error = MaterialAsset(prefix + "_Error", Error, true),
                success = MaterialAsset(prefix + "_Success", Success, true),
                text = MaterialAsset(prefix + "_White", Text, true),
                muted = MaterialAsset(prefix + "_Muted", Muted * .55f, true)
            };
        }

        private static Material MaterialAsset(string name, Color color, bool unlit, float metallic = 0f, float smoothness = .4f)
        {
            string path = $"{AssetFolder}/{name}.asset";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("DepthLab requires Universal Render Pipeline shaders.");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            if (!unlit)
            {
                material.SetFloat("_Metallic", metallic);
                material.SetFloat("_Smoothness", smoothness);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh PrismMesh(string prefix)
        {
            string path = $"{AssetFolder}/{prefix}_TriangularPrism.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            Vector3[] corners =
            {
                new Vector3(-.5f, -.5f, -.5f), new Vector3(.5f, -.5f, -.5f), new Vector3(0f, .5f, -.5f),
                new Vector3(-.5f, -.5f, .5f), new Vector3(.5f, -.5f, .5f), new Vector3(0f, .5f, .5f)
            };
            int[] indices = { 0, 2, 1, 3, 4, 5, 0, 1, 4, 0, 4, 3, 1, 2, 5, 1, 5, 4, 2, 0, 3, 2, 3, 5 };
            var vertices = new Vector3[indices.Length];
            var triangles = new int[indices.Length];
            for (int i = 0; i < indices.Length; i++) { vertices[i] = corners[indices[i]]; triangles[i] = i; }
            mesh = new Mesh { name = prefix + " triangular prism", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/DepthLab"))
                AssetDatabase.CreateFolder("Assets", "DepthLab");
            if (!AssetDatabase.IsValidFolder(AssetFolder))
                AssetDatabase.CreateFolder("Assets/DepthLab", "Generated");
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            return color;
        }
    }
}
