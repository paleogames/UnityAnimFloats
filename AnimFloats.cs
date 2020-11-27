using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Paleo.Common {

    /// <summary>
    /// Interpolate a collection of numeric values within a given time frame.
    /// </summary>
    public sealed class AnimFloats {

        public enum Mode : ushort {

            /// <summary>
            /// Play once.
            /// </summary>
            FORWARD,

            /// <summary>
            /// Play once in reverse.
            /// </summary>
            REVERSE,

            /// <summary>
            /// Play forward until stopped.
            /// </summary>
            FORWARD_LOOP,

            /// <summary>
            /// Play forward, then in reverse, then forward, etc... until stopped.
            /// </summary>
            REVERSE_LOOP
        }

        struct ValData {
            public Action<float> Apply;
            public AnimationCurve curve;
            public float start, end;
        }

        Coroutine _currentRoutine;
        List<ValData> _vals;
        MonoBehaviour _host;
        WaitForSeconds _timer;
        readonly decimal _duration;
        decimal _startTime;
        bool _interrupt;

        /// <summary>
        /// Initialize an Animator.
        /// </summary>
        /// <param name="host">Monobehavior component to host the anim coroutine. Usually the sender.</param>
        /// <param name="duration">Total duration of the animation in seconds.</param>
        /// <param name="updateFrequency">Update value(s) every this many seconds.</param>
        public AnimFloats(MonoBehaviour host, float duration, float updateFrequency) {

            _host = host;
            // Stored in milliseconds.
            _duration = (decimal)duration * 1000;
            _timer = new WaitForSeconds(updateFrequency);
            _vals = new List<ValData>();
        }

        /// <summary>
        /// Add a parameter to the current animation.
        /// </summary>
        /// <param name="Apply">Method called on each val update.</param>
        /// <param name="c">Curve applied to each animation step.</param>
        /// <param name="start">Initial value.</param>
        /// <param name="end">Target value.</param>
        public void AddVal(Action<float> Apply, AnimationCurve c, float start, float end) {

            _vals.Add(new ValData() {
                Apply = Apply, curve = c, start = start, end = end
            });
        }

        /// <summary>
        /// Execute the animation. The most recent call takes over a running animation.
        /// </summary>
        /// <param name="m">Lookup enum inline doc for usage.</param>
        /// <param name="Done">Called when the animation is over, or interrupted.</param>
        public void Run(Mode m = Mode.FORWARD, Action Done = null) {

            if (_vals.Count == 0) {
                throw new Exception("Attempting to run an empty animator.");
            }
            if (_currentRoutine != null) {
                _host.StopCoroutine(_currentRoutine);
            }
            _interrupt = false;
            _startTime = DateTime.Now.Ticks;
            _currentRoutine = _host.StartCoroutine(UpdateVals(m, Done));
        }

        /// <summary>
        /// Stop animation according to its play mode.
        /// FORWARD, REVERSE: Return to start value.
        /// FORWARD_LOOP: Finish current iteration and stop.
        /// REVERSE_LOOP: Finish current iteration, adds a reverse iteration to return
        ///               to initial value if needed.
        /// </summary>
        public void Stop() {

            _interrupt = true;
        }

        private IEnumerator UpdateVals(Mode m, Action Done) {

            bool finished = false;
            bool forward = !m.Equals(Mode.REVERSE);
            bool loop = m.Equals(Mode.FORWARD_LOOP) || m.Equals(Mode.REVERSE_LOOP);

            while (!finished) {

                yield return _timer;

                // Stop requested, not looping: backtrack.
                if (_interrupt && !loop) {
                    forward = !forward;
                    _interrupt = false;
                    _startTime = DateTime.Now.Ticks - (_duration * TimeSpan.TicksPerMillisecond
                        - (DateTime.Now.Ticks - _startTime));
                }
                float normalizedTime = (float)((DateTime.Now.Ticks - _startTime)
                    / TimeSpan.TicksPerMillisecond / _duration);

                if (normalizedTime > 1f) {
                    finished = true;
                }
                float evalTime = forward ? normalizedTime : 1 - normalizedTime;

                foreach (ValData vd in _vals) {

                    // Apply curve value, or target value at the end of a cycle.
                    float val = finished ? forward ? vd.end : vd.start
                        : Mathf.Lerp(vd.start, vd.end, vd.curve.Evaluate(evalTime));

                    vd.Apply(val);
                }
                // Loop overrides finished behavior.
                if (finished && loop) {
                    _startTime = DateTime.Now.Ticks;
                    if (m.Equals(Mode.REVERSE_LOOP)) {
                        forward = !forward;
                        // Add an extra reverse cycle if needed.
                        finished = _interrupt && forward ? true : false;
                    } else {
                        finished = _interrupt;
                    }
                }
            }
            Done?.Invoke();
        }
    }
}