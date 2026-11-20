using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace HyzMouseHub
{
    internal static class HidNative
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct SpDeviceInterfaceData
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HiddAttributes
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HidpCaps
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Overlapped
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public uint Offset;
            public uint OffsetHigh;
            public IntPtr hEvent;
        }

        internal const uint GenericRead = 0x80000000;
        internal const uint GenericWrite = 0x40000000;
        internal const uint FileShareRw = 0x00000003;
        internal const uint OpenExisting = 3;
        internal const uint FileFlagOverlapped = 0x40000000;
        internal const int ErrorIoPending = 997;

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_GetAttributes(IntPtr h, ref HiddAttributes a);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr pp);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_FreePreparsedData(IntPtr p);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern int HidP_GetCaps(IntPtr pp, ref HidpCaps caps);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool HidD_GetProductString(IntPtr h, StringBuilder sb, int len);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool HidD_GetManufacturerString(IntPtr h, StringBuilder sb, int len);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_SetOutputReport(IntPtr h, byte[] buf, int len);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_GetFeature(IntPtr h, byte[] buf, int len);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_SetFeature(IntPtr h, byte[] buf, int len);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr h, IntPtr devInfo, ref Guid guid, int index, ref SpDeviceInterfaceData data);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr h, ref SpDeviceInterfaceData data, IntPtr detail, int size, out int required, IntPtr devInfo);

        [DllImport("setupapi.dll", SetLastError = true)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr templ);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool WriteFile(IntPtr h, byte[] buf, int n, out int written, ref Overlapped ov);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool ReadFile(IntPtr h, byte[] buf, int n, out int read, ref Overlapped ov);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetOverlappedResult(IntPtr h, ref Overlapped ov, out int transferred, bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CancelIoEx(IntPtr h, ref Overlapped ov);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr CreateEvent(IntPtr sec, bool manual, bool initial, IntPtr name);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool ResetEvent(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(IntPtr h, uint ms);

        /// <summary>
        /// HID 路径的短哈希，用于区分同 VID:PID 的多个接收器。
        /// 路径在同一台机器上插同一USB口时稳定、换口时不同，正好满足“按设备区分”的需要。
        /// </summary>
        internal static string ShortHash(string value)
        {
            if (string.IsNullOrEmpty(value)) return "00000000";
            unchecked
            {
                uint hash = 2166136261; // FNV-1a
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619;
                }
                return hash.ToString("X8");
            }
        }

        internal static List<HidDeviceDescriptor> Enumerate()
        {
            List<HidDeviceDescriptor> result = new List<HidDeviceDescriptor>();
            Guid hidGuid = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");
            IntPtr list = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, 0x12);
            if (list == new IntPtr(-1)) return result;

            SpDeviceInterfaceData did = new SpDeviceInterfaceData();
            did.cbSize = Marshal.SizeOf(typeof(SpDeviceInterfaceData));
            int index = 0;
            while (SetupDiEnumDeviceInterfaces(list, IntPtr.Zero, ref hidGuid, index, ref did))
            {
                index++;
                int required = 0;
                SetupDiGetDeviceInterfaceDetail(list, ref did, IntPtr.Zero, 0, out required, IntPtr.Zero);
                if (required <= 0) continue;

                IntPtr buf = Marshal.AllocHGlobal(required);
                try
                {
                    Marshal.WriteInt32(buf, 0, IntPtr.Size == 8 ? 8 : 6);
                    int required2 = 0;
                    if (!SetupDiGetDeviceInterfaceDetail(list, ref did, buf, required, out required2, IntPtr.Zero)) continue;
                    string path = Marshal.PtrToStringUni(IntPtr.Add(buf, 4));
                    if (string.IsNullOrEmpty(path)) continue;

                    IntPtr h = CreateFile(path, 0, FileShareRw, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                    if (h == new IntPtr(-1)) continue;
                    try
                    {
                        HiddAttributes attrs = new HiddAttributes();
                        attrs.Size = Marshal.SizeOf(typeof(HiddAttributes));
                        if (!HidD_GetAttributes(h, ref attrs)) continue;

                        IntPtr pp = IntPtr.Zero;
                        HidpCaps caps = new HidpCaps();
                        caps.Reserved = new ushort[17];
                        bool ok = HidD_GetPreparsedData(h, out pp);
                        if (ok) ok = HidP_GetCaps(pp, ref caps) == 0x00110000;
                        if (pp != IntPtr.Zero) HidD_FreePreparsedData(pp);
                        if (!ok) continue;

                        HidDeviceDescriptor d = new HidDeviceDescriptor();
                        d.VendorId = attrs.VendorID;
                        d.ProductId = attrs.ProductID;
                        d.UsagePage = caps.UsagePage;
                        d.Usage = caps.Usage;
                        d.InputReportLength = caps.InputReportByteLength;
                        d.OutputReportLength = caps.OutputReportByteLength;
                        d.FeatureReportLength = caps.FeatureReportByteLength;
                        d.Path = path;

                        StringBuilder sb = new StringBuilder(256);
                        if (HidD_GetProductString(h, sb, 256)) d.ProductName = sb.ToString();
                        StringBuilder sb2 = new StringBuilder(256);
                        if (HidD_GetManufacturerString(h, sb2, 256)) d.Manufacturer = sb2.ToString();
                        result.Add(d);
                    }
                    finally { CloseHandle(h); }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            SetupDiDestroyDeviceInfoList(list);
            return result;
        }
    }

    internal sealed class HidSession : IDisposable
    {
        private IntPtr _handle = IntPtr.Zero;
        private IntPtr _event = IntPtr.Zero;
        private int _inputLen;
        private bool _closed;

        public HidSession(string path, int inputLen, int outputLen)
        {
            _inputLen = Math.Max(1, inputLen);
            _handle = HidNative.CreateFile(
                path,
                HidNative.GenericRead | HidNative.GenericWrite,
                HidNative.FileShareRw,
                IntPtr.Zero,
                HidNative.OpenExisting,
                HidNative.FileFlagOverlapped,
                IntPtr.Zero);
            if (_handle == new IntPtr(-1))
                throw new InvalidOperationException("无法打开 HID 设备，Win32 错误 " + Marshal.GetLastWin32Error());
            _event = HidNative.CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        }

        public bool Write(byte[] report, int timeoutMs)
        {
            if (_closed) return false;
            HidNative.ResetEvent(_event);
            HidNative.Overlapped ov = new HidNative.Overlapped();
            ov.hEvent = _event;
            int written = 0;
            bool ok = HidNative.WriteFile(_handle, report, report.Length, out written, ref ov);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (err != HidNative.ErrorIoPending) return false;
                if (HidNative.WaitForSingleObject(_event, (uint)timeoutMs) != 0)
                {
                    HidNative.CancelIoEx(_handle, ref ov);
                    return false;
                }
            }
            return true;
        }

        public bool SetOutputReport(byte[] report)
        {
            if (_closed) return false;
            return HidNative.HidD_SetOutputReport(_handle, report, report.Length);
        }

        public bool SetFeature(byte[] report)
        {
            if (_closed) return false;
            return HidNative.HidD_SetFeature(_handle, report, report.Length);
        }

        public bool GetFeature(byte[] report)
        {
            if (_closed) return false;
            return HidNative.HidD_GetFeature(_handle, report, report.Length);
        }

        public byte[] Read(int timeoutMs)
        {
            if (_closed) return null;
            HidNative.ResetEvent(_event);
            HidNative.Overlapped ov = new HidNative.Overlapped();
            ov.hEvent = _event;
            byte[] buf = new byte[_inputLen];
            int read = 0;
            bool ok = HidNative.ReadFile(_handle, buf, _inputLen, out read, ref ov);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (err != HidNative.ErrorIoPending) return null;
                if (HidNative.WaitForSingleObject(_event, (uint)timeoutMs) != 0)
                {
                    HidNative.CancelIoEx(_handle, ref ov);
                    HidNative.GetOverlappedResult(_handle, ref ov, out read, true);
                    return null;
                }
                if (!HidNative.GetOverlappedResult(_handle, ref ov, out read, false)) return null;
            }
            if (read <= 0) return null;
            byte[] result = new byte[read];
            Array.Copy(buf, result, read);
            return result;
        }

        public void Flush(int idleMs)
        {
            for (int i = 0; i < 24; i++)
            {
                if (Read(idleMs) == null) break;
            }
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            if (_event != IntPtr.Zero) { HidNative.CloseHandle(_event); _event = IntPtr.Zero; }
            if (_handle != IntPtr.Zero && _handle != new IntPtr(-1)) HidNative.CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
