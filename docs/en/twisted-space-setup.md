# Twisted Space — VR Panels & Forced-Perspective Puzzle: Setup Guide

🌐 **English** · [한국어](../ko/twisted-space-setup.md) · [中文原文](../../Assets/TwistedSpace/SETUP.txt)

> [!NOTE]
> This is a translation of `Assets/TwistedSpace/SETUP.txt`. It describes the original **Twisted Space** prototype (the *Parallax Chamber*).
> In the current main scene the **DepthLab** experience has replaced that room: the Parallax Chamber object is disabled and the XR Device Simulator is turned off.
> Keep this guide as the reference for the puzzle and world-space UI components that DepthLab still builds on.

## Versions and input

- XR Interaction Toolkit is pinned to **3.3.2**; the project uses the **new Input System**.
- The actual editor version is **Unity 6000.6.8f1**, not 6000.3 LTS. The editor version was not changed and URP was not downgraded. Scripts use the Unity 6 / XRI 3.x APIs.
- The XR Device Simulator is the classic simulator shipped with the official package. It does not use the legacy Input Manager.
- On Apple Silicon Macs, keyboard-and-mouse simulation needs neither SteamVR nor a real headset runtime.
- Builds for a real headset need the matching XR Provider configured for the target device.

## Preparing the XR scene

1. Wait for Unity to finish importing packages and scripts. The official XR Device Simulator sample is already imported into `Assets/Samples`.
   To re-import it, use **Twisted Space › 1. Import XR Device Simulator**.
2. In a scene that has no Camera, EventSystem or XR Origin, run **Twisted Space › 2. Create Simulator XR Rig**, then save the scene.
   The tool never deletes existing cameras and never overwrites existing scenes.
3. Both generated controllers have an `XRDirectInteractor`; the **grip** handles direct grabbing.
   `XRRayInteractor` is for **UI only**: UI Press is bound to the trigger so it does not compete with grabbing for selection.
   The UI input module is `XRUIInputModule` — not `StandaloneInputModule` or `InputSystemUIInputModule`.
4. The official simulator's help overlay is disabled on the generated instance. The sample's help-UI assets do not need to be dragged into the game scene. **All game UI uses World Space.**
5. Use the simulator in the Game window. Key bindings follow the three `.inputactions` assets inside the sample; you can view or change them in the Inspector. **Never enable two simulators at once.**

## Panels and buttons

- The Canvas carries `VRWorldSpacePanel` with render mode **World Space**. Do not parent the panel to the headset.
- Drag the **Main Camera** inside the XR Origin into the panel's headset-camera field.
- The Canvas needs `CanvasGroup` and `TrackedDeviceGraphicRaycaster`; the script requires these components.
- Recommended: Canvas size **700 × 400**, world scale **0.0015**, buttons at least **70 px** tall.
- Avoid adding a Collider to the panel that would block its own rays.

**Buttons**

- Use `ConfirmedXRButton` instead of the standard `Button` component (never both on the same object).
- Assign the button Image as **Target Graphic**. Its base color should not be pure white, or there is no headroom for the highlight.
- You can give each button a light-blue **Halo Image**; turn its *Raycast Target* off.
- Standard UGUI materials are unlit. The highlight/halo simulates an emissive look without Bloom or heavy post-processing.
- Each button's **On Click** can bind its own `UnityEvent` callback.
- Confirmed buttons rely on the ray's *release* click event; Poke's "Click UI On Down" mode is not supported.
- The trigger must be held for **at least 0.15 s** and released **on the same button** to fire the callback once. Releasing early, moving off the button, hiding the panel or disabling interaction cancels it.
- Do not also call gameplay events from `EventTrigger` / `PointerDown` — that bypasses the confirmation guard.

**Panel API**

- The panel exposes `FadeIn()` / `FadeOut()`; fades run as coroutines with an adjustable duration.
- Drag the **Reset puzzle** and **Back** buttons into the panel's fields, then bind `OnResetPuzzle` / `OnBack`.
- Bind each gameplay function **once**: not on both the button's On Click and the panel event.
- The game decides what *Back* does (hide the panel, return to the room or switch levels). The script never quits the app on its own.

## Puzzle configuration

- Areas use `PuzzleObservationVolume`, targets use `PerspectivePuzzleTarget`, and the main controller uses `ForcedPerspectivePuzzle`. Every field has comments and Inspector tooltips.
- Areas must be **Triggers**. Assign the headset's dedicated head collider as the observer — never controller or prop colliders.
- At least one side of a trigger pair needs a Rigidbody. For the head probe, use a small `SphereCollider` with a Rigidbody (*Is Kinematic* on, *Use Gravity* off) that follows the headset. The head probe can be a Trigger so it does not push scene objects.

**Targets**

- Each target root needs a `Collider`, `Rigidbody`, `XRGrabInteractable` and `PerspectivePuzzleTarget`.
- Do not use non-convex dynamic `MeshCollider`s; a simple `BoxCollider` or a convex collider suits VR better.
- Target materials should use a shader with `_EmissionColor`, such as **URP/Lit**.
- Before building, enable **Emission** on those material assets with a very low non-zero base emission so the `_EMISSION` shader variant survives the build. Do not rely only on enabling the keyword at runtime, or the variant may be stripped.
- Geometry that should float after being grabbed should have Rigidbody *Use Gravity* turned off.
- Add only the renderers that take part in the silhouette check to the target list — not helper markers or shadow planes.

**Main controller**

- Wire the controller to the areas, the Main Camera, the target array and the Transform that defines the correct viewing direction.
- Compound observation areas: use several child objects with `BoxCollider` + `PuzzleObservationVolume` and drag them all into the controller's **Observation Volumes** array.
- The correct direction is that Transform's blue **+Z** axis; point it at the puzzle objects in the Scene view.
- Bind `OnPuzzleSolved` to door opening, sound effects and so on; bind the Reset button to `ResetPuzzle()`.
- The progress event can drive a World Space UGUI `Slider.value`. Set the Slider range to 0..1 and turn *Interactable* off if it is display-only. Progress does not mean any single target qualifies on its own — **all targets must be aligned at the same time.**

**How the check works**

- It approximates with **projected bounding boxes**; it is not pixel-silhouette recognition or a GPU occlusion test. Design geometry size and distance for the intended viewpoint so the projected boxes can cover each other. Puzzles where "complementary pieces form one complete shape" do not suit this overlap algorithm.
- It uses the **center-eye (mono) projection** to avoid progress jitter from alternating left/right eyes.
- Targets behind the viewer, past the near clipping plane or outside the view cannot pass.
- Success is **latched** until `ResetPuzzle` is called explicitly. Do not wire the success event back into `ResetPuzzle`.
- If a target is still grabbed during a reset, the player is asked to let go. Once the last hand releases and the XRI detach finishes, the target returns to its initial pose without yanking the controller. Targets waiting for a reset cannot take part in the check.

## Feel defaults

- Slower head follow, soft hover transitions and a lenient alignment threshold of **0.65**.
- After tuning at runtime, **exit Play Mode first**, then save the parameters on the scene or prefab.
- To keep tuning the feel later, change only the times, thresholds and curves in the Inspector; there is no need to touch the event structure.
