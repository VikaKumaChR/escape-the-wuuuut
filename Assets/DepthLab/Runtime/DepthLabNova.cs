using TMPro;
using UnityEngine;

namespace DepthLab
{
    public sealed class DepthLabNova : MonoBehaviour
    {
        public enum Emotion { Neutral, Happy, Curious, Thinking, Encouraging, Excited }
        public bool externallyDriven;
        public DepthLabApp app;
        public DepthLabSettings settings;
        public Transform visualRoot, torso, head;
        public Transform[] shoulders, elbows, eyeCores, eyeSmiles, brows, mouthSegments, reactorSegments;
        public Renderer[] luminousRenderers;
        public Canvas dialogueCanvas;
        public TMP_Text subtitle;
        public GameObject subtitlePanel;
        public DepthLabButton talkButton, hintButton;
        public AudioSource toneSource;
        public AudioClip[] emotionTones;
        public Emotion State { get; private set; }
        private int chapter = int.MinValue, progress = -1, mistakes = -1;
        private float speechUntil, lastActivity, nextBlink;
        private Vector3 restingPosition;
        private MaterialPropertyBlock properties;
        private bool poseCached, externalPaused = true, welcomeWave, externalAudioOwned;
        private bool originalIgnoreListenerVolume;
        private float presentationTime, presentationRemaining, externalNextBlink = 3f;
        private Vector3[] eyeScales, smileScales, browScales, mouthScales;
        private Quaternion headRotation;
        private Quaternion[] shoulderRotations, elbowRotations, browRotations;

        public bool ExternalPresentationActive => externallyDriven && presentationRemaining > 0f;
        public uint ExternalPresentationVersion { get; private set; }

        private void Awake()
        {
            properties = new MaterialPropertyBlock();
            CachePose();
            nextBlink = Time.time + 3f;
        }

        private void Update()
        {
            if (externallyDriven)
            {
                if (app == null || !app.IsPlaying || app.IsPaused) SetExternalPaused(true);
                return;
            }
            RestoreAudioOwnership();
            if (app == null || app.headCamera == null) return;
            if (hintButton != null) hintButton.interactable = app.IsPlaying && !app.IsPaused;
            if (app.IsPaused) return;
            if (chapter != app.ActiveChapter)
            {
                chapter = app.ActiveChapter;
                progress = mistakes = -1;
                if (chapter >= 0 && chapter < app.chapters.Length)
                {
                    transform.position = app.chapters[chapter].transform.TransformPoint(new Vector3(-2.5f, 0, -1.3f));
                    Speak("欢迎来到" + DepthLabProgress.Titles[chapter] + "。先观察目标，再动手验证。", Emotion.Curious);
                }
                else
                {
                    transform.position = new Vector3(2.8f, 0, 1.2f);
                    Speak("欢迎，我是诺娃。沿青色标记前往第一实验室，也可以向我了解操作方法。", Emotion.Happy);
                }
                Vector3 facing = app.Head.position - transform.position;
                facing.y = 0;
                if (facing.sqrMagnitude > .01f)
                    transform.rotation = Quaternion.LookRotation(-facing.normalized);
            }
            if (app.IsPlaying && chapter >= 0 && chapter < app.chapters.Length)
            {
                int current = app.chapters[chapter].Progress;
                if (progress >= 0 && current > progress)
                    Speak(current >= app.chapters[chapter].Goal
                        ? "实验完成！出口正在打开，我们继续下一项研究。"
                        : "做得很好，观察结果已经记录。继续下一个目标。",
                        current >= app.chapters[chapter].Goal ? Emotion.Excited : Emotion.Happy);
                if (mistakes >= 0 && app.Mistakes > mistakes)
                    Speak("没关系，错误也是线索。换一个角度观察，我会陪你继续尝试。", Emotion.Encouraging);
                progress = current;
                mistakes = app.Mistakes;
                if (Time.time - lastActivity > 45f)
                    Speak("可以走到侧面观察前后距离。需要具体帮助时，点击请求提示。", Emotion.Thinking);
            }
            bool speaking = Time.time < speechUntil;
            if (!speaking && State != Emotion.Neutral) SetEmotion(Emotion.Neutral);
            if (subtitlePanel != null) subtitlePanel.SetActive(speaking);
            Animate(speaking);
        }

        public void Talk()
        {
            if (externallyDriven) return;
            if (app == null || app.IsPaused) return;
            Speak(app.IsPlaying && app.ActiveChapter >= 0
                ? app.chapters[app.ActiveChapter].Objective
                : "这里研究双眼视差、运动视差、视觉错觉和光线。你可以自由探索大厅，或从操作教程开始。",
                Emotion.Curious);
        }

        public void Hint()
        {
            if (externallyDriven) return;
            if (app == null || !app.IsPlaying || app.IsPaused) return;
            app.Execute(LabAction.Hint);
            Speak(app.chapters[app.ActiveChapter].Hint, Emotion.Thinking);
        }

        private void Speak(string text, Emotion emotion)
        {
            if (subtitle != null) subtitle.text = text;
            speechUntil = Time.time + Mathf.Clamp(text.Length * .12f, 5f, 12f);
            lastActivity = Time.time;
            SetEmotion(emotion);
            int index = (int)emotion;
            if (toneSource != null && emotionTones != null && index < emotionTones.Length &&
                emotionTones[index] != null)
            {
                toneSource.volume = settings != null ? settings.sfx * .35f : .35f;
                toneSource.PlayOneShot(emotionTones[index]);
            }
        }

        private void SetEmotion(Emotion emotion)
        {
            State = emotion;
            Color color = emotion == Emotion.Encouraging || emotion == Emotion.Thinking
                ? new Color32(168, 85, 247, 255) : new Color32(0, 229, 255, 255);
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.SetColor("_EmissionColor", color * 1.5f);
            if (luminousRenderers != null)
                foreach (Renderer renderer in luminousRenderers)
                    if (renderer != null) renderer.SetPropertyBlock(properties);
            if (eyeSmiles != null)
                foreach (Transform eye in eyeSmiles)
                    if (eye != null) eye.gameObject.SetActive(emotion == Emotion.Happy || emotion == Emotion.Excited);
            if (eyeCores != null)
                for (int i = 0; i < eyeCores.Length; i++)
                    if (eyeCores[i] != null)
                        eyeCores[i].gameObject.SetActive(!((emotion == Emotion.Happy || emotion == Emotion.Excited) &&
                            eyeSmiles != null && i < eyeSmiles.Length && eyeSmiles[i] != null));
        }

        public bool ValidateExternalConfiguration(out string error)
        {
            error = null;
            if (visualRoot == null || head == null || visualRoot == transform ||
                !visualRoot.IsChildOf(transform) || !head.IsChildOf(visualRoot))
                error = "Nova requires a separate VisualRoot child and a head beneath it.";
            else if (!HasParts(shoulders, 2) || !HasParts(elbows, 2) ||
                !HasParts(eyeCores, 2) || !HasParts(eyeSmiles, 2) ||
                !HasParts(brows, 2) || !HasParts(mouthSegments, 1))
                error = "Nova requires two shoulders, elbows, eyes, smile eyes and brows, plus mouth segments.";
            else if (!elbows[0].IsChildOf(shoulders[0]) || !elbows[1].IsChildOf(shoulders[1]))
                error = "Nova elbows must be articulated children of their corresponding shoulders.";
            else if (toneSource == null || !toneSource.enabled || !toneSource.gameObject.activeSelf ||
                emotionTones == null || emotionTones.Length < 6)
                error = "Nova requires an enabled toneSource and the six existing synthesized emotion tones.";
            else
                for (int i = 0; i < 6; i++)
                    if (emotionTones[i] == null)
                    {
                        error = "Nova emotionTones[" + i + "] is missing; assign the existing synthesized tone.";
                        break;
                    }
            return error == null;
        }

        private static bool HasParts(Transform[] parts, int minimum)
        {
            if (parts == null || parts.Length < minimum) return false;
            foreach (Transform part in parts)
                if (part == null) return false;
            return true;
        }

        public void ResetExternalPresentation()
        {
            if (!externallyDriven) return;
            CachePose();
            if (toneSource != null) toneSource.Stop();
            presentationTime = presentationRemaining = 0f;
            externalNextBlink = 3f;
            welcomeWave = false;
            externalPaused = true;
            SetEmotion(Emotion.Neutral);
            if (visualRoot != null) visualRoot.localPosition = restingPosition;
            if (head != null) head.localRotation = headRotation;
            RestoreRotations(shoulders, shoulderRotations);
            RestoreRotations(elbows, elbowRotations);
            RestoreRotations(brows, browRotations);
            RestoreScales(eyeCores, eyeScales);
            RestoreScales(eyeSmiles, smileScales);
            RestoreScales(brows, browScales);
            RestoreScales(mouthSegments, mouthScales);
        }

        // Text plus a short nonverbal tone; the mouth is a display, not recorded speech.
        public void PresentExternal(string text, Emotion emotion, bool waveHello = false)
        {
            if (!externallyDriven || !isActiveAndEnabled || externalPaused) return;
            unchecked { ExternalPresentationVersion++; }
            if (subtitle != null)
            {
                subtitle.pageToDisplay = 1;
                subtitle.text = text;
            }
            if (subtitlePanel != null) subtitlePanel.SetActive(true);
            presentationRemaining = Mathf.Clamp((text == null ? 0 : text.Length) * .12f, 5f, 12f);
            welcomeWave = waveHello;
            SetEmotion(emotion);
            int index = (int)emotion;
            if (toneSource != null && emotionTones != null && index >= 0 &&
                index < emotionTones.Length && emotionTones[index] != null)
            {
                UpdateExternalVolume();
                toneSource.Stop();
                toneSource.PlayOneShot(emotionTones[index]);
            }
        }

        public void SetExternalPaused(bool paused)
        {
            if (!externallyDriven) return;
            externalPaused = paused;
            // Stop rather than replaying a stale cue when the user resumes.
            if (paused && toneSource != null) toneSource.Stop();
        }

        public void TickExternal(float activeDeltaTime)
        {
            if (!externallyDriven || !isActiveAndEnabled || externalPaused ||
                activeDeltaTime <= 0f || float.IsNaN(activeDeltaTime) || float.IsInfinity(activeDeltaTime)) return;
            CachePose();
            presentationTime += activeDeltaTime;
            presentationRemaining = Mathf.Max(0f, presentationRemaining - activeDeltaTime);
            bool presenting = presentationRemaining > 0f;
            if (!presenting && State != Emotion.Neutral)
            {
                welcomeWave = false;
                SetEmotion(Emotion.Neutral);
            }
            UpdateExternalVolume();
            AnimateExternal(presenting, activeDeltaTime);
        }

        private void OnDisable()
        {
            if (toneSource != null) toneSource.Stop();
            externalPaused = true;
            RestoreAudioOwnership();
        }

        private void UpdateExternalVolume()
        {
            if (toneSource == null) return;
            if (!externalAudioOwned)
            {
                originalIgnoreListenerVolume = toneSource.ignoreListenerVolume;
                externalAudioOwned = true;
            }
            // Settings.Apply also sets AudioListener.volume; don't apply master twice.
            toneSource.ignoreListenerVolume = true;
            DepthLabSettings mix = settings != null ? settings : app != null ? app.settings : null;
            toneSource.volume = .35f * (mix != null ? Mathf.Clamp01(mix.master) * Mathf.Clamp01(mix.sfx) : 1f);
        }

        private void RestoreAudioOwnership()
        {
            if (!externalAudioOwned) return;
            if (toneSource != null) toneSource.ignoreListenerVolume = originalIgnoreListenerVolume;
            externalAudioOwned = false;
        }

        private void CachePose()
        {
            if (poseCached) return;
            poseCached = true;
            if (visualRoot != null) restingPosition = visualRoot.localPosition;
            headRotation = head != null ? head.localRotation : Quaternion.identity;
            eyeScales = CaptureScales(eyeCores);
            smileScales = CaptureScales(eyeSmiles);
            browScales = CaptureScales(brows);
            mouthScales = CaptureScales(mouthSegments);
            shoulderRotations = CaptureRotations(shoulders);
            elbowRotations = CaptureRotations(elbows);
            browRotations = CaptureRotations(brows);
        }

        private static Vector3[] CaptureScales(Transform[] parts)
        {
            var result = new Vector3[parts == null ? 0 : parts.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = parts[i] != null ? parts[i].localScale : Vector3.one;
            return result;
        }

        private static Quaternion[] CaptureRotations(Transform[] parts)
        {
            var result = new Quaternion[parts == null ? 0 : parts.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = parts[i] != null ? parts[i].localRotation : Quaternion.identity;
            return result;
        }

        private static void RestoreScales(Transform[] parts, Vector3[] scales)
        {
            for (int i = 0; parts != null && scales != null && i < parts.Length && i < scales.Length; i++)
                if (parts[i] != null) parts[i].localScale = scales[i];
        }

        private static void RestoreRotations(Transform[] parts, Quaternion[] rotations)
        {
            for (int i = 0; parts != null && rotations != null && i < parts.Length && i < rotations.Length; i++)
                if (parts[i] != null) parts[i].localRotation = rotations[i];
        }

        private void AnimateExternal(bool presenting, float delta)
        {
            float time = presentationTime;
            float wave = Mathf.Sin(time * 4f);
            float blend = 1f - Mathf.Exp(-9f * delta);
            if (visualRoot != null)
                visualRoot.localPosition = restingPosition + Vector3.up * (Mathf.Sin(time * 1.3f) * .035f);
            if (head != null)
                head.localRotation = Quaternion.Slerp(head.localRotation,
                    headRotation * Quaternion.Euler(presenting ? wave * 3f : 0f, 0f,
                        State == Emotion.Curious ? 10f : State == Emotion.Encouraging ? -6f : 0f), blend);

            for (int i = 0; shoulders != null && i < shoulders.Length; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Vector3 shoulder = Vector3.zero, elbow = Vector3.zero;
                if (welcomeWave && i == 1)
                {
                    shoulder = new Vector3(15f, 0f, side * (105f + wave * 12f));
                    elbow = new Vector3(20f, wave * 15f, side * 35f);
                }
                else if (State == Emotion.Happy || State == Emotion.Excited)
                {
                    shoulder = new Vector3(12f, 0f, side * ((State == Emotion.Excited ? 120f : 65f) + wave * 8f));
                    elbow = new Vector3(30f, 0f, side * 20f);
                }
                else if (State == Emotion.Encouraging)
                {
                    shoulder = new Vector3(25f, side * -15f, side * 28f);
                    elbow = new Vector3(95f + wave * 3f, side * 25f, 0f);
                }
                else if (State == Emotion.Thinking && i == 1)
                {
                    shoulder = new Vector3(25f, -20f, -15f);
                    elbow = new Vector3(135f, 0f, -25f);
                }
                if (shoulders[i] != null)
                    shoulders[i].localRotation = Quaternion.Slerp(shoulders[i].localRotation,
                        shoulderRotations[i] * Quaternion.Euler(shoulder), blend);
                if (elbows != null && i < elbows.Length && elbows[i] != null)
                    elbows[i].localRotation = Quaternion.Slerp(elbows[i].localRotation,
                        elbowRotations[i] * Quaternion.Euler(elbow), blend);
            }

            bool blink = time >= externalNextBlink && time < externalNextBlink + .15f;
            if (time >= externalNextBlink + .15f) externalNextBlink = time + 3.5f;
            for (int i = 0; eyeCores != null && i < eyeCores.Length; i++)
                if (eyeCores[i] != null)
                    eyeCores[i].localScale = Vector3.Scale(eyeScales[i],
                        new Vector3(1f, blink ? .1f : State == Emotion.Thinking ? .65f : 1f, 1f));
            for (int i = 0; eyeSmiles != null && i < eyeSmiles.Length; i++)
                if (eyeSmiles[i] != null)
                    eyeSmiles[i].localScale = Vector3.Scale(smileScales[i], new Vector3(1f, blink ? .1f : 1f, 1f));
            for (int i = 0; brows != null && i < brows.Length; i++)
            {
                if (brows[i] == null) continue;
                float side = i == 0 ? -1f : 1f;
                float angle = State == Emotion.Encouraging ? side * -14f :
                    State == Emotion.Thinking ? side * 12f :
                    State == Emotion.Curious ? (i == 0 ? -12f : 20f) :
                    State == Emotion.Happy || State == Emotion.Excited ? side * -8f : 0f;
                brows[i].localRotation = Quaternion.Slerp(brows[i].localRotation,
                    browRotations[i] * Quaternion.Euler(0f, 0f, angle), blend);
                brows[i].localScale = Vector3.Scale(browScales[i],
                    new Vector3(1f, presenting ? 1f + .08f * wave : 1f, 1f));
            }
            for (int i = 0; mouthSegments != null && i < mouthSegments.Length; i++)
                if (mouthSegments[i] != null)
                    mouthSegments[i].localScale = Vector3.Scale(mouthScales[i],
                        new Vector3(1f, presenting ? .6f + .3f * Mathf.Abs(Mathf.Sin(time * 4f + i)) : .5f, 1f));
        }

        private void Animate(bool speaking)
        {
            float time = Time.time;
            float wave = Mathf.Sin(time * (State == Emotion.Excited ? 5f : 2.5f));
            if (visualRoot != null)
                visualRoot.localPosition = restingPosition + Vector3.up * Mathf.Sin(time * 1.3f) * .012f;
            if (head != null)
                head.localRotation = Quaternion.Euler(speaking ? wave * 4f : 0, speaking ? wave * 5f : 0,
                    State == Emotion.Curious ? 10f : 0);
            for (int i = 0; shoulders != null && i < shoulders.Length; i++)
            {
                float side = i == 0 ? -1f : 1f;
                float lift = State == Emotion.Excited ? 70f :
                    State == Emotion.Happy ? 35f : speaking ? 18f : 6f;
                if (shoulders[i] != null)
                    shoulders[i].localRotation = Quaternion.Slerp(shoulders[i].localRotation,
                        Quaternion.Euler(speaking ? -15f : 0, 0, side * (lift + (speaking ? wave * 10f : 0))), .12f);
                if (elbows != null && i < elbows.Length && elbows[i] != null)
                    elbows[i].localRotation = Quaternion.Euler(State == Emotion.Thinking ? -90f : -20f, 0, -side * 9f);
            }
            if (mouthSegments != null)
                for (int i = 0; i < mouthSegments.Length; i++)
                    if (mouthSegments[i] != null)
                        mouthSegments[i].localScale = Vector3.Scale(mouthScales[i],
                            new Vector3(1, speaking ? .6f + Mathf.Abs(Mathf.Sin(time * 9f + i)) : .5f, 1));
            bool blink = time >= nextBlink && time < nextBlink + .15f;
            if (time > nextBlink + .15f) nextBlink = time + 3.5f;
            if (eyeCores != null)
                for (int i = 0; i < eyeCores.Length; i++)
                    if (eyeCores[i] != null) eyeCores[i].localScale = Vector3.Scale(eyeScales[i],
                        new Vector3(1, blink ? .1f : State == Emotion.Thinking ? .65f : 1, 1));
        }
    }
}
