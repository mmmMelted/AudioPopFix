using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using NAudio.CoreAudioApi;

namespace AudioPopFixTray
{
    public class DevicePickerForm : Form
    {
        private CheckedListBox _list;
        private Button _ok;
        private Button _cancel;

        public IEnumerable<string> SelectedDeviceIds =>
            _list.CheckedItems.Cast<DeviceItem>().Select(d => d.Id);

        public DevicePickerForm(IList<MMDevice> devices, IEnumerable<string> precheckedIds)
        {
            Text = "Select Audio Devices to Keep Awake";
            Width = 520;
            Height = 420;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            _list = new CheckedListBox
            {
                Dock = DockStyle.Top,
                Height = 320
            };

            foreach (var d in devices)
            {
                var item = new DeviceItem(d);
                int idx = _list.Items.Add(item);
                if (precheckedIds.Contains(d.ID))
                    _list.SetItemChecked(idx, true);
            }

            _ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 320, Top = 330, Width = 80 };
            _cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 410, Top = 330, Width = 80 };

            Controls.Add(_list);
            Controls.Add(_ok);
            Controls.Add(_cancel);
        }

        private class DeviceItem
        {
            public string Id { get; }
            public string Name { get; }

            public DeviceItem(MMDevice device)
            {
                Id = device.ID;
                Name = $"{device.FriendlyName}  [{device.DeviceFriendlyName}]";
            }

            public override string ToString() => Name;
        }
    }
}
