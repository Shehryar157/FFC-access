using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FFCAccess
{
    /// <summary>
    /// Unity's GetKeyDown only fires once per press. Text boxes should repeat while a key is held,
    /// like Windows does: a short pause, then fast repeats. The pause and the speed are the player's own
    /// Windows keyboard settings (Control Panel, Keyboard), but never slower than the mod's old fixed values.
    /// </summary>
    internal static class KeyRepeat
    {
        private static readonly float FirstDelay = 0.45f;
        private static readonly float Interval = 0.06f;

        private static readonly Dictionary<KeyCode, float> nextRepeat = new Dictionary<KeyCode, float>();

        [DllImport("user32", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);

        static KeyRepeat()
        {
            try
            {
                // Windows stores the repeat delay as 0 to 3 (250 to 1000 ms) and the speed as 0 to 31
                // (about 2.5 to 30 repeats per second).
                if (SystemParametersInfo(0x16, 0, out int delay, 0) && delay >= 0 && delay <= 3)
                {
                    FirstDelay = Mathf.Min(FirstDelay, (delay + 1) * 0.25f);
                }
                if (SystemParametersInfo(0x0A, 0, out int speed, 0) && speed >= 0 && speed <= 31)
                {
                    Interval = Mathf.Min(Interval, 1f / (2.5f + speed * 27.5f / 31f));
                }
            }
            catch
            {
                // Keep the built-in values.
            }
        }

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
