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
        Profiles
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
        public void ApplyAppearance(int displayMode, int refreshSeconds, int lowThreshold, int sleepMinutes)
        {
            displayMode = displayMode == 1 ? 1 : 0;
            refreshSeconds = refreshSeconds == 30 ? 30 : (refreshSeconds == 90 ? 90 : 15);
            lowThreshold = Math.Min(50, Math.Max(0, lowThreshold));
            sleepMinutes = Math.Min(AppConfig.SleepTimeoutMax, Math.Max(AppConfig.SleepTimeoutMin, sleepMinutes));

            if (_config.DisplayMode == displayMode && _config.RefreshSeconds == refreshSeconds &&
                _config.LowThreshold == lowThreshold && _config.SleepTimeoutMinutes == sleepMinutes)
                return;

            _config.DisplayMode = displayMode;
            _config.RefreshSeconds = refreshSeconds;
            _config.LowThreshold = lowThreshold;
            _config.SleepTimeoutMinutes = sleepMinutes;
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
