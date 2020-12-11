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

        public enum PlayMode : byte {

            /// <summary>
            /// Play once.
            /// </summary>
            FORWARD,

            /// <summary>
            /// Play once in reverse.
            /// </summary>
            REVERSE,

            /// <summary>
            /// Once forward, once in reverse.
            /// </summary>
            FORWARD_REVERSE,

            /// <summary>
            /// Play forward repeatingly until stopped.
            /// </summary>
            LOOP_FORWARD,

            /// <summary>
            /// Play forward, then in reverse, then forward, etc... until stopped.
            /// </summary>
            LOOP_FORWARD_REVERSE
        }

        // Value 0 is reserved for not stopping.
        public enum StopMode : byte {

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
            public float min, max;
            public bool interpolate;
        }

        /// <summary>
        /// Prevent two instances with the same label from playing at the same time.
        /// </summary>
        static Dictionary<string, bool> _labelIsPlaying = new Dictionary<string, bool>();

        readonly decimal _duration;
        readonly string _label;
        Coroutine _coroutine;
        List<AnimData> _anims;
        MonoBehaviour _host;
        StopMode _interrupt = 0;
        WaitForSeconds _timer;

        /// <param name="host">Monobehavior component to host the anim coroutine. Usually the sender.</param>
        /// <param name="duration">Total duration of the animation in seconds.</param>
        /// <param name="fps">Update animation this many times per seconds.</param>
        /// <param name="label">Prevent two instances with the same label from playing at the same time.</param>
        public AnimFloats(MonoBehaviour host, float duration, float fps, string label = null) {

            _host = host;
            // Stored in milliseconds.
            _duration = (decimal)duration * 1000;
            _timer = new WaitForSeconds(1f / fps);
            _anims = new List<AnimData>();

            if (!string.IsNullOrEmpty(label)) {
                if (!_labelIsPlaying.ContainsKey(label)) {
                    _labelIsPlaying.Add(label, false);
                }
                _label = label;
            }
        }

        /// <summary>
        /// The value of the curve is injected directly in Apply().
        /// </summary>
        /// <param name="c">Curve applied to each animation step.</param>
        /// <param name="Apply">Method called on each val update.</param>
        public void AddProperty(AnimationCurve c, Action<float> Apply) {
            AddProperty(c, 0f, 0f, Apply);
        }

        /// <summary>
        /// Interpolates between min and max values according to the curve.
        /// </summary>
        /// <param name="c">Curve applied to each animation step.</param>
        /// <param name="min">When the anim curve is at its lowest.</param>
        /// <param name="max">When the anim curve is at its highest.</param>
        /// <param name="Apply">Method called on each val update.</param>
        public void AddProperty(AnimationCurve c, float min, float max, Action<float> Apply) {

            _anims.Add(new AnimData() {
                Apply = Apply, curve = c, min = min, max = max, interpolate = !min.Equals(max)
            });
        }

        /// <returns>True if this instance is playing.</returns>
        public bool IsPlaying() {
            return _coroutine != null;
        }

        /// <summary>
        /// Execute the animation. The most recent call takes over a running animation.
        /// </summary>
        /// <param name="m">Lookup enum inline doc for usage.</param>
        /// <param name="EndCycle">Called at the end of anim, loop cycle, or when stopped.</param>
        /// <returns>False when another animation with the same label is already playing.</returns>
        public bool Play(PlayMode m = PlayMode.FORWARD, Action EndCycle = null) {

            if (_anims.Count == 0) {
                throw new Exception("Attempting to run an empty animator.");
            }
            if (IsPlaying()) {
                _host.StopCoroutine(_coroutine);
                HasStopped(null);
            }
            if (!string.IsNullOrEmpty(_label)) {

                // Other instance with same label playing.
                if (_labelIsPlaying[_label]) {
                    return false;
                }
                _labelIsPlaying[_label] = true;
            }
            _interrupt = 0;
            _coroutine = _host.StartCoroutine(UpdateVals(m, EndCycle));
            return true;
        }

        /// <summary>
        /// Terminate the animation.
        /// </summary>
        /// <param name="m">Lookup enum inline doc for usage.</param>
        public void Stop(StopMode m = StopMode.FINISH_CYCLE) {
            _interrupt = m;
        }

        /// <summary>
        /// Cleanup after anim.
        /// </summary>
        private void HasStopped(Action Done) {

            _coroutine = null;
            if (!string.IsNullOrEmpty(_label)) {
                _labelIsPlaying[_label] = false;
            }
            Done?.Invoke();
        }

        private IEnumerator UpdateVals(PlayMode m, Action EndCycle) {

            bool finished = false;
            bool forward = !m.Equals(PlayMode.REVERSE);
            bool loop = m.Equals(PlayMode.LOOP_FORWARD) || m.Equals(PlayMode.LOOP_FORWARD_REVERSE);
            decimal startTime = DateTime.Now.Ticks;

            while (!finished && !_interrupt.Equals(StopMode.IMMEDIATE)) {

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

                    // Original values are applied at the end of a cycle.
                    if (anim.interpolate) {
                        anim.Apply(finished ? forward ? anim.max : anim.min
                            : Mathf.Lerp(anim.min, anim.max, anim.curve.Evaluate(evalTime)));
                    } else {
                        anim.Apply(finished ? forward ? anim.curve.Evaluate(1f) : anim.curve.Evaluate(0f)
                            : anim.curve.Evaluate(evalTime));
                    }
                }
                // Override finished behavior.
                if (finished) {
                    if (loop) {
                        if (m.Equals(PlayMode.LOOP_FORWARD_REVERSE)) {
                            forward = !forward;
                            // Add a reverse cycle to get back to start value.
                            finished = _interrupt.Equals(StopMode.FINISH_CYCLE) && forward ? true : false;
                        } else {
                            finished = _interrupt > 0;
                        }
                        // Callback each loop cycle, except beginning of reverse cycle.
                        if (forward && !finished) {
                            EndCycle?.Invoke();
                        }
                    } else if (m.Equals(PlayMode.FORWARD_REVERSE) && _interrupt == 0 && forward) {
                        // One reverse play for this mode.
                        forward = !forward;
                        finished = false;
                    }
                    startTime = DateTime.Now.Ticks;
                }
            }
            HasStopped(EndCycle);
        }
    }
}