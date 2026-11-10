using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace DepthLab
{
    [DisallowMultipleComponent]
    public sealed class InteractiveSocketVFX : MonoBehaviour
    {
        public ChapterPiece piece;
        public DepthSortingChapter chapter;
        public DepthLabApp app;
        public Camera headCamera;
        public Renderer[] sourceRenderers = Array.Empty<Renderer>();
        public Material highlightMaterial;
        public ParticleSystem snapParticles;
        public AudioSource audioSource;
        public AudioClip successCue, errorCue, grabCue, releaseCue;
        public bool environmentController;
        public InteractiveSocketVFX[] pieceEffects = Array.Empty<InteractiveSocketVFX>();
        public ParticleSystem ambientDust;
        public Renderer[] doorEnergy = Array.Empty<Renderer>();
        public Renderer[] targetRings = Array.Empty<Renderer>();

        private const float SnapDuration = .25f;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color Cyan = new Color(.05f, .82f, 1f, 1f);
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>(32);
        private readonly List<Vector3> uiRay = new List<Vector3>(2);
        private MaterialPropertyBlock block;
        private Transform proxyRoot;
        private MeshRenderer[] proxies, highlights;
        private bool[] originalForceOff;
        private MaterialPropertyBlock[] doorBlocks, ringBlocks;
        private Color[] ringColors;
        private int[] ringStates;
        private EventSystem pointerEvents;
        private PointerEventData pointer;
        private TrackedDeviceEventData trackedPointer;
        private InteractiveSocketVFX desktopTarget;
        private bool initialized, subscribed, snapping, successLatched, wasPlaced, wasHeld;
        private bool desktopHovered, xrHovered, permitted, focused = true, applicationPaused;
        private bool visualHeld, visualHovered;
        private float snapStarted, nextHover, releaseAt = -1f, lastDoorAmount = -1f;
        private Vector3 releasePosition, previousPosition;
        private Quaternion releaseRotation, previousRotation;
        private int mistakes;
        private XRBaseInputInteractor hapticInteractor;
        private float lastXRRelease = float.NegativeInfinity;

        private bool Allowed => initialized && focused && !applicationPaused && Time.timeScale > 0f &&
            app != null && !app.IsPaused && chapter != null && chapter.InteractionAllowed;

        private void Awake()
        {
            if (app == null || chapter == null || chapter.chapterIndex != 0 || headCamera == null ||
                audioSource == null || successCue == null || errorCue == null || grabCue == null || releaseCue == null)
            {
                Fail("Chapter 1 effects are missing the app, chapter, camera or audio references. Re-apply the effects setup.");
                return;
            }
            block = new MaterialPropertyBlock();
            if (environmentController)
            {
                if (piece != null || chapter.exitDoor == null || ambientDust == null ||
                    pieceEffects == null || pieceEffects.Length != 3 ||
                    targetRings == null || targetRings.Length != 3 ||
                    doorEnergy == null || doorEnergy.Length == 0 ||
                    chapter.emptySlotMaterials == null || chapter.emptySlotMaterials.Length != 3)
                {
                    Fail("Chapter 1 environment effects are missing piece, target ring, exit or dust references.");
                    return;
                }
                for (int i = 0; i < pieceEffects.Length; i++)
                    if (pieceEffects[i] == null || pieceEffects[i].piece == null ||
                        pieceEffects[i].piece.identity != i || targetRings[i] == null)
                    {
                        Fail("Chapter 1 effect pieces and target rings must be assigned in identity order.");
                        return;
                    }
                for (int i = 0; i < doorEnergy.Length; i++)
                    if (doorEnergy[i] == null) { Fail("The exit energy frame reference is missing."); return; }
                CacheEnvironment();
            }
            else
            {
                if (piece == null || piece.chapter != chapter || piece.body == null || piece.grab == null ||
                    snapParticles == null || highlightMaterial == null || sourceRenderers == null ||
                    sourceRenderers.Length == 0)
                {
                    Fail("Interaction effects are missing the source mesh, physics body, grab component or particle references.");
                    return;
                }
                for (int i = 0; i < sourceRenderers.Length; i++)
                {
                    Renderer source = sourceRenderers[i];
                    MeshFilter filter = source != null ? source.GetComponent<MeshFilter>() : null;
                    if (!(source is MeshRenderer) || filter == null || filter.sharedMesh == null ||
                        !source.transform.IsChildOf(piece.transform))
                    {
                        Fail("Interaction effects only support the piece's own static source meshes.");
                        return;
                    }
                }
                BuildRenderProxies();
            }
            initialized = true;
        }

        private void Fail(string message)
        {
            Debug.LogError(message, this);
            enabled = false;
        }

        private void OnEnable()
        {
            if (!initialized) return;
            permitted = false;
            releaseAt = -1f;
            if (environmentController)
            {
                mistakes = app.Mistakes;
                nextHover = 0f;
                lastDoorAmount = -1f;
                for (int i = 0; i < ringStates.Length; i++) ringStates[i] = -1;
                UpdateEnvironmentVisuals();
                return;
            }
            RestoreOriginals();
            wasPlaced = successLatched = piece.IsPlaced;
            wasHeld = false;
            previousPosition = piece.transform.position;
            previousRotation = piece.transform.rotation;
            piece.grab.hoverEntered.AddListener(HoverEntered);
            piece.grab.hoverExited.AddListener(HoverExited);
            piece.grab.selectEntered.AddListener(SelectEntered);
            piece.grab.selectExited.AddListener(SelectExited);
            subscribed = true;
            xrHovered = piece.grab.isHovered;
            SetHighlight(false, false);
        }

        private void OnDisable()
        {
            if (subscribed && piece != null && piece.grab != null)
            {
                piece.grab.hoverEntered.RemoveListener(HoverEntered);
                piece.grab.hoverExited.RemoveListener(HoverExited);
                piece.grab.selectEntered.RemoveListener(SelectEntered);
                piece.grab.selectExited.RemoveListener(SelectExited);
            }
            subscribed = false;
            hapticInteractor = null;
            if (!initialized) return;
            Suspend();
            if (environmentController)
            {
                ambientDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                for (int i = 0; i < doorEnergy.Length; i++)
                    if (doorEnergy[i] != null) doorEnergy[i].SetPropertyBlock(doorBlocks[i]);
                for (int i = 0; i < targetRings.Length; i++)
                    if (targetRings[i] != null) targetRings[i].SetPropertyBlock(ringBlocks[i]);
            }
        }

        private void OnDestroy()
        {
            RestoreOriginals();
            if (proxyRoot != null) Destroy(proxyRoot.gameObject);
            if (highlights == null) return;
            for (int i = 0; i < highlights.Length; i++)
                if (highlights[i] != null) Destroy(highlights[i].gameObject);
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (paused && initialized) Suspend();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
            if (!hasFocus && initialized) Suspend();
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            bool allowed = Allowed;
            if (!allowed)
            {
                if (permitted || snapping || wasHeld) Suspend();
                if (!environmentController)
                {
                    wasPlaced = successLatched = piece.IsPlaced;
                    previousPosition = piece.transform.position;
                    previousRotation = piece.transform.rotation;
                }
                else mistakes = app.Mistakes;
                return;
            }
            if (!permitted)
            {
                permitted = true;
                if (environmentController) ambientDust.Play(true);
                else xrHovered = piece.grab.isHovered;
            }
            if (environmentController)
            {
                UpdateEnvironment();
                return;
            }

            bool placed = piece.IsPlaced;
            if (!placed && wasPlaced)
            {
                RestoreOriginals();
                snapParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                audioSource.Stop();
                successLatched = false;
                releaseAt = -1f;
            }
            if (placed && !wasPlaced && !successLatched)
                NotifySnapped(previousPosition, previousRotation);
            wasPlaced = placed;
            bool held = !placed && IsHeld();
            ChangeHeld(held);
            if (releaseAt >= 0f && Time.unscaledTime >= releaseAt)
            {
                releaseAt = -1f;
                if (!placed && !held) Play(releaseCue, .78f);
            }
            if (snapping)
            {
                float t = Mathf.Clamp01((Time.time - snapStarted) / SnapDuration);
                float ease = t * t * (3f - 2f * t);
                proxyRoot.SetPositionAndRotation(
                    Vector3.Lerp(releasePosition, piece.transform.position, ease),
                    Quaternion.Slerp(releaseRotation, piece.transform.rotation, ease));
                if (t >= 1f) RestoreOriginals();
            }
            SetHighlight(held, !placed && (desktopHovered || xrHovered));
            previousPosition = piece.transform.position;
            previousRotation = piece.transform.rotation;
        }

        /// <summary>Call once after SnapTo has positioned the body, passing its pre-snap world pose.</summary>
        public void NotifySnapped(Vector3 previousPosition, Quaternion previousRotation)
        {
            if (!initialized || environmentController || piece == null || !piece.IsPlaced || successLatched)
                return;
            successLatched = wasPlaced = true;
            wasHeld = false;
            releaseAt = -1f;
            RestoreOriginals();
            SetHighlight(false, false);
            if (!isActiveAndEnabled || !Allowed) return;

            releasePosition = previousPosition;
            releaseRotation = previousRotation;
            proxyRoot.localScale = piece.transform.localScale;
            proxyRoot.SetPositionAndRotation(releasePosition, releaseRotation);
            for (int i = 0; i < sourceRenderers.Length; i++)
            {
                Renderer source = sourceRenderers[i];
                originalForceOff[i] = source.forceRenderingOff;
                proxies[i].enabled = source.enabled && source.gameObject.activeInHierarchy && !originalForceOff[i];
                source.GetPropertyBlock(block);
                proxies[i].SetPropertyBlock(block);
                int materialCount = source.GetComponent<MeshFilter>().sharedMesh.subMeshCount;
                for (int sub = 0; sub < materialCount; sub++)
                {
                    source.GetPropertyBlock(block, sub);
                    proxies[i].SetPropertyBlock(block, sub);
                }
                source.forceRenderingOff = true;
            }
            snapping = true;
            snapStarted = Time.time;
            proxyRoot.gameObject.SetActive(true);
            EmitSnapRing();
            Play(successCue, 1.16f);
            if (hapticInteractor != null && Time.unscaledTime - lastXRRelease < 1f)
                hapticInteractor.SendHapticImpulse(.4f, .12f);
        }

        private bool IsHeld()
        {
            return piece.IsHeld || piece.grab.isSelected;
        }

        private void ChangeHeld(bool held)
        {
            if (wasHeld == held) return;
            wasHeld = held;
            if (held)
            {
                releaseAt = -1f;
                Play(grabCue, 1.4f);
            }
            else if (!piece.IsPlaced)
                releaseAt = Time.unscaledTime + .06f;
        }

        private void HoverEntered(HoverEnterEventArgs args)
        {
            xrHovered = true;
            if (Allowed && !piece.IsPlaced) SetHighlight(wasHeld, true);
        }

        private void HoverExited(HoverExitEventArgs args)
        {
            xrHovered = piece.grab.isHovered;
            if (Allowed) SetHighlight(wasHeld, !piece.IsPlaced && (xrHovered || desktopHovered));
        }

        private void SelectEntered(SelectEnterEventArgs args)
        {
            if (!Allowed || piece.IsPlaced) return;
            hapticInteractor = args.interactorObject as XRBaseInputInteractor;
            if (hapticInteractor != null) hapticInteractor.SendHapticImpulse(.22f, .06f);
            ChangeHeld(true);
            SetHighlight(true, true);
        }

        private void SelectExited(SelectExitEventArgs args)
        {
            lastXRRelease = args.isCanceled ? float.NegativeInfinity : Time.unscaledTime;
            if (args.isCanceled || !Allowed)
            {
                wasHeld = false;
                releaseAt = -1f;
                SetHighlight(false, false);
                return;
            }
            ChangeHeld(piece.grab.isSelected && !piece.IsPlaced);
        }

        private void SetDesktopHover(bool hovered)
        {
            desktopHovered = hovered;
            if (initialized && !environmentController)
                SetHighlight(Allowed && wasHeld, Allowed && !piece.IsPlaced && (hovered || xrHovered));
        }

        private void SetHighlight(bool held, bool hovered)
        {
            if (highlights == null ||
                (!held && !hovered && visualHeld == held && visualHovered == hovered)) return;
            visualHeld = held;
            visualHovered = hovered;
            Color color = Cyan;
            color.a = (held ? .32f : .18f) * (.86f + .14f * Mathf.Sin(Time.time * 2f));
            block.Clear();
            block.SetColor(BaseColor, color);
            for (int i = 0; i < highlights.Length; i++)
            {
                highlights[i].enabled = (held || hovered) && sourceRenderers[i].enabled &&
                    !sourceRenderers[i].forceRenderingOff;
                highlights[i].SetPropertyBlock(block);
            }
        }

        private void Suspend()
        {
            permitted = false;
            releaseAt = -1f;
            wasHeld = desktopHovered = xrHovered = false;
            audioSource.Stop();
            if (environmentController)
            {
                SetDesktopTarget(null);
                if (ambientDust.isPlaying) ambientDust.Pause(true);
                // Pause/reset must also restore pieces when script execution order differs.
                for (int i = 0; i < pieceEffects.Length; i++)
                    if (pieceEffects[i] != null && pieceEffects[i].initialized) pieceEffects[i].Suspend();
            }
            else
            {
                RestoreOriginals();
                SetHighlight(false, false);
                snapParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void RestoreOriginals()
        {
            if (snapping && sourceRenderers != null)
                for (int i = 0; i < sourceRenderers.Length; i++)
                    if (sourceRenderers[i] != null) sourceRenderers[i].forceRenderingOff = originalForceOff[i];
            snapping = false;
            if (proxyRoot != null) proxyRoot.gameObject.SetActive(false);
        }

        private void BuildRenderProxies()
        {
            var root = new GameObject("Snap Render Proxy");
            root.SetActive(false);
            proxyRoot = root.transform;
            proxyRoot.SetParent(piece.transform.parent, false);
            proxyRoot.localScale = piece.transform.localScale;
            proxies = new MeshRenderer[sourceRenderers.Length];
            highlights = new MeshRenderer[sourceRenderers.Length];
            originalForceOff = new bool[sourceRenderers.Length];
            var transforms = new Dictionary<Transform, Transform> { { piece.transform, proxyRoot } };
            for (int i = 0; i < sourceRenderers.Length; i++)
            {
                MeshRenderer source = (MeshRenderer)sourceRenderers[i];
                Mesh mesh = source.GetComponent<MeshFilter>().sharedMesh;
                Transform node = CopyTransform(source.transform, transforms);
                node.gameObject.layer = source.gameObject.layer;
                node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer proxy = node.gameObject.AddComponent<MeshRenderer>();
                Material[] materials = source.sharedMaterials;
                proxy.sharedMaterials = materials;
                proxy.shadowCastingMode = source.shadowCastingMode;
                proxy.receiveShadows = source.receiveShadows;
                proxy.lightProbeUsage = source.lightProbeUsage;
                proxy.reflectionProbeUsage = source.reflectionProbeUsage;
                proxy.probeAnchor = source.probeAnchor;
                proxy.renderingLayerMask = source.renderingLayerMask;
                proxies[i] = proxy;

                var shell = new GameObject("Grab Outline");
                shell.layer = source.gameObject.layer;
                shell.transform.SetParent(source.transform, false);
                shell.transform.localScale = Vector3.one * 1.035f;
                shell.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer outline = shell.AddComponent<MeshRenderer>();
                var outlineMaterials = new Material[materials.Length];
                for (int sub = 0; sub < outlineMaterials.Length; sub++) outlineMaterials[sub] = highlightMaterial;
                outline.sharedMaterials = outlineMaterials;
                outline.shadowCastingMode = ShadowCastingMode.Off;
                outline.receiveShadows = false;
                outline.lightProbeUsage = LightProbeUsage.Off;
                outline.reflectionProbeUsage = ReflectionProbeUsage.Off;
                outline.renderingLayerMask = source.renderingLayerMask;
                outline.enabled = false;
                highlights[i] = outline;
            }
        }

        private Transform CopyTransform(Transform source, Dictionary<Transform, Transform> copies)
        {
            if (copies.TryGetValue(source, out Transform existing)) return existing;
            Transform parent = CopyTransform(source.parent, copies);
            Transform copy = new GameObject("Mesh Pose").transform;
            copy.SetParent(parent, false);
            copy.localPosition = source.localPosition;
            copy.localRotation = source.localRotation;
            copy.localScale = source.localScale;
            copies.Add(source, copy);
            return copy;
        }

        private void EmitSnapRing()
        {
            snapParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            snapParticles.Play(true);
            Vector3 center = piece.transform.position;
            if (chapter.slots != null && piece.identity >= 0 && piece.identity < chapter.slots.Length &&
                chapter.slots[piece.identity] != null)
                center = chapter.slots[piece.identity].position;
            for (int i = 0; i < 18; i++)
            {
                float angle = i * (Mathf.PI * 2f / 18f);
                Vector3 radial = chapter.transform.right * Mathf.Cos(angle) + chapter.transform.forward * Mathf.Sin(angle);
                var emit = new ParticleSystem.EmitParams
                {
                    position = center + radial * .31f - chapter.transform.up * .16f,
                    velocity = radial * .32f + chapter.transform.up * .08f,
                    startSize = .032f,
                    startLifetime = .45f,
                    startColor = Color.white,
                    applyShapeToPosition = false
                };
                snapParticles.Emit(emit, 1);
            }
        }

        private void Play(AudioClip clip, float pitch)
        {
            if (!Allowed || clip == null) return;
            audioSource.Stop();
            audioSource.volume = .32f * (app.settings != null ? Mathf.Clamp01(app.settings.sfx) : 1f);
            if (piece != null) audioSource.transform.position = piece.transform.position;
            audioSource.clip = clip;
            audioSource.pitch = pitch;
            audioSource.Play();
        }

        private void CacheEnvironment()
        {
            doorBlocks = new MaterialPropertyBlock[doorEnergy.Length];
            ringBlocks = new MaterialPropertyBlock[targetRings.Length];
            ringColors = new Color[targetRings.Length];
            ringStates = new int[targetRings.Length];
            for (int i = 0; i < doorEnergy.Length; i++)
            {
                doorBlocks[i] = new MaterialPropertyBlock();
                doorEnergy[i].GetPropertyBlock(doorBlocks[i]);
            }
            for (int i = 0; i < targetRings.Length; i++)
            {
                ringBlocks[i] = new MaterialPropertyBlock();
                targetRings[i].GetPropertyBlock(ringBlocks[i]);
                Material material = chapter.emptySlotMaterials[i];
                ringColors[i] = material != null && material.HasProperty(BaseColor) ? material.GetColor(BaseColor) : Cyan;
                ringStates[i] = -1;
            }
        }

        private void UpdateEnvironment()
        {
            if (app.Mistakes > mistakes) Play(errorCue, .65f);
            mistakes = app.Mistakes;
            UpdateEnvironmentVisuals();
            if (Time.unscaledTime < nextHover) return;
            nextHover = Time.unscaledTime + .05f;
            PollDesktopHover();
        }

        private void UpdateEnvironmentVisuals()
        {
            float amount = Mathf.Clamp01(chapter.exitDoor.OpenAmount);
            if (!Mathf.Approximately(amount, lastDoorAmount))
            {
                lastDoorAmount = amount;
                Color color = Cyan;
                color.a = .22f * amount;
                for (int i = 0; i < doorEnergy.Length; i++)
                {
                    doorEnergy[i].GetPropertyBlock(block);
                    block.SetColor(BaseColor, color);
                    doorEnergy[i].SetPropertyBlock(block);
                }
            }
            for (int i = 0; i < targetRings.Length; i++)
            {
                InteractiveSocketVFX effect = pieceEffects[i];
                int state = effect.piece.IsPlaced ? 2 : effect.wasHeld ? 1 : 0;
                if (ringStates[i] == state) continue;
                ringStates[i] = state;
                Color color = state == 2 ? new Color(.3f, 1f, .72f, .32f) : ringColors[i];
                color.a = state == 1 ? .38f : state == 2 ? .32f : .22f;
                targetRings[i].GetPropertyBlock(block);
                block.SetColor(BaseColor, color);
                targetRings[i].SetPropertyBlock(block);
            }
        }

        private void PollDesktopHover()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !headCamera.isActiveAndEnabled || headCamera.stereoEnabled)
            {
                SetDesktopTarget(null);
                return;
            }
            Vector2 position = mouse.position.ReadValue();
            Ray ray = headCamera.ScreenPointToRay(position);
            if (!headCamera.pixelRect.Contains(position) || PointerBlocked(position, ray))
            {
                SetDesktopTarget(null);
                return;
            }
            InteractiveSocketVFX target = null;
            if (headCamera.gameObject.scene.GetPhysicsScene().Raycast(ray.origin, ray.direction,
                out RaycastHit hit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                ChapterPiece candidate = hit.collider.GetComponentInParent<ChapterPiece>();
                if (candidate != null && candidate.chapter == chapter && !candidate.IsPlaced &&
                    candidate.grab != null && candidate.grab.isActiveAndEnabled &&
                    (headCamera.cullingMask & (1 << hit.collider.gameObject.layer)) != 0)
                    for (int i = 0; i < pieceEffects.Length; i++)
                        if (pieceEffects[i].piece == candidate) { target = pieceEffects[i]; break; }
            }
            SetDesktopTarget(target);
        }

        private bool PointerBlocked(Vector2 position, Ray ray)
        {
            EventSystem events = EventSystem.current;
            if (events == null) return false;
            if (pointerEvents != events)
            {
                pointerEvents = events;
                pointer = new PointerEventData(events);
                trackedPointer = new TrackedDeviceEventData(events);
                uiRay.Clear();
                uiRay.Add(Vector3.zero);
                uiRay.Add(Vector3.zero);
            }
            if (events.IsPointerOverGameObject()) return true;
            pointer.Reset();
            pointer.position = position;
            pointer.button = PointerEventData.InputButton.Left;
            uiHits.Clear();
            events.RaycastAll(pointer, uiHits);
            if (HasUIHit()) return true;
            trackedPointer.Reset();
            trackedPointer.pointerId = int.MinValue;
            trackedPointer.position = position;
            uiRay[0] = ray.origin;
            uiRay[1] = ray.GetPoint(headCamera.farClipPlane);
            trackedPointer.rayPoints = uiRay;
            trackedPointer.layerMask = headCamera.cullingMask;
            uiHits.Clear();
            events.RaycastAll(trackedPointer, uiHits);
            return HasUIHit();
        }

        private bool HasUIHit()
        {
            for (int i = 0; i < uiHits.Count; i++)
                if (uiHits[i].gameObject != null && uiHits[i].gameObject.GetComponentInParent<Canvas>() != null)
                    return true;
            return false;
        }

        private void SetDesktopTarget(InteractiveSocketVFX target)
        {
            if (desktopTarget == target) return;
            if (desktopTarget != null) desktopTarget.SetDesktopHover(false);
            desktopTarget = target;
            if (desktopTarget != null && desktopTarget.isActiveAndEnabled) desktopTarget.SetDesktopHover(true);
        }
    }
}
