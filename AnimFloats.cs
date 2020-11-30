using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ********************************
/// *** Paleo Games LLC · © 2020 ***
/// ********************************
/// View LICENSE file for usage information.
/// </summary>
namespace Paleo.Common {

    /// <summary>
    /// Interpolate numeric values within a given time frame.
    /// </summary>
    public sealed class AnimFloats {

        public enum PlayMode : ushort {

            /// <summary>
            /// Play once.
            /// </summary>
            FORWARD,

            /// <summary>
            /// Play once in reverse.
            /// </summary>
            REVERSE,

            /// <summary>
            /// Play forward repeatingly until stopped.
            /// </summary>
            FORWARD_LOOP,

            /// <summary>
            /// Play forward, then in reverse, then forward, etc... until stopped.
            /// </summary>
            REVERSE_LOOP
        }

        // Value 0 is reserved for not stopping.
        public enum StopMode : ushort {

            /// <summary>
            /// Interrupt the animation at its current value.
            /// </summary>
            IMMEDIATE = 1,

            /// <summary>
            /// Stop the animation at the end of the current cycle.
            /// In the forward cycle of a REVERSE_LOOP, a reverse cycle is added to get back to start value.
            /// </summary>
            FINISH_CYCLE = 2,

            /// <summary>
            /// Backtrack the current cycle (if forward), and stop the animation.
            /// </summary>
            FINISH_CYCLE_REVERSE = 3
        }

        struct AnimData {

            public Action<float> Apply;
            public AnimationCurve curve;
            public float start, end;
        }

        Coroutine _coroutine;
        List<AnimData> _anims;
        MonoBehaviour _host;
        WaitForSeconds _timer;
        readonly decimal _duration;
        StopMode _interrupt = 0;

        /// <param name="host">Monobehavior component to host the anim coroutine. Usually the sender.</param>
        /// <param name="duration">Total duration of the animation in seconds.</param>
        /// <param name="fps">Update animation this many times per seconds.</param>
        public AnimFloats(MonoBehaviour host, float duration, float fps) {

            _host = host;
            // Stored in milliseconds.
            _duration = (decimal)duration * 1000;
            _timer = new WaitForSeconds(1f / fps);
            _anims = new List<AnimData>();
        }

        /// <summary>
        /// Add a parameter to the current animation.
        /// </summary>
        /// <param name="c">Curve applied to each animation step.</param>
        /// <param name="start">Initial value.</param>
        /// <param name="end">Target value.</param>
        /// <param name="Apply">Method called on each val update.</param>
        public void AddVal(AnimationCurve c, float start, float end, Action<float> Apply) {

            _anims.Add(new AnimData() {
                Apply = Apply, curve = c, start = start, end = end
            });
        }

        public bool IsPlaying() {
            return _coroutine != null;
        }

        /// <summary>
        /// Execute the animation. The most recent call takes over a running animation.
        /// </summary>
        /// <param name="m">Lookup enum inline doc for usage.</param>
        /// <param name="Done">Called when the animation is over, or stopped.</param>
        public void Play(PlayMode m = PlayMode.FORWARD, Action Done = null) {

            if (_anims.Count == 0) {
                throw new Exception("Attempting to run an empty animator.");
            }
            if (_coroutine != null) {
                _host.StopCoroutine(_coroutine);
            }
            _interrupt = 0;
            _coroutine = _host.StartCoroutine(UpdateVals(m, Done));
        }

        /// <summary>
        /// Terminate the animation.
        /// </summary>
        /// <param name="m">Lookup enum inline doc for usage.</param>
        public void Stop(StopMode m = StopMode.FINISH_CYCLE) {

            if (m.Equals(StopMode.IMMEDIATE) && _coroutine != null) {
                _host.StopCoroutine(_coroutine);
                _coroutine = null;
            } else {
                _interrupt = m;
            }
        }

        private IEnumerator UpdateVals(PlayMode m, Action Done) {

            bool finished = false;
            bool forward = !m.Equals(PlayMode.REVERSE);
            bool loop = m.Equals(PlayMode.FORWARD_LOOP) || m.Equals(PlayMode.REVERSE_LOOP);
            decimal startTime = DateTime.Now.Ticks;

            while (!finished) {

                yield return _timer;

                if (_interrupt.Equals(StopMode.FINISH_CYCLE_REVERSE)) {
                    _interrupt = 0;
                    loop = false;
                    // Backtrack forward cycle.
                    if (forward) {
                        forward = false;
                        // Override start time to duration minus what was left to play.
                        startTime = DateTime.Now.Ticks - (_duration * TimeSpan.TicksPerMillisecond
                            - (DateTime.Now.Ticks - startTime));
                    }
                }
                float normalizedTime = (float)((DateTime.Now.Ticks - startTime)
                    / TimeSpan.TicksPerMillisecond / _duration);

                if (normalizedTime > 1f) {
                    finished = true;
                }
                float evalTime = forward ? normalizedTime : 1f - normalizedTime;

                foreach (AnimData anim in _anims) {

                    // Apply curve value, or target value at the end of a cycle.
                    float val = finished ? forward ? anim.end : anim.start
                        : Mathf.Lerp(anim.start, anim.end, anim.curve.Evaluate(evalTime));

                    anim.Apply(val);
                }
                // Loops override finished behavior.
                if (finished && loop) {
                    if (m.Equals(PlayMode.REVERSE_LOOP)) {
                        forward = !forward;
                        // Add a reverse cycle to get back to start value.
                        finished = _interrupt.Equals(StopMode.FINISH_CYCLE) && forward ? true : false;
                    } else {
                        finished = _interrupt > 0;
                    }
                    startTime = DateTime.Now.Ticks;
                }
            }
            _coroutine = null;
            Done?.Invoke();
        }
    }
}