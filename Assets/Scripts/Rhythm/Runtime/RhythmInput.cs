using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Necronomi.Rhythm
{
    /// <summary>
    /// Lane keys, built in code so no .inputactions asset has to be edited to add a game mode.
    /// Reports how long ago the press actually happened, which the controller subtracts from
    /// the song clock: a press that landed mid frame is judged where it landed, not where the
    /// frame ended.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RhythmInput : MonoBehaviour
    {
        [Tooltip("One control path per lane, in lane order")]
        [SerializeField] private string[] laneBindings =
        {
            "<Keyboard>/d",
            "<Keyboard>/f",
            "<Keyboard>/j",
            "<Keyboard>/k"
        };

        [Tooltip("Fallback used when the Input System package is not active")]
        [SerializeField] private KeyCode[] legacyKeys = { KeyCode.D, KeyCode.F, KeyCode.J, KeyCode.K };

        [Tooltip("Upper bound on the press age correction, seconds. Guards against stale timestamps")]
        [SerializeField, Range(0f, 0.2f)] private float maxPressAge = 0.05f;

        /// <summary>Lane index, and how many seconds ago the press happened.</summary>
        public event Action<int, double> OnLanePressed;

        /// <summary>Lane index, and how many seconds ago the release happened.</summary>
        public event Action<int, double> OnLaneReleased;

        public bool InputEnabled { get; set; } = true;

        public int LaneCount =>
#if ENABLE_INPUT_SYSTEM
            laneBindings != null ? laneBindings.Length : 0;
#else
            legacyKeys != null ? legacyKeys.Length : 0;
#endif

        private bool[] held;

#if ENABLE_INPUT_SYSTEM
        private InputAction[] actions;
#endif

        public bool IsHeld(int lane) => held != null && lane >= 0 && lane < held.Length && held[lane];

        private void Awake()
        {
            held = new bool[Mathf.Max(1, LaneCount)];

#if ENABLE_INPUT_SYSTEM
            if (laneBindings == null)
                return;

            actions = new InputAction[laneBindings.Length];
            for (int i = 0; i < laneBindings.Length; i++)
            {
                actions[i] = new InputAction($"RhythmLane{i}", InputActionType.Button, laneBindings[i]);

                int lane = i;
                actions[i].performed += context => HandlePressed(lane, context.time);
                actions[i].canceled += context => HandleReleased(lane, context.time);
            }
#endif
        }

        private void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            if (actions == null)
                return;

            for (int i = 0; i < actions.Length; i++)
                actions[i].Enable();
#endif
        }

        private void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            if (actions == null)
                return;

            for (int i = 0; i < actions.Length; i++)
                actions[i].Disable();
#endif

            if (held == null)
                return;

            Array.Clear(held, 0, held.Length);
        }

#if !ENABLE_INPUT_SYSTEM
        private void Update()
        {
            if (legacyKeys == null)
                return;

            for (int i = 0; i < legacyKeys.Length; i++)
            {
                if (Input.GetKeyDown(legacyKeys[i]))
                    HandlePressed(i, Time.realtimeSinceStartupAsDouble);
                else if (Input.GetKeyUp(legacyKeys[i]))
                    HandleReleased(i, Time.realtimeSinceStartupAsDouble);
            }
        }
#endif

        private void HandlePressed(int lane, double eventTime)
        {
            if (!InputEnabled || held == null || lane >= held.Length)
                return;

            held[lane] = true;
            OnLanePressed?.Invoke(lane, PressAge(eventTime));
        }

        private void HandleReleased(int lane, double eventTime)
        {
            if (held == null || lane >= held.Length)
                return;

            held[lane] = false;

            if (InputEnabled)
                OnLaneReleased?.Invoke(lane, PressAge(eventTime));
        }

        /// <summary>
        /// Input timestamps run on the realtime clock. Convert to an age in seconds so the
        /// caller can subtract it from whatever clock it uses.
        /// </summary>
        private double PressAge(double eventTime)
        {
            double age = Time.realtimeSinceStartupAsDouble - eventTime;
            return Math.Clamp(age, 0d, maxPressAge);
        }
    }
}
