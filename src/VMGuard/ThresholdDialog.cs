using System;
using System.Drawing;
using System.Windows.Forms;

namespace VMGuard
{
    /// <summary>Small dialog to pick a custom battery threshold.</summary>
    internal sealed class ThresholdDialog : Form
    {
        private readonly NumericUpDown _value;

        public ThresholdDialog(int current)
        {
            Text = "VMGuard - Battery threshold";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(320, 120);

            var label = new Label
            {
                Text = "Suspend VMs when the battery is at or below (%):",
                AutoSize = true,
                Location = new Point(12, 15),
            };
            _value = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 100,
                Value = Math.Max(1, Math.Min(100, current)),
                Location = new Point(15, 42),
                Width = 80,
            };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(146, 80), Width = 75 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(230, 80), Width = 75 };

            Controls.AddRange(new Control[] { label, _value, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public int Value => (int)_value.Value;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            _value.Select(0, _value.Text.Length);
            _value.Focus();
        }
    }
}
