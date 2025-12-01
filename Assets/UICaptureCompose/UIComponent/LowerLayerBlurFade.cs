using System;
using UnityEngine;

namespace UICaptureCompose.UIComponent
{
    [RequireComponent(typeof(UIScreen.UIScreen))]
    public class LowerLayerBlurFade: MonoBehaviour
    {
        public float _from = 0;
        public float _to = 1;
        public float _duration = 1;
        
        private UIScreen.UIScreen _screen;
        private float _elapsed;
        private float _endTime;

        private void OnEnable()
        {
            _screen = GetComponent<UIScreen.UIScreen>();
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup > _endTime)
                return;
            _elapsed += Time.deltaTime;
            _screen.SetLowerBlurStrength(Mathf.Lerp(_from, _to, _elapsed/_duration));
        }

        [ContextMenu("Play")]
        public void Play()
        {
            _elapsed = 0;
            _endTime = Time.realtimeSinceStartup + _duration;
        }
    }
}