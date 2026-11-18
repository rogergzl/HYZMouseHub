using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace HyzMouseHub
{
    public sealed class MouseProfile
    {
        public string Name;
        public int DpiX;
        public int DpiY;
        public int Stage;
        public string DeviceId;

        public MouseProfile()
        {
            Name = "新配置";
            DpiX = 1600;
            DpiY = 1600;
            Stage = 1;
            DeviceId = "";
        }

        public MouseProfile Clone()
        {
            MouseProfile p = new MouseProfile();
            p.Name = Name;
            p.DpiX = DpiX;
            p.DpiY = DpiY;
            p.Stage = Stage;
            p.DeviceId = DeviceId;
            return p;
        }

        public override string ToString()
        {
            return Name + "  " + DpiX + "×" + DpiY + "  (档 " + Stage + ")";
        }
    }

    public sealed class AppConfig
    {
        public const int SleepTimeoutMin = 1;
        public const int SleepTimeoutMax = 240;
        public const int SleepTimeoutDefault = 5;

        public int DisplayMode = 0; // 0 = 数字, 1 = 鼠标图标
        public int RefreshSeconds = 15;
        public int LowThreshold = 15;
        // 设备连续无应答多久之后才判定为休眠（分钟）。期间沿用上一次读到的电量。
        public int SleepTimeoutMinutes = SleepTimeoutDefault;
        public string SelectedDeviceId = "";
        public string ActiveProfile = "";
        // 托盘显示哪些设备：null/"" 表示全部显示；否则为分号分隔的 DeviceInfo.Id 列表。
        public string TrayVisibleIds = "";
        // 托盘图标显示顺序（分号分隔的 DeviceInfo.Id）；空串=按设备发现顺序。
        public string TrayOrder = "";
        public List<MouseProfile> Profiles = new List<MouseProfile>();

        public bool IsTrayVisible(string deviceId)
        {
            if (string.IsNullOrEmpty(TrayVisibleIds)) return true;
            foreach (string part in TrayVisibleIds.Split(';'))
            {
                if (string.Equals(part.Trim(), deviceId, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>返回托盘图标顺序索引，未记录时为 int.MaxValue（排在末尾）。</summary>
        public int TrayOrderIndex(string deviceId)
        {
            if (string.IsNullOrEmpty(TrayOrder)) return int.MaxValue;
            string[] parts = TrayOrder.Split(';');
            for (int i = 0; i < parts.Length; i++)
                if (string.Equals(parts[i].Trim(), deviceId, StringComparison.OrdinalIgnoreCase)) return i;
            return int.MaxValue;
        }

        /// <summary>把某设备移动到给定顺序位置，其余按顺序顺延。</summary>
        public void SetTrayOrder(IEnumerable<string> orderedIds)
        {
            List<string> ids = new List<string>();
            foreach (string id in orderedIds)
                if (!string.IsNullOrEmpty(id) && !ids.Contains(id)) ids.Add(id);
            TrayOrder = ids.Count == 0 ? "" : string.Join(";", ids.ToArray());
        }

        public static string ConfigDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HYZMouseHub");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string SettingsPath
        {
            get { return Path.Combine(ConfigDir, "settings.ini"); }
        }

        private static string ProfilesPath
        {
            get { return Path.Combine(ConfigDir, "profiles.ini"); }
        }

        public static AppConfig Load()
        {
            AppConfig c = new AppConfig();
            Dictionary<string, string> kv = ParseIni(SettingsPath);
            int v;
            if (int.TryParse(Get(kv, "DisplayMode"), out v)) c.DisplayMode = v == 1 ? 1 : 0;
            if (int.TryParse(Get(kv, "RefreshSeconds"), out v) && v >= 5) c.RefreshSeconds = v;
            if (int.TryParse(Get(kv, "LowThreshold"), out v) && v >= 0 && v <= 100) c.LowThreshold = v;
            if (int.TryParse(Get(kv, "SleepTimeoutMinutes"), out v) && v >= SleepTimeoutMin && v <= SleepTimeoutMax)
                c.SleepTimeoutMinutes = v;
            c.SelectedDeviceId = Get(kv, "SelectedDeviceId");
            c.ActiveProfile = Get(kv, "ActiveProfile");
            c.TrayVisibleIds = Get(kv, "TrayVisibleIds");
            c.TrayOrder = Get(kv, "TrayOrder");

            if (!File.Exists(ProfilesPath))
            {
                // 不再写入“桌面 1600 / 游戏 3200”这类占位配置：
                // 配置档位只显示用户主动保存的项，当前板载 DPI 由设备实时读取。
                c.Save();
                return c;
            }

            foreach (string line in File.ReadAllLines(ProfilesPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                string[] parts = line.Split('\t');
                if (parts.Length < 5) continue;
                MouseProfile p = new MouseProfile();
                p.Name = Unescape(parts[0]);
                int.TryParse(parts[1], out p.DpiX);
                int.TryParse(parts[2], out p.DpiY);
                int.TryParse(parts[3], out p.Stage);
                p.DeviceId = parts.Length > 4 ? Unescape(parts[4]) : "";
                if (p.DpiX <= 0) p.DpiX = 1600;
                if (p.DpiY <= 0) p.DpiY = p.DpiX;
                if (p.Stage < 1) p.Stage = 1;
                c.Profiles.Add(p);
            }

            // 一次性清理旧版本自动写入的占位配置（用户从未编辑，DeviceId 为空）。
            int removed = c.Profiles.RemoveAll(delegate(MouseProfile p)
            {
                if (!string.IsNullOrEmpty(p.DeviceId)) return false;
                if (p.Name == "桌面 1600" && p.DpiX == 1600 && p.DpiY == 1600 && p.Stage == 1) return true;
                if (p.Name == "游戏 3200" && p.DpiX == 3200 && p.DpiY == 3200 && p.Stage == 2) return true;
                return false;
            });
            if (removed > 0) c.Save();
            return c;
        }

        public void Save()
        {
            Dictionary<string, string> kv = new Dictionary<string, string>();
            kv["DisplayMode"] = DisplayMode.ToString();
            kv["RefreshSeconds"] = RefreshSeconds.ToString();
            kv["LowThreshold"] = LowThreshold.ToString();
            kv["SleepTimeoutMinutes"] = SleepTimeoutMinutes.ToString();
            kv["SelectedDeviceId"] = SelectedDeviceId;
            kv["ActiveProfile"] = ActiveProfile;
            kv["TrayVisibleIds"] = TrayVisibleIds;
            kv["TrayOrder"] = TrayOrder;
            WriteIni(SettingsPath, kv);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# name\tdpiX\tdpiY\tstage\tdeviceId");
            foreach (MouseProfile p in Profiles)
            {
                sb.Append(Escape(p.Name)).Append('\t')
                  .Append(p.DpiX).Append('\t')
                  .Append(p.DpiY).Append('\t')
                  .Append(p.Stage).Append('\t')
                  .Append(Escape(p.DeviceId)).AppendLine();
            }
            File.WriteAllText(ProfilesPath, sb.ToString(), new UTF8Encoding(false));
        }

        public MouseProfile FindProfile(string name)
        {
            foreach (MouseProfile p in Profiles)
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    return key != null && key.GetValue("HYZMouseHub") != null;
                }
            }
            catch { return false; }
        }

        public static void SetAutoStart(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (key == null) return;
                    if (enabled)
                    {
                        string exe = Path.ChangeExtension(ApplicationExecutablePath(), ".exe");
                        key.SetValue("HYZMouseHub", "\"" + exe + "\" --autostart", RegistryValueKind.String);
                    }
                    else
                    {
                        key.DeleteValue("HYZMouseHub", false);
                    }
                }
            }
            catch { }
        }

        private static string ApplicationExecutablePath()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().Location;
        }

        private static Dictionary<string, string> ParseIni(string path)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return result;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                result[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return result;
        }

        private static void WriteIni(string path, Dictionary<string, string> kv)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in kv)
                sb.Append(pair.Key).Append('=').Append(pair.Value).AppendLine();
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string Get(Dictionary<string, string> kv, string key)
        {
            string value;
            return kv.TryGetValue(key, out value) ? value : "";
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "").Replace("\n", "");
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\t", "\t").Replace("\\\\", "\\");
        }
    }
}
