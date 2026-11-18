using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HyzMouseHub
{
    /// <summary>
    /// 通过 Windows Core Audio 读取/调节 GSA 70 等 USB 音频端点的系统音量。
    /// 这是 Windows 的标准音频接口，不依赖厂商私有 HID 协议，也不会伪造数值。
    /// 仅操作名称匹配音频设备的播放端点，且只改该端点音量。
    /// </summary>
    internal sealed class VolumeService
    {
        private const int DEVICE_STATE_ACTIVE = 0x1;
        private const int RENDER = 0;
        private const int STGM_READ = 0;
        private static readonly Guid IID_IAudioEndpointVolume =
            new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        private static readonly Guid PKEY_Device_FriendlyName =
            new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0");
        private const int PKEY_Device_FriendlyName_Pid = 14;

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IMMDeviceCollection ppDevices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
            int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice ppDevice);
            int RegisterEndpointNotificationCallback(IntPtr pClient);
            int UnregisterEndpointNotificationCallback(IntPtr pClient);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            int GetCount(out int pcDevices);
            int Item(int nDevice, out IMMDevice ppDevice);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
            int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
            int GetState(out int pdwState);
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            int GetCount(out int cProps);
            int GetAt(int iProp, out PROPERTYKEY pkey);
            int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
            int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
            int Commit();
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out int pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetMute(bool bMute, ref Guid pguidEventContext);
            int GetMute(out bool pbMute);
            int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
            int VolumeStepUp(ref Guid pguidEventContext);
            int VolumeStepDown(ref Guid pguidEventContext);
            int QueryHardwareSupport(out uint pdwHardwareSupportMask);
            int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPERTYKEY
        {
            public Guid fmtid;
            public int pid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPVARIANT
        {
            public ushort vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;
            public IntPtr pwszVal;
        }

        [DllImport("ole32.dll")]
        private static extern void PropVariantClear(ref PROPVARIANT pvar);

        internal sealed class Endpoint
        {
            public string Id;
            public string Name;
            public override string ToString() { return Name; }
        }

        /// <summary>列出名称疑似 EPOS / GSA / Sennheiser 的播放端点。</summary>
        internal static List<Endpoint> ListHeadsetEndpoints()
        {
            List<Endpoint> result = new List<Endpoint>();
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection collection = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                if (enumerator.EnumAudioEndpoints(RENDER, DEVICE_STATE_ACTIVE, out collection) != 0) return result;
                int count;
                if (collection.GetCount(out count) != 0) return result;
                for (int i = 0; i < count; i++)
                {
                    IMMDevice device;
                    if (collection.Item(i, out device) != 0 || device == null) continue;
                    try
                    {
                        string name = FriendlyName(device);
                        string id;
                        if (device.GetId(out id) != 0) continue;
                        if (LooksLikeHeadset(name)) result.Add(new Endpoint { Id = id, Name = name });
                    }
                    catch { }
                    finally { Marshal.ReleaseComObject(device); }
                }
            }
            catch { }
            finally
            {
                if (collection != null) Marshal.ReleaseComObject(collection);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
            return result;
        }

        private static bool LooksLikeHeadset(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            name = name.ToLowerInvariant();
            return name.Contains("epos") || name.Contains("gsa 70") || name.Contains("gsa70") ||
                   name.Contains("gsp 670") || name.Contains("gsp670") || name.Contains("sennheiser");
        }

        private static string FriendlyName(IMMDevice device)
        {
            IPropertyStore store = null;
            try
            {
                if (device.OpenPropertyStore(STGM_READ, out store) != 0 || store == null) return "";
                PROPERTYKEY key = new PROPERTYKEY { fmtid = PKEY_Device_FriendlyName, pid = PKEY_Device_FriendlyName_Pid };
                PROPVARIANT value;
                if (store.GetValue(ref key, out value) != 0) return "";
                try
                {
                    return value.pwszVal == IntPtr.Zero ? "" : Marshal.PtrToStringUni(value.pwszVal) ?? "";
                }
                finally { PropVariantClear(ref value); }
            }
            catch { return ""; }
            finally { if (store != null) Marshal.ReleaseComObject(store); }
        }

        /// <summary>返回 0-100，-1 表示读取失败。</summary>
        internal static int GetVolume(string endpointId)
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            object com = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                if (enumerator.GetDevice(endpointId, out device) != 0 || device == null) return -1;
                Guid iid = IID_IAudioEndpointVolume;
                if (device.Activate(ref iid, 1, IntPtr.Zero, out com) != 0 || com == null) return -1;
                IAudioEndpointVolume volume = (IAudioEndpointVolume)com;
                float level;
                if (volume.GetMasterVolumeLevelScalar(out level) != 0) return -1;
                return (int)Math.Round(level * 100f);
            }
            catch { return -1; }
            finally
            {
                if (com != null) Marshal.ReleaseComObject(com);
                if (device != null) Marshal.ReleaseComObject(device);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }

        /// <summary>设置 0-100 的系统音量，返回是否成功。</summary>
        internal static bool SetVolume(string endpointId, int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            object com = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                if (enumerator.GetDevice(endpointId, out device) != 0 || device == null) return false;
                Guid iid = IID_IAudioEndpointVolume;
                if (device.Activate(ref iid, 1, IntPtr.Zero, out com) != 0 || com == null) return false;
                Guid context = Guid.Empty;
                IAudioEndpointVolume volume = (IAudioEndpointVolume)com;
                return volume.SetMasterVolumeLevelScalar(percent / 100f, ref context) == 0;
            }
            catch { return false; }
            finally
            {
                if (com != null) Marshal.ReleaseComObject(com);
                if (device != null) Marshal.ReleaseComObject(device);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }
    }
}
