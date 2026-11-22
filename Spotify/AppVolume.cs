using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace WidgetNotes.Spotify
{
    /// <summary>
    /// Lautstärke der Spotify-App im Windows-Lautstärkemixer (Core Audio) – funktioniert ohne Spotify-Konto.
    /// </summary>
    internal static class AppVolume
    {
        private static Guid _context = Guid.Empty;

        /// <summary>Lautstärke 0–100 oder null, wenn Spotify gerade keine Audio-Sitzung hat.</summary>
        public static int? Get()
        {
            int? result = null;
            ForEachSpotifySession(v =>
            {
                if (result == null && v.GetMasterVolume(out var level) == 0) result = (int)Math.Round(level * 100);
            });
            return result;
        }

        public static bool Set(int pct)
        {
            bool any = false;
            float level = Math.Clamp(pct, 0, 100) / 100f;
            ForEachSpotifySession(v =>
            {
                v.SetMasterVolume(level, ref _context);
                any = true;
            });
            return any;
        }

        private static void ForEachSpotifySession(Action<ISimpleAudioVolume> action)
        {
            var pids = Process.GetProcessesByName("Spotify").Select(p => { using (p) return (uint)p.Id; }).ToHashSet();
            if (pids.Count == 0) return;

            object? enumeratorObj = null;
            try
            {
                enumeratorObj = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!);
                var enumerator = (IMMDeviceEnumerator)enumeratorObj!;
                if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device) != 0) return;

                var iid = typeof(IAudioSessionManager2).GUID;
                if (device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var managerObj) != 0) return;
                var manager = (IAudioSessionManager2)managerObj;
                if (manager.GetSessionEnumerator(out var sessions) != 0) return;
                sessions.GetCount(out int count);
                for (int i = 0; i < count; i++)
                {
                    if (sessions.GetSession(i, out var session) != 0) continue;
                    try
                    {
                        if (session.GetProcessId(out var pid) == 0 && pids.Contains(pid))
                            action((ISimpleAudioVolume)session);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(session);
                    }
                }
                Marshal.ReleaseComObject(sessions);
                Marshal.ReleaseComObject(manager);
                Marshal.ReleaseComObject(device);
            }
            catch
            {
                // Kein Audiogerät o. Ä. – dann eben keine Lautstärke.
            }
            finally
            {
                if (enumeratorObj != null) Marshal.ReleaseComObject(enumeratorObj);
            }
        }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
        }

        [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            // IAudioSessionControl
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName(IntPtr value, ref Guid context);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath(IntPtr value, ref Guid context);
            [PreserveSig] int GetGroupingParam(out Guid param);
            [PreserveSig] int SetGroupingParam(ref Guid param, ref Guid context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
            // IAudioSessionControl2
            [PreserveSig] int GetSessionIdentifier(out IntPtr id);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
            [PreserveSig] int GetProcessId(out uint pid);
            [PreserveSig] int IsSystemSoundsSession();
            [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float level, ref Guid context);
            [PreserveSig] int GetMasterVolume(out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }
    }
}
