using System;
using System.Collections.Generic;

namespace HyzMouseHub
{
    /// <summary>
    /// 开机自启后的启动快速扫描窗口：前 60 秒内固定每 5 秒读一次设备。
    /// 结束条件二选一——读到活动设备（在线且是实时读数）立即结束，或窗口到期。
    /// 本类只持有窗口状态，不读设备、不碰界面；生效间隔由中间层对外提供。
    /// </summary>
    internal sealed class StartupBurst
    {
        /// <summary>窗口内固定的扫描间隔。</summary>
        public const int IntervalSeconds = 5;

        /// <summary>窗口总时长，到期后回到设置里的刷新间隔。</summary>
        public const int WindowSeconds = 60;

        private DateTime _deadline;
        private bool _active;

        public void Begin()
        {
            _deadline = DateTime.Now.AddSeconds(WindowSeconds);
            _active = true;
        }

        /// <summary>窗口是否仍生效。到期当次即返回 false，不依赖外部计时器来收尾。</summary>
        public bool IsActive()
        {
            if (!_active) return false;
            if (DateTime.Now < _deadline) return true;
            _active = false;
            return false;
        }

        /// <summary>窗口内剩余秒数，仅用于诊断输出。</summary>
        public int RemainingSeconds()
        {
            if (!IsActive()) return 0;
            int left = (int)Math.Ceiling((_deadline - DateTime.Now).TotalSeconds);
            return Math.Max(0, left);
        }

        /// <summary>窗口到期或已达成目的时手动收尾（例如板载 DPI 已读到）。</summary>
        public void Stop()
        {
            _active = false;
        }

        /// <summary>用本轮扫描结果判定：只要有一台设备读到实时电量，就结束快速扫描。</summary>
        public void Observe(IList<DeviceSnapshot> snapshots)
        {
            if (!IsActive() || snapshots == null) return;
            foreach (DeviceSnapshot s in snapshots)
            {
                if (IsLiveReading(s))
                {
                    _active = false;
                    return;
                }
            }
        }

        /// <summary>活动设备：本次真的读到电量（在线、非缓存）。缓存读数不算活动。</summary>
        public static bool IsLiveReading(DeviceSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Battery == null) return false;
            return snapshot.Battery.Online && !snapshot.Battery.Cached && snapshot.Battery.Percent >= 0;
        }
    }
}
