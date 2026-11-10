# Twisted Space — Parallax Observatory: Demo Guide

🌐 **English** · [한국어](../ko/twisted-space-demo-guide.md) · [中文原文](../../Assets/TwistedSpace/Demo/DEMO_GUIDE.txt)

> [!NOTE]
> This is a translation of `Assets/TwistedSpace/Demo/DEMO_GUIDE.txt`, which describes the original **Twisted Space** playable prototype.
> That room is **disabled** in the current main scene, and the XR Device Simulator is turned off there.
> For the game you play today (DepthLab), read **[How to Play](how-to-play.md)** instead.

## Open and run

- Scene: `Assets/TwistedSpace/Demo/Scenes/ParallaxChamber.unity`
- Double-click the scene in the Project window, wait for compilation to finish, press **Play**, then click inside the **Game** window.
- If new files don't show up yet, use **Assets › Refresh**.
- No manual wiring is needed: the scene already stores the camera, head probe, targets, UI and every button event.
- The original `Assets/Scenes/SampleScene.unity` was not modified.
- Full prefab: `Assets/TwistedSpace/Demo/Prefabs/ParallaxChamber.prefab`. It contains the wired room, UI, XR Rig and puzzle and can be dropped into an empty scene. Do not use it together with another XR Rig.
- The playtest scene uses an instance of this prefab; every object can be expanded and inspected.

Other facts:

- The editor is **Unity 6000.6.8f1**; XRI is pinned to **3.3.2**.
- This is a keyboard-and-mouse **XR Device Simulator** playtest, not a build configured for a specific headset.
- No SteamVR, legacy Input Manager, DOTween, real-time background blur or Bloom was added.

## First steps

1. **Keyboard and mouse (Mac or PC):** after pressing Play, click the Game window, then click **Start exploring** with the mouse.
   A normal short click also works; there is a 0.15 s confirmation delay and no need to aim the simulated controller first.
   The main menu is fixed in world space and does not follow your head. While the menu is open, simulator keyboard/mouse controls are paused and scene occluders are hidden.
   With a real headset, the XR ray and trigger stay the controls; the camera is not locked to a mouse view.
2. Once exploring, press **Y** to select the right hand and **R** to switch the mouse between translate and rotate.
   The **left mouse button** is now the selected controller's trigger: hold it for at least 0.15 s and release on a button to confirm.
   Quick clicks, moving off the button while holding, or a panel fading out will not trigger it.
3. Grabbing uses **G** (grip), not the left mouse button. Move the hand to one of a frame's **solid edges**, hold G, move the hand, then release G to place it.
   The hollow middle of a frame cannot be grabbed.
4. Move the three frames one by one, then return to the **cyan square observation zone** on the floor.
   Face the exit and overlap the outer outlines of all three frames; hold for about **0.35 s**.
5. On success, the frames light up, the sliding exit door opens, and your time and number of hints are shown.
   Choose **Replay** or **Back to main menu**.

## Official simulator default bindings (Input System)

| Key | Action |
|---|---|
| **T** / **Y** / **U** | Control the left hand / right hand / whole body |
| **Tab** | Cycle the control target |
| **R** | Toggle mouse translate / rotate (hold **Left Ctrl** for temporary rotate) |
| **W A S D** | Move the current device forward / left / back / right |
| **Q / E** | Move the current device down / up |
| **Hold right mouse button** | Control the head |
| **Left mouse button** | Current controller's trigger |
| **G** | Current controller's grip (grab) |
| **Backslash** | Toggle mouse lock |

- Never enable two simulators at once, and never add `InputSystemUIInputModule` or `StandaloneInputModule`.
- If you change bindings, the source of truth is the three `.inputactions` assets in `Assets/Samples/XR Interaction Toolkit/3.3.2/XR Device Simulator`.

## Simulator body collision

- The full prefab includes **Simulator Collision**, outside the XR Origin.
- When you press **U** to control the body and move with WASD, a capsule sweep blocks walls, structural pillars, closed doors, the floor and the ceiling.
- Moving diagonally into a wall slides along it. Once the door is fully open you can enter the exit alcove, which still has a solid back wall.
- **Q / E** keep vertical debug movement but cannot pass through the floor or ceiling. This is not a gravity or stair system.
- Environment solids use physics layer **8: TwistedSpaceSolid**. Observation zones, hands and grabbable frames are not on that layer.
- Adjust the body size and environment layers on: *prefab root › Simulator Collision*.
- This component only corrects the XR Origin of the simulated headset and never writes the camera's tracked pose.
- It stops correcting automatically when a real headset is connected or the tracking source is unclear. Real VR still needs its own locomotion and comfort-occlusion solution.
- This fix does not change how grabbed objects move kinematically, and does not guarantee that hands or held objects never pass through walls.

## Gameplay and feel

- The targets are three independent, gravity-free frames. The intro level follows only the grab **position**, not wrist rotation, and releasing does not throw — so a freshly adjusted silhouette does not keep drifting. Rotation can be enabled later on `XRGrabInteractable`.
- Direct grabbing requires the hand to touch the frame; the UI ray does not double as a remote grab.
- With the simulator you can move a hand to a distant frame, or move the body with **U** and come back to the observation zone.
- The overlap threshold is **0.65**; the head-direction tolerance is **25°**.
- **Alignment progress** is normalized relative to the success threshold, not the raw geometric overlap ratio.
- **Stability check** separately shows how long you have held after reaching the threshold.
- The check only runs while the dedicated head collider is inside the observation zone; hands and props entering do not count.
- The reset confirmation panel pauses detection and the timer, but it does not stop headset tracking or drag the hands.
- If an object is still held, reset first asks you to let go; the initial position is restored after the XRI detach completes.
- Cancelling a reset keeps the current positions and the timer. Returning to the main menu stops this round's detection, and starting again resets it.
- On the result screen the puzzle component stays enabled so the success glow animation can finish.

## Visuals and placeholder assets

- All four panels are World Space UGUI: main menu, hint/progress, reset confirmation and results.
- The main menu, reset and results panels are placed once when opened and then do not follow the head; the hint panel is fixed to the right of the puzzle.
- The main menu uses its own architectural key art, a large title and a clear primary button; frames and hand models do not block the opening view.
- The main menu's World Space Canvas has both a regular mouse `GraphicRaycaster` and an XR `TrackedDeviceGraphicRaycaster`, switched by the same `XRUIInputModule`. No Screen Space Overlay or second input module is used.
- Static medium-weight CJK font, small frosted noise textures, light-blue borders and a little orange for warnings.
- The simulator's built-in screen overlay help is disabled; instructions are floating signs in the scene.
- Original architecture and UI textures, buttons and icons: `Assets/TwistedSpace/Demo/Art`.
- The room has grooved wall panels, segmented ceiling panels, recessed light strips, floor seams and an observation ring.
- Materials use small textures and moderate emission; extra fill lights cast no shadows; there is no real-time frosted-glass blur or post-processing glow.
- Updates were saved to the existing `ParallaxChamber.prefab`; there is no need to drag in another copy.
- If you are in Play Mode, exit first, then use **Assets › Refresh**, wait for the import and press Play again.
- Set the Game window **Scale to 1x**. 2x only magnifies the editor view; it does not raise the game resolution.

Placeholders:

- Characters are replaceable placeholders: gloves on the controllers, a floating calibration body on the right, and a distant observer.
- The scene is built from editable primitive geometry. It is not final art, a full story or a complete level collection.
- Material assets have moderate Emission enabled; keep the keyword so the emissive variant is not stripped at build time.

## Editing and extending

| What | Where |
|---|---|
| Runtime controller | *Parallax Chamber › Demo Flow* |
| Puzzle parameters | *Parallax Chamber › Forced Perspective Puzzle* |
| Scene objects | *Parallax Chamber › Demo Environment* |
| Panels and buttons | *Parallax Chamber › World Space UI* |
| Door animation | *Parallax Chamber › Exit Door Controller* |

- Just drag and edit the existing scene. **Twisted Space › 3. Create Playable Demo Scene** only *opens* the scene if it already exists and never overwrites your edits.
- The scene generator keeps the original build process so it can be extended in a separate copy later.
- **Twisted Space › Apply Visual And Collision Upgrade** re-applies this upgrade and saves only the prefab, without reopening the scene. It resets the art materials and decorations it owns, so back up custom tweaks first.
- This update did not change the existing render-pipeline assets, Build Settings or real-headset Provider configuration.

## Reference solution (for debugging)

With the head at `(0, 1.6, 0)` facing **+Z**, and the three targets at their initial scale with zero rotation, placing their centers at `(0, 1.6, 1.55)`, `(0, 1.6, 1.90)` and `(0, 1.6, 2.25)` aligns them perfectly.
The algorithm computes projected bounding-box overlap; it is not exact pixel-silhouette recognition or a free-form shape puzzle.
