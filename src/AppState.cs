using System;
using System.Collections.Generic;

namespace HyzMouseHub
{
    /// <summary>状态变更所属的区域，界面据此决定要不要重画。</summary>
    internal enum StateArea
    {
        Appearance,
        TrayDevices,
        Selection,
        Profiles,
        Dpi
    }

    internal sealed class StateChangedEventArgs : EventArgs
    {
        public readonly StateArea Area;
        public StateChangedEventArgs(StateArea area) { Area = area; }
    }

    /// <summary>
    /// 设置状态中间层。全局权威状态只此一份：设置窗口和托盘菜单都只通过本类读写，
    /// 任一处改动先写入本类、立即落盘，再广播 Changed，另一处据此同步自己的控件。
    /// 这样不存在“两个界面各持一份显示方式”的副本，也就不会互相覆盖。
    /// </summary>
    internal sealed class AppState
    {
        private readonly AppConfig _config;
        private readonly StartupBurst _startupBurst = new StartupBurst();

        public AppState(AppConfig config)
        {
            _config = config ?? new AppConfig();
        }

        public event EventHandler<StateChangedEventArgs> Changed;

        /// <summary>持久化原始数据：仅供 AppConfig 自身与诊断命令使用，界面不要直接读写。</summary>
        public AppConfig Persistence { get { return _config; } }

        private void Raise(StateArea area)
        {
            EventHandler<StateChangedEventArgs> handler = Changed;
            if (handler != null) handler(this, new StateChangedEventArgs(area));
        }

        // ---------------- 外观与运行 ----------------

        public int DisplayMode { get { return _config.DisplayMode; } }
        public int RefreshSeconds { get { return _config.RefreshSeconds; } }
        public int LowThreshold { get { return _config.LowThreshold; } }
        public int LowThresholdFor(string deviceId) { return _config.EffectiveLowThreshold(deviceId); }
        public bool HasLowThresholdOverride(string deviceId)
        {
            return !string.IsNullOrEmpty(deviceId) && _config.LowThresholdOverrides.ContainsKey(deviceId);
        }
        public bool ChargeFullAlert { get { return _config.ChargeFullAlert; } }
        public bool DisconnectAlert { get { return _config.DisconnectAlert; } }
        /// <summary>设备无应答后仍保留上次读数的时长（分钟），超过才显示休眠。</summary>
        public int SleepTimeoutMinutes { get { return _config.SleepTimeoutMinutes; } }

        public void SetDisplayMode(int mode)
        {
            mode = mode == 1 ? 1 : 0;
            if (_config.DisplayMode == mode) return;
            _config.DisplayMode = mode;
            _config.Save();
            Raise(StateArea.Appearance);
        }

        public void SetRefreshSeconds(int seconds)
        {
            seconds = Math.Max(5, seconds);
            if (_config.RefreshSeconds == seconds) return;
            _config.RefreshSeconds = seconds;
            _config.Save();
            Raise(StateArea.Appearance);
        }

        /// <summary>一次写入整块外观设置：只落盘一次、只广播一次，避免中间态被另一处读到。</summary>
        public void ApplyAppearance(int displayMode, int refreshSeconds, int lowThreshold, int sleepMinutes,
                                    bool chargeFullAlert, bool disconnectAlert)
        {
            displayMode = displayMode == 1 ? 1 : 0;
            refreshSeconds = refreshSeconds == 30 ? 30 : (refreshSeconds == 90 ? 90 : 15);
            lowThreshold = Math.Min(50, Math.Max(0, lowThreshold));
            sleepMinutes = Math.Min(AppConfig.SleepTimeoutMax, Math.Max(AppConfig.SleepTimeoutMin, sleepMinutes));

            if (_config.DisplayMode == displayMode && _config.RefreshSeconds == refreshSeconds &&
                _config.LowThreshold == lowThreshold && _config.SleepTimeoutMinutes == sleepMinutes &&
                _config.ChargeFullAlert == chargeFullAlert && _config.DisconnectAlert == disconnectAlert)
                return;

            _config.DisplayMode = displayMode;
            _config.RefreshSeconds = refreshSeconds;
            _config.LowThreshold = lowThreshold;
            _config.SleepTimeoutMinutes = sleepMinutes;
            _config.ChargeFullAlert = chargeFullAlert;
            _config.DisconnectAlert = disconnectAlert;
            _config.PruneLowThresholdOverrides();
            _config.Save();
            Raise(StateArea.Appearance);
        }

        /// <summary>单设备阈值覆盖；设为全局值即移除覆盖。立即落盘并广播。</summary>
        public void SetLowThresholdOverride(string deviceId, int value)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            _config.SetLowThresholdOverride(deviceId, value);
            _config.Save();
            Raise(StateArea.Appearance);
        }

        // ---------------- 启动快速扫描 ----------------

        /// <summary>开机自启后 1 分钟内固定 5 秒扫描一次；读到活动设备或到期即恢复设置间隔。</summary>
        public bool StartupScanActive { get { return _startupBurst.IsActive(); } }

        /// <summary>当前生效的刷新间隔：快速扫描期间固定 5 秒，其余时间用设置值。</summary>
        public int EffectiveRefreshSeconds
        {
            get { return _startupBurst.IsActive() ? StartupBurst.IntervalSeconds : _config.RefreshSeconds; }
        }

        /// <summary>快速扫描窗口剩余秒数，仅供诊断输出。</summary>
        public int StartupScanRemainingSeconds { get { return _startupBurst.RemainingSeconds(); } }

        /// <summary>仅由开机自启路径调用，标记快速扫描窗口开始。</summary>
        public void BeginStartupScan()
        {
            _startupBurst.Begin();
        }

        /// <summary>每轮扫描结束后回传结果，由窗口自行判断是否提前结束。</summary>
        public void ReportScan(IList<DeviceSnapshot> snapshots)
        {
            _startupBurst.Observe(snapshots);
        }

        // ---------------- 板载 DPI ----------------

        // 启动后 1 分钟的 5 秒探查窗口，整次运行只开一次：窗口内不受设置间隔限制，
        // 每 5 秒试一次；某台设备读到值、或窗口到期，它就不再按 5 秒走。
        private DateTime _dpiBurstEnd;
        // 界面「读取中…」提示的截止时间，可以随打开设置窗口顺延，和探查节奏无关。
        private DateTime _dpiHintEnd;
        // 每台设备最后一次尝试读取的时间，用于按节奏节流。
        private readonly Dictionary<string, DateTime> _dpiLastAttempt =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        // 本次运行已经读到过值：补读窗口内只对还没读到的设备用 5 秒节奏，读到过就走刷新间隔。
        private readonly HashSet<string> _dpiResolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 最后一次读到的板载 DPI，跨重启保留。板载是硬件里存的档位，读到一次就一直算数：
        /// 休眠、刷新、读写失败都不会把它清掉，界面上永远显示这一份。
        /// </summary>
        public DpiSnapshot CachedDpi(string deviceId)
        {
            return _config.CachedDpi(deviceId);
        }

        /// <summary>只有真的读到才更新；值没变就不重复落盘。</summary>
        public void RememberDpi(string deviceId, int x, int y, int stage)
        {
            if (string.IsNullOrEmpty(deviceId) || x <= 0) return;
            int ny = y > 0 ? y : x;
            int ns = Math.Max(0, stage);
            _dpiResolved.Add(deviceId);
            DpiSnapshot old = _config.CachedDpi(deviceId);
            if (old != null && old.X == x && old.Y == ny && old.Stage == ns) return;
            _config.SetCachedDpi(deviceId, x, ny, ns);
            _config.Save();
            Raise(StateArea.Dpi);
        }

        /// <summary>
        /// 开启动探查与「读取中…」提示：程序启动、自启动、以及每次打开设置窗口时调用。
        /// 5 秒探查窗口整次运行只开一次（从第一次调用算 1 分钟），之后打开窗口只顺延提示文字。
        /// </summary>
        public void BeginDpiWindow()
        {
            if (_dpiBurstEnd == DateTime.MinValue)
                _dpiBurstEnd = DateTime.Now.AddSeconds(StartupBurst.WindowSeconds);
            DateTime hint = DateTime.Now.AddSeconds(StartupBurst.WindowSeconds);
            if (hint > _dpiHintEnd) _dpiHintEnd = hint;
        }

        /// <summary>「读取中…」提示是否还开着（还没有存下值时，界面据此决定显示提示还是 --）。</summary>
        public bool DpiWindowActive { get { return DateTime.Now <= _dpiHintEnd; } }

        /// <summary>启动后 1 分钟的 5 秒探查窗口是否还在。</summary>
        public bool DpiBurstActive { get { return DateTime.Now <= _dpiBurstEnd; } }

        /// <summary>
        /// 这台设备两次自动重读之间该等多久：启动后 1 分钟内、且本次运行还没读到它，固定 5 秒；
        /// 其余情况一律跟设置里的刷新间隔走。
        /// </summary>
        public int DpiRepeatSecondsFor(string deviceId)
        {
            if (!string.IsNullOrEmpty(deviceId) && !_dpiResolved.Contains(deviceId) && DpiBurstActive)
                return StartupBurst.IntervalSeconds;
            return Math.Max(StartupBurst.IntervalSeconds, _config.RefreshSeconds);
        }

        /// <summary>
        /// 这台设备现在该不该读板载 DPI：距上次尝试已经过了它该等的时长（见 DpiRepeatSecondsFor）。
        /// 手动读取走服务的直读接口，不看这里。读到过也照样按节奏重读——运行期间在别处换了档位，
        /// 下一个周期就能跟上；读不到（休眠）只是这一次没应答，已存的值不动，下一个周期再试。
        /// </summary>
        public bool DpiProbeDue(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;
            DateTime last;
            if (!_dpiLastAttempt.TryGetValue(deviceId, out last)) return true;
            return (DateTime.Now - last).TotalSeconds >= DpiRepeatSecondsFor(deviceId);
        }

        public void MarkDpiProbe(string deviceId)
        {
            if (!string.IsNullOrEmpty(deviceId)) _dpiLastAttempt[deviceId] = DateTime.Now;
        }

        // ---------------- 托盘显示 ----------------

        public bool IsTrayVisible(string deviceId)
        {
            return _config.IsTrayVisible(deviceId);
        }

        public void SetTrayVisibleIds(string ids)
        {
            ids = ids ?? "";
            if (string.Equals(_config.TrayVisibleIds, ids, StringComparison.Ordinal)) return;
            _config.TrayVisibleIds = ids;
            _config.Save();
            Raise(StateArea.TrayDevices);
        }

        public int TrayOrderIndex(string deviceId)
        {
            return _config.TrayOrderIndex(deviceId);
        }

        public void SetTrayOrder(IEnumerable<string> orderedIds)
        {
            _config.SetTrayOrder(orderedIds);
            _config.Save();
            Raise(StateArea.TrayDevices);
        }

        // ---------------- 设备选择与配置档位 ----------------

        public string SelectedDeviceId { get { return _config.SelectedDeviceId; } }

        public void SetSelectedDevice(string deviceId)
        {
            deviceId = deviceId ?? "";
            if (string.Equals(_config.SelectedDeviceId, deviceId, StringComparison.OrdinalIgnoreCase)) return;
            _config.SelectedDeviceId = deviceId;
            _config.Save();
            Raise(StateArea.Selection);
        }

        public string ActiveProfile { get { return _config.ActiveProfile; } }

        public void SetActiveProfile(string name)
        {
            _config.ActiveProfile = name ?? "";
            _config.Save();
            Raise(StateArea.Profiles);
        }

        public IList<MouseProfile> Profiles { get { return _config.Profiles; } }

        public MouseProfile FindProfile(string name)
        {
            return _config.FindProfile(name);
        }

        /// <summary>配置档位列表被修改后调用：落盘并通知另一处界面重载。</summary>
        public void ProfilesChanged(string activeProfile)
        {
            if (activeProfile != null) _config.ActiveProfile = activeProfile;
            _config.Save();
            Raise(StateArea.Profiles);
        }
    }
}
