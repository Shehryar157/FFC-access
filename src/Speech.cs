using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FFCAccess
{
    /// <summary>
    /// Speech output through Tolk: NVDA, JAWS and other screen readers, with SAPI as fallback.
    /// </summary>
    internal static class Speech
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string path);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string path);

        [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Tolk_Load();

        [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Tolk_Unload();

        [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Tolk_TrySAPI([MarshalAs(UnmanagedType.I1)] bool trySapi);

        [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Tolk_PreferSAPI([MarshalAs(UnmanagedType.I1)] bool preferSapi);

        [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Tolk_DetectScreenReader();

        [DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool Tolk_Output(string str, [MarshalAs(UnmanagedType.I1)] bool interrupt);

        [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool Tolk_Silence();

        private static bool loaded;

        /// <summary>Last text spoken, for the repeat key.</summary>
        public static string Last = "";

        /// <summary>Time of the last high-priority message; focus speech queues behind it instead of cutting it off.</summary>
        private static float priorityUntil;

        public static string ReaderName { get; private set; } = "none";

        public static void Init(string pluginDir, bool preferSapi)
        {
            try
            {
                // Tolk loads its screen reader drivers (nvdaControllerClient64.dll etc.) by name, so they
                // must be findable: point the DLL search path at our plugin folder.
                SetDllDirectory(pluginDir);
                // Tolk asks Windows for its screen reader drivers by name. Windows doesn't always search our folder
                // (the search-path setting above didn't hold inside the game), so load them ourselves first: once a
                // DLL is loaded, a request for it by name gets the copy already in memory.
                foreach (string driver in new[] { "nvdaControllerClient64.dll", "SAAPI64.dll" })
                {
                    IntPtr h = LoadLibrary(Path.Combine(pluginDir, driver));
                    Plugin.Log.LogInfo("Preload " + driver + ": " + (h != IntPtr.Zero ? "ok" : "failed, error " + Marshal.GetLastWin32Error()));
                }
                if (LoadLibrary(Path.Combine(pluginDir, "Tolk.dll")) == IntPtr.Zero)
                {
                    Plugin.Log.LogError("Could not load Tolk.dll from " + pluginDir);
                    return;
                }
                Tolk_TrySAPI(true);
                Tolk_PreferSAPI(preferSapi);
                Tolk_Load();
                loaded = true;
                IntPtr name = Tolk_DetectScreenReader();
                ReaderName = name == IntPtr.Zero ? "none" : Marshal.PtrToStringUni(name);
                Plugin.Log.LogInfo("Speech ready. Screen reader: " + ReaderName);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Speech init failed: " + e);
            }
        }

        public static void SetPreferSapi(bool prefer)
        {
            if (loaded)
            {
                try { Tolk_PreferSAPI(prefer); } catch { }
            }
        }

        public static void Shutdown()
        {
            if (loaded)
            {
                try { Tolk_Unload(); } catch { }
                loaded = false;
            }
        }

        /// <summary>Speak text. interrupt cuts off whatever is being said.</summary>
        public static void Say(string text, bool interrupt = true)
        {
            text = TextUtil.Clean(text);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            Last = text;
            Plugin.Log.LogInfo("SAY" + (interrupt ? "!" : " ") + ": " + text);
            if (!loaded)
            {
                return;
            }
            try
            {
                Tolk_Output(text, interrupt);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Tolk_Output failed: " + e.Message);
            }
        }

        /// <summary>
        /// Speak something important (new screen, popup, section text). Focus messages that arrive
        /// shortly afterwards are queued instead of interrupting it.
        /// </summary>
        public static void SayPriority(string text, bool interrupt = true, float protectSeconds = 1.5f)
        {
            Say(text, interrupt);
            priorityUntil = Time.unscaledTime + protectSeconds;
        }

        /// <summary>
        /// Speak something the game did on its own (a roll result, a combat message, a popup, a new section).
        /// Events arriving close together queue up instead of cutting each other off.
        /// </summary>
        public static void SayEvent(string text, float protectSeconds = 1.5f)
        {
            bool interrupt = Time.unscaledTime > priorityUntil;
            Say(text, interrupt);
            priorityUntil = Mathf.Max(priorityUntil, Time.unscaledTime + protectSeconds);
        }

        /// <summary>Speak a focus change: interrupts, unless a priority message was just spoken.</summary>
        public static void SayFocus(string text)
        {
            Say(text, Time.unscaledTime > priorityUntil);
        }

        public static void Silence()
        {
            if (loaded)
            {
                try { Tolk_Silence(); } catch { }
            }
        }
    }
}
