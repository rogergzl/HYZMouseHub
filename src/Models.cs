using System;
using System.Collections.Generic;

namespace HyzMouseHub
{
    // 设备外形类别：托盘图标据此选择鼠标 / 键盘 / 耳机轮廓，并在数字模式右上角加角标。
    public enum DeviceKind
    {
        Mouse,
        Keyboard,
        Headset
    }

    public sealed class HidDeviceDescriptor
    {
        public ushort VendorId;
        public ushort ProductId;
        public ushort UsagePage;
        public ushort Usage;
        public int InputReportLength;
        public int OutputReportLength;
        public int FeatureReportLength;
        public string Path;
        public string ProductName;
        public string Manufacturer;

        public string VidPid
        {
            get { return VendorId.ToString("X4") + ":" + ProductId.ToString("X4"); }
        }
    }

    public sealed class DeviceInfo
    {
        public string Id;
        public string Provider;
        public string Brand;
        public string Model;
        public string Name;
        public ushort VendorId;
        public ushort ProductId;
        public string HidPath;
        public bool SupportsBattery = true;
        public bool SupportsDpi;
        public DeviceKind Kind = DeviceKind.Mouse;
        public int DpiMin = 200;
        public int DpiMax = 26000;
        public int DpiStep = 10;
        public string DpiNote = "";
        public string Sensor = "";

        public string ShortName
        {
            get { return string.IsNullOrEmpty(Model) ? Name : Brand + " " + Model; }
        }

        public string VidPid
        {
            get { return VendorId.ToString("X4") + ":" + ProductId.ToString("X4"); }
        }
    }

    public sealed class BatteryReading
    {
        public int Percent = -1;
        public bool Charging;
        public bool Online;
        public int Millivolts;
        public string Detail = "";
        public string Error = "";
        // 电池健康度说明：本程序支持的外设 HID 协议均不上报 SoH/循环次数，
        // 这里只展示硬件真实提供的信息（如 ATK 电压），不伪造健康百分比。
        public string HealthText = "";
        public DateTime UpdatedAt = DateTime.Now;

        public string StatusText
        {
            get
            {
                if (Percent < 0) return Online ? "--" : (Sleeping ? "休眠" : "--");
                return Percent + "%" + (Charging ? " 充电中" : "");
            }
        }

        public bool Sleeping
        {
            get { return !Online && string.IsNullOrEmpty(Error) == false && Error.Contains("休眠"); }
        }
    }

    public sealed class DpiReading
    {
        public int X = -1;
        public int Y = -1;
        public int Stage = -1;
        public int StageCount = -1;
        public string Error = "";

        public string Text
        {
            get
            {
                if (X <= 0) return string.IsNullOrEmpty(Error) ? "未知" : Error;
                if (Y > 0 && Y != X) return X + " × " + Y;
                if (Stage > 0) return X + "  (档 " + Stage + ")";
                return X.ToString();
            }
        }
    }

    public sealed class DeviceSnapshot
    {
        public DeviceInfo Info;
        public BatteryReading Battery = new BatteryReading();
        public DpiReading Dpi = new DpiReading();
    }

    public interface IDeviceProvider
    {
        string Name { get; }
        List<DeviceInfo> Discover(List<HidDeviceDescriptor> catalog);
        BatteryReading ReadBattery(DeviceInfo device);
        DpiReading ReadDpi(DeviceInfo device);
        bool WriteDpi(DeviceInfo device, int x, int y, int stage);
    }

    public abstract class DeviceProviderBase : IDeviceProvider
    {
        public abstract string Name { get; }
        public abstract List<DeviceInfo> Discover(List<HidDeviceDescriptor> catalog);

        public virtual BatteryReading ReadBattery(DeviceInfo device)
        {
            BatteryReading r = new BatteryReading();
            r.Error = "该设备不支持电量读取";
            return r;
        }

        public virtual DpiReading ReadDpi(DeviceInfo device)
        {
            DpiReading r = new DpiReading();
            r.Error = "该设备不支持 DPI 读取";
            return r;
        }

        public virtual bool WriteDpi(DeviceInfo device, int x, int y, int stage)
        {
            return false;
        }
    }
}
