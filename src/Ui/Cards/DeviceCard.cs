using System;
using System.Drawing;
using System.Windows.Forms;

namespace HyzMouseHub
{
    /// <summary>设备状态：切换设备 + 三行键值摘要。</summary>
    internal sealed class DeviceCard : Card
    {
        private const int ValueRow = 26;

        public readonly ComboBox DeviceList = new ComboBox();
        public readonly Label Model = ValueLabel();
        public readonly Label Battery = ValueLabel();
        public readonly Label Sensor = ValueLabel();

        private readonly Label _modelKey = KeyLabel("型号");
        private readonly Label _batteryKey = KeyLabel("电量");
        private readonly Label _sensorKey = KeyLabel("传感器");

        public DeviceCard() : base("设备状态", Theme.FieldHeight + Theme.RowGap + ValueRow * 3)
        {
            Theme.StyleCombo(DeviceList);
            Content.Controls.Add(DeviceList);

            Content.Controls.Add(_modelKey);
            Content.Controls.Add(Model);
            Content.Controls.Add(_batteryKey);
            Content.Controls.Add(Battery);
            Content.Controls.Add(_sensorKey);
            Content.Controls.Add(Sensor);
        }

        protected override void LayoutContent()
        {
            int w = Content.Width;
            int fieldHeight = Math.Min(Theme.FieldHeight, DeviceList.Height);
            DeviceList.SetBounds(0, Math.Max(0, (Theme.FieldHeight - fieldHeight) / 2), w, fieldHeight);

            int keyWidth = 58;
            int y = Theme.FieldHeight + Theme.RowGap;
            Label[] keys = { _modelKey, _batteryKey, _sensorKey };
            Label[] values = { Model, Battery, Sensor };
            for (int i = 0; i < keys.Length; i++)
            {
                int top = y + i * ValueRow;
                keys[i].SetBounds(0, top, keyWidth, ValueRow);
                values[i].SetBounds(keyWidth, top, Math.Max(40, w - keyWidth), ValueRow);
            }
        }
    }
}
