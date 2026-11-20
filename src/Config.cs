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

    /// <summary>一次读到的板载 DPI。板载值是硬件里存的档位，读到一次就一直算数。</summary>
    public sealed class DpiSnapshot
    {
        public int X;
        public int Y;
        public int Stage;

        public bool IsValid { get { return X > 0; } }

        public string Text
        {
            get { return X + " × " + (Y > 0 ? Y : X) + (Stage > 0 ? "（档 " + Stage + "）" : ""); }
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
        // 每设备低电量阈值覆盖：没有覆盖项时用 LowThreshold。
        public readonly Dictionary<string, int> LowThresholdOverrides =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // 充满提醒 / 设备断开提醒。低电量提醒始终开启，阈值可为 0 关闭。
        public bool ChargeFullAlert = true;
        public bool DisconnectAlert = true;
        public List<MouseProfile> Profiles = new List<MouseProfile>();

        /// <summary>
        /// 每台设备最后一次读到的板载 DPI。板载数值不受休眠影响：
        /// 启动 / 自启动、手动读取、手动写入和每个刷新间隔的自动重读会更新它，
        /// 休眠、读写失败都不会清掉。
        /// </summary>
        public readonly Dictionary<string, DpiSnapshot> LastDpi =
            new Dictionary<string, DpiSnapshot>(StringComparer.OrdinalIgnoreCase);

        public DpiSnapshot CachedDpi(string deviceId)
        {
            DpiSnapshot value;
            if (!string.IsNullOrEmpty(deviceId) && LastDpi.TryGetValue(deviceId, out value) && value.IsValid)
                return value;
            return null;
        }

        public void SetCachedDpi(string deviceId, int x, int y, int stage)
        {
            if (string.IsNullOrEmpty(deviceId) || x <= 0) return;
            DpiSnapshot s = new DpiSnapshot();
            s.X = x;
            s.Y = y > 0 ? y : x;
            s.Stage = Math.Max(0, stage);
            LastDpi[deviceId] = s;
        }

        /// <summary>最近一次 SetAutoStart 的失败原因，成功为 null。</summary>
        public static string LastAutoStartError { get; private set; }

        public int EffectiveLowThreshold(string deviceId)
        {
            int value;
            if (!string.IsNullOrEmpty(deviceId) && LowThresholdOverrides.TryGetValue(deviceId, out value))
                return value;
            return LowThreshold;
        }

        /// <summary>设为全局值等同于移除覆盖，避免配置里堆积无意义的重复项。</summary>
        public void SetLowThresholdOverride(string deviceId, int value)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            value = Math.Min(100, Math.Max(0, value));
            if (value == LowThreshold) LowThresholdOverrides.Remove(deviceId);
            else LowThresholdOverrides[deviceId] = value;
        }

        /// <summary>全局阈值变化时清掉与新值相同的覆盖项。</summary>
        public void PruneLowThresholdOverrides()
        {
            List<string> stale = new List<string>();
            foreach (KeyValuePair<string, int> kv in LowThresholdOverrides)
                if (kv.Value == LowThreshold) stale.Add(kv.Key);
            foreach (string key in stale) LowThresholdOverrides.Remove(key);
        }

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

        public static string ReadErrorLogPath
        {
            get { return Path.Combine(ConfigDir, "read-errors.log"); }
        }

        private static string SettingsPath
        {
            get { return Path.Combine(ConfigDir, "settings.ini"); }
        }

        private static string ProfilesPath
        {
            get { return Path.Combine(ConfigDir, "profiles.ini"); }
        }

        /// <summary>把任意秒数归到界面上可选的三档（15 / 30 / 90），取最近的一档。</summary>
        private static int NearestRefreshSeconds(int value)
        {
            int best = 15;
            int gap = Math.Abs(value - 15);
            if (Math.Abs(value - 30) < gap) { best = 30; gap = Math.Abs(value - 30); }
            if (Math.Abs(value - 90) < gap) best = 90;
            return best;
        }

        public static AppConfig Load()
        {
            AppConfig c = new AppConfig();
            Dictionary<string, string> kv = ParseIni(SettingsPath);
            int v;
            if (int.TryParse(Get(kv, "DisplayMode"), out v)) c.DisplayMode = v == 1 ? 1 : 0;
            // 刷新间隔在界面上只有 15 / 30 / 90 三档：手工改成别的值（如 7 秒）时按最近一档归位，
            // 否则实际按 7 秒跑、界面却勾着 90 秒，两处显示对不上。
            if (int.TryParse(Get(kv, "RefreshSeconds"), out v) && v >= 5) c.RefreshSeconds = NearestRefreshSeconds(v);
            if (int.TryParse(Get(kv, "LowThreshold"), out v) && v >= 0 && v <= 100) c.LowThreshold = v;
            if (int.TryParse(Get(kv, "SleepTimeoutMinutes"), out v) && v >= SleepTimeoutMin && v <= SleepTimeoutMax)
                c.SleepTimeoutMinutes = v;
            bool flag;
            if (bool.TryParse(Get(kv, "ChargeFullAlert"), out flag)) c.ChargeFullAlert = flag;
            if (bool.TryParse(Get(kv, "DisconnectAlert"), out flag)) c.DisconnectAlert = flag;
            c.SelectedDeviceId = Get(kv, "SelectedDeviceId");
            c.ActiveProfile = Get(kv, "ActiveProfile");
            c.TrayVisibleIds = Get(kv, "TrayVisibleIds");
            c.TrayOrder = Get(kv, "TrayOrder");
            c.LoadThresholdOverrides(Get(kv, "LowThresholds"));
            c.LoadLastDpi(Get(kv, "LastDpi"));

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

        private void LoadThresholdOverrides(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return;
            foreach (string part in encoded.Split(';'))
            {
                if (string.IsNullOrEmpty(part)) continue;
                int colon = part.LastIndexOf(':');
                if (colon <= 0 || colon >= part.Length - 1) continue;
                int value;
                if (!int.TryParse(part.Substring(colon + 1), out value)) continue;
                if (value < 0 || value > 100) continue;
                LowThresholdOverrides[part.Substring(0, colon).Trim()] = value;
            }
        }

        private string EncodeThresholdOverrides()
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in LowThresholdOverrides)
                parts.Add(kv.Key + ":" + kv.Value);
            return string.Join(";", parts.ToArray());
        }

        /// <summary>格式：设备ID|x|y|档位，多条用分号隔开。设备 ID 里没有竖线。</summary>
        private void LoadLastDpi(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return;
            foreach (string part in encoded.Split(';'))
            {
                if (string.IsNullOrEmpty(part)) continue;
                string[] fields = part.Split('|');
                if (fields.Length < 4) continue;
                string id = fields[0].Trim();
                if (id.Length == 0) continue;
                int x, y, stage;
                if (!int.TryParse(fields[1], out x) || x <= 0) continue;
                if (!int.TryParse(fields[2], out y) || y <= 0) y = x;
                if (!int.TryParse(fields[3], out stage) || stage < 0) stage = 0;
                DpiSnapshot s = new DpiSnapshot();
                s.X = x;
                s.Y = y;
                s.Stage = stage;
                LastDpi[id] = s;
            }
        }

        private string EncodeLastDpi()
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, DpiSnapshot> kv in LastDpi)
                parts.Add(kv.Key + "|" + kv.Value.X + "|" + kv.Value.Y + "|" + kv.Value.Stage);
            return string.Join(";", parts.ToArray());
        }

        public void Save()
        {
            Dictionary<string, string> kv = new Dictionary<string, string>();
            kv["DisplayMode"] = DisplayMode.ToString();
            kv["RefreshSeconds"] = RefreshSeconds.ToString();
            kv["LowThreshold"] = LowThreshold.ToString();
            kv["SleepTimeoutMinutes"] = SleepTimeoutMinutes.ToString();
            kv["ChargeFullAlert"] = ChargeFullAlert.ToString();
            kv["DisconnectAlert"] = DisconnectAlert.ToString();
            kv["LowThresholds"] = EncodeThresholdOverrides();
            kv["LastDpi"] = EncodeLastDpi();
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
            WriteFileAtomic(ProfilesPath, sb.ToString(), new UTF8Encoding(false));
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

        /// <summary>写启动项并回读校验；失败时把原因写入 LastAutoStartError，不再静默。</summary>
        public static bool SetAutoStart(bool enabled)
        {
            LastAutoStartError = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (key == null)
                    {
                        LastAutoStartError = "无法打开启动项注册表键";
                        return false;
                    }
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
                if (IsAutoStartEnabled() != enabled)
                {
                    LastAutoStartError = "写入后校验未通过，可能被安全软件拦截";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                LastAutoStartError = ex.Message;
                return false;
            }
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
            WriteFileAtomic(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// 先写同目录临时文件再替换，避免进程退出 / 断电时留下半截 ini。
        /// 路径都在 %AppData% 下，同卷替换可用 File.Replace；首次写入用 Move。
        /// </summary>
        private static void WriteFileAtomic(string path, string content, Encoding encoding)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, content, encoding);
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
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
