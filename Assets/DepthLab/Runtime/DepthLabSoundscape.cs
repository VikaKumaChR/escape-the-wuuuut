using UnityEngine;

namespace DepthLab
{
    public sealed class DepthLabSoundscape : MonoBehaviour
    {
        public DepthLabApp app;
        public AudioClip laboratoryMusic, forestMusic, illusionMusic, opticsMusic;
        private AudioClip pending;
        private float gain;

        private void Update()
        {
            AudioSource source = app.musicAudio;
            AudioClip requested = app.ActiveChapter == 1 ? forestMusic :
                app.ActiveChapter == 2 ? illusionMusic : app.ActiveChapter == 3 ? opticsMusic : laboratoryMusic;
            pending = requested;
            if (source.clip != pending)
            {
                gain = Mathf.MoveTowards(gain, 0, Time.unscaledDeltaTime * 1.8f);
                if (gain <= 0)
                {
                    source.Stop();
                    source.clip = pending;
                    source.loop = true;
                    source.Play();
                }
            }
            else
            {
                if (!source.isPlaying && !AudioListener.pause) source.Play();
                gain = Mathf.MoveTowards(gain, app.IsPaused ? .35f : 1f, Time.unscaledDeltaTime);
            }
            source.volume = app.settings.music * gain;
        }
    }
}
