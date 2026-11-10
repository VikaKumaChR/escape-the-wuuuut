using UnityEngine;

namespace DepthLab
{
    public sealed class DepthLabRoomAtmosphere : MonoBehaviour
    {
        public DepthLabApp app;
        public LabChapter chapter;
        public ParallaxForestChapter forest;
        public ParticleSystem dust;
        public GameObject observationControls;
        public Transform[] leaves;
        public Renderer[] exitLights;
        private Quaternion[] restingRotations;
        private bool flowing;
        private bool celebrated;
        private float clock;
        private MaterialPropertyBlock glow;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            restingRotations = new Quaternion[leaves.Length];
            for (int i = 0; i < leaves.Length; i++) restingRotations[i] = leaves[i].localRotation;
            glow = new MaterialPropertyBlock();
        }

        private void Update()
        {
            bool active = app.IsPlaying && !app.IsPaused && chapter.InteractionAllowed;
            if (flowing != active)
            {
                flowing = active;
                if (active) dust.Play(); else dust.Pause();
            }
            if (forest != null && observationControls != null)
                observationControls.SetActive(!forest.PathDiscovered);
            if (!active) return;
            clock += Time.deltaTime;
            for (int i = 0; i < leaves.Length; i++)
                leaves[i].localRotation = restingRotations[i] * Quaternion.Euler(0, 0,
                    Mathf.Sin(clock * .65f + i * .7f) * 2.5f);
            if (chapter.ExitReady && !celebrated)
            {
                celebrated = true;
                app.PlaySound(true);
            }
            if (exitLights != null)
            {
                glow.SetColor(BaseColor, new Color(.05f, .82f, 1, .24f * chapter.exitDoor.OpenAmount));
                foreach (Renderer light in exitLights) light.SetPropertyBlock(glow);
            }
        }

        private void OnDisable()
        {
            flowing = false;
            celebrated = false;
            if (dust != null) dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
