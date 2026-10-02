using System.Collections.Generic;
using UnityEngine;

namespace FFCAccess
{
    /// <summary>
    /// Unity's GetKeyDown only fires once per press. Text boxes should repeat while a key is held,
    /// like Windows does: a short pause, then fast repeats.
    /// </summary>
    internal static class KeyRepeat
    {
        private const float FirstDelay = 0.45f;
        private const float Interval = 0.06f;

        private static readonly Dictionary<KeyCode, float> nextRepeat = new Dictionary<KeyCode, float>();

        /// <summary>True on the frame the key goes down, and then repeatedly while it's held.</summary>
        public static bool Pressed(KeyCode key)
        {
            float now = Time.unscaledTime;
            if (Input.GetKeyDown(key))
            {
                nextRepeat[key] = now + FirstDelay;
                return true;
            }
            if (!Input.GetKey(key))
            {
                nextRepeat.Remove(key);
                return false;
            }
            float next;
            if (nextRepeat.TryGetValue(key, out next) && now >= next)
            {
                nextRepeat[key] = now + Interval;
                return true;
            }
            return false;
        }
    }
}
