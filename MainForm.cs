using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Aj179PStat
{
    public class MainForm : Form
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private readonly HidDeviceManager _hidManager;
        private readonly System.Threading.Timer _pollingTimer;
        private readonly NotifyIcon _notifyIcon;

        private Label _lblBatteryPercent = null!;
        private Label _lblStatusState = null!;
        private Label _lblDeviceInfo = null!;
        private TextBox _txtLog = null!;
        private Button _btnRefresh = null!;
        private Button _btnMinimizeTray = null!;
        private CheckBox _chkMinimizeToTrayOnClose = null!;
        private CheckBox _chkStartWithWindows = null!;
        private CheckBox _chkEnableNotification = null!;
        private NumericUpDown _numNotificationThreshold = null!;
        private ComboBox _cmbInterval = null!;

        private IntPtr _currentHicon = IntPtr.Zero;
        private int _pollingIntervalMinutes = 5;
        private bool _hasNotifiedLowBattery = false;
        private bool _allowShowForm = false; // Prevents form from showing on app launch

        private const string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string RegistrySettingsKey = @"SOFTWARE\Aj179PStat";
        private const string AppName = "Aj179PStat";

        public MainForm()
        {
            _hidManager = new HidDeviceManager();

            Text = "Ajazz AJ179 Pro Battery Monitor";
            Size = new Size(520, 520);
            MinimumSize = new Size(450, 480);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            InitializeComponents();
            LoadUserSettings();

            _notifyIcon = new NotifyIcon
            {
                Visible = true,
                Text = "Ajazz AJ179 Pro - Initializing...",
                ContextMenuStrip = CreateContextMenu()
            };

            // Only restore/toggle window on LEFT mouse click so RIGHT mouse click opens context menu natively
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ToggleWindowVisibility();
                }
            };

            // Initial fetch
            UpdateBatteryStatus();

            // Timer
            _pollingTimer = new System.Threading.Timer(_ => 
            {
                if (InvokeRequired)
                    BeginInvoke(new Action(UpdateBatteryStatus));
                else
                    UpdateBatteryStatus();
            }, null, TimeSpan.FromMinutes(_pollingIntervalMinutes), TimeSpan.FromMinutes(_pollingIntervalMinutes));
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!_allowShowForm)
            {
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        private void InitializeComponents()
        {
            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 110,
                BackColor = Color.FromArgb(30, 30, 35)
            };

            _lblBatteryPercent = new Label
            {
                Text = "--%",
                Font = new Font("Segoe UI", 36f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(20, 10),
                AutoSize = true
            };

            _lblStatusState = new Label
            {
                Text = "Checking device...",
                Font = new Font("Segoe UI", 12f, FontStyle.Regular),
                ForeColor = Color.LightGray,
                Location = new Point(25, 72),
                AutoSize = true
            };

            pnlTop.Controls.Add(_lblBatteryPercent);
            pnlTop.Controls.Add(_lblStatusState);

            var pnlMiddle = new Panel
            {
                Dock = DockStyle.Top,
                Height = 230,
                Padding = new Padding(15)
            };

            _lblDeviceInfo = new Label
            {
                Text = "Target: Ajazz AJ179 Pro (VID: 0x3151, PID: 0x402D)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(15, 10),
                AutoSize = true
            };

            _txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9f),
                Location = new Point(15, 35),
                Size = new Size(475, 95)
            };

            _chkEnableNotification = new CheckBox
            {
                Text = "Notify when battery is below:",
                Checked = true,
                Location = new Point(15, 140),
                AutoSize = true
            };
            _chkEnableNotification.CheckedChanged += (s, e) => SaveUserSettings();

            _numNotificationThreshold = new NumericUpDown
            {
                Minimum = 5,
                Maximum = 90,
                Value = 20,
                Size = new Size(55, 23),
                Location = new Point(205, 137)
            };
            _numNotificationThreshold.ValueChanged += (s, e) => SaveUserSettings();

            var lblPercentSign = new Label
            {
                Text = "%",
                Location = new Point(265, 140),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };

            _chkMinimizeToTrayOnClose = new CheckBox
            {
                Text = "Minimize to tray on close (X)",
                Checked = true,
                Location = new Point(15, 175),
                AutoSize = true
            };

            _chkStartWithWindows = new CheckBox
            {
                Text = "Start with Windows",
                Checked = IsStartWithWindowsEnabled(),
                Location = new Point(240, 175),
                AutoSize = true
            };
            _chkStartWithWindows.CheckedChanged += (s, e) => ToggleStartWithWindows(_chkStartWithWindows.Checked);

            pnlMiddle.Controls.Add(_lblDeviceInfo);
            pnlMiddle.Controls.Add(_txtLog);
            pnlMiddle.Controls.Add(_chkEnableNotification);
            pnlMiddle.Controls.Add(_numNotificationThreshold);
            pnlMiddle.Controls.Add(lblPercentSign);
            pnlMiddle.Controls.Add(_chkMinimizeToTrayOnClose);
            pnlMiddle.Controls.Add(_chkStartWithWindows);

            var pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                Padding = new Padding(15)
            };

            _btnRefresh = new Button
            {
                Text = "Refresh Now",
                Size = new Size(120, 35),
                Location = new Point(15, 10)
            };
            _btnRefresh.Click += (s, e) => UpdateBatteryStatus();

            var lblInterval = new Label
            {
                Text = "Interval:",
                Location = new Point(150, 18),
                AutoSize = true
            };

            _cmbInterval = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Size = new Size(90, 25),
                Location = new Point(205, 15)
            };
            _cmbInterval.Items.AddRange(new object[] { "1 min", "5 min", "15 min", "30 min" });
            _cmbInterval.SelectedIndex = 1;
            _cmbInterval.SelectedIndexChanged += (s, e) =>
            {
                int min = _cmbInterval.SelectedIndex switch
                {
                    0 => 1,
                    1 => 5,
                    2 => 15,
                    3 => 30,
                    _ => 5
                };
                SetPollingInterval(min);
            };

            _btnMinimizeTray = new Button
            {
                Text = "Minimize to Tray",
                Size = new Size(130, 35),
                Location = new Point(360, 10)
            };
            _btnMinimizeTray.Click += (s, e) => MinimizeToTray();

            pnlBottom.Controls.Add(_btnRefresh);
            pnlBottom.Controls.Add(lblInterval);
            pnlBottom.Controls.Add(_cmbInterval);
            pnlBottom.Controls.Add(_btnMinimizeTray);

            Controls.Add(pnlMiddle);
            Controls.Add(pnlTop);
            Controls.Add(pnlBottom);
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();

            var header = new ToolStripMenuItem("Ajazz AJ179 Pro Monitor")
            {
                Enabled = false,
                Font = new Font(Control.DefaultFont, FontStyle.Bold)
            };
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(new ToolStripMenuItem("Open Dashboard", null, (s, e) => RestoreFromTray()));
            menu.Items.Add(new ToolStripMenuItem("Refresh Now", null, (s, e) => UpdateBatteryStatus()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication()));

            return menu;
        }

        public void UpdateBatteryStatus()
        {
            try
            {
                BatteryStatus status = _hidManager.ReadBatteryStatus();

                // Update Form UI
                if (!status.IsConnected)
                {
                    _lblBatteryPercent.Text = "N/A";
                    _lblBatteryPercent.ForeColor = Color.Gray;
                    _lblStatusState.Text = "Disconnected / Mouse not found";
                    _txtLog.Text = $"[{DateTime.Now:HH:mm:ss}] Mouse Disconnected.\r\nSearching for VID_3151 & PID_402D...";
                }
                else
                {
                    _lblBatteryPercent.Text = $"{status.BatteryPercent}%";
                    _lblBatteryPercent.ForeColor = status.BatteryPercent > 50 ? Color.LightGreen :
                                                   status.BatteryPercent > 20 ? Color.Gold : Color.OrangeRed;
                    _lblStatusState.Text = "Status: Mouse Connected";
                    
                    _txtLog.Text = $"[{DateTime.Now:HH:mm:ss}] Battery: {status.BatteryPercent}%\r\n" +
                                   $"Message: {status.StatusMessage}\r\n" +
                                   $"Raw Payload Hex: {status.RawDataHex}";

                    // Low battery notification check
                    CheckLowBatteryNotification(status.BatteryPercent);
                }

                // Update System Tray Icon
                using Bitmap bitmap = IconGenerator.CreateBatteryBitmap(status);
                IntPtr newHicon = bitmap.GetHicon();
                Icon newIcon = Icon.FromHandle(newHicon);

                _notifyIcon.Icon = newIcon;

                if (_currentHicon != IntPtr.Zero)
                {
                    DestroyIcon(_currentHicon);
                }
                _currentHicon = newHicon;

                string tooltip = status.IsConnected ? $"Ajazz AJ179 Pro: {status.BatteryPercent}%" : "Ajazz AJ179 Pro: Disconnected";
                _notifyIcon.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
            }
            catch (Exception ex)
            {
                _txtLog.Text = $"[{DateTime.Now:HH:mm:ss}] Error: {ex.Message}";
            }
        }

        private void CheckLowBatteryNotification(int batteryPercent)
        {
            if (!_chkEnableNotification.Checked) return;

            int threshold = (int)_numNotificationThreshold.Value;

            if (batteryPercent <= threshold)
            {
                if (!_hasNotifiedLowBattery)
                {
                    _notifyIcon.ShowBalloonTip(
                        5000,
                        "Ajazz AJ179 Pro - Low Battery Alert",
                        $"Battery is at {batteryPercent}% (below your {threshold}% threshold). Please charge your mouse!",
                        ToolTipIcon.Warning
                    );
                    _hasNotifiedLowBattery = true;
                }
            }
            else if (batteryPercent > threshold + 2)
            {
                // Reset notification flag when battery goes back above threshold
                _hasNotifiedLowBattery = false;
            }
        }

        private void SetPollingInterval(int minutes)
        {
            _pollingIntervalMinutes = minutes;
            _pollingTimer.Change(TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(minutes));
        }

        private void ToggleWindowVisibility()
        {
            if (Visible && WindowState != FormWindowState.Minimized)
            {
                MinimizeToTray();
            }
            else
            {
                RestoreFromTray();
            }
        }

        public void RestoreFromTray()
        {
            _allowShowForm = true;
            Show();
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            BringToFront();
            Activate();
        }

        public void MinimizeToTray()
        {
            Hide();
            ShowInTaskbar = false;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && _chkMinimizeToTrayOnClose.Checked)
            {
                e.Cancel = true;
                MinimizeToTray();
            }
            else
            {
                ExitApplication();
                base.OnFormClosing(e);
            }
        }

        private void LoadUserSettings()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistrySettingsKey, false);
                if (key != null)
                {
                    object? notifyVal = key.GetValue("NotifyEnabled");
                    if (notifyVal is int notifyInt)
                    {
                        _chkEnableNotification.Checked = notifyInt == 1;
                    }

                    object? thresholdVal = key.GetValue("LowThreshold");
                    if (thresholdVal is int thresholdInt && thresholdInt >= 5 && thresholdInt <= 90)
                    {
                        _numNotificationThreshold.Value = thresholdInt;
                    }
                }
            }
            catch { }
        }

        private void SaveUserSettings()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistrySettingsKey, true);
                key.SetValue("NotifyEnabled", _chkEnableNotification.Checked ? 1 : 0);
                key.SetValue("LowThreshold", (int)_numNotificationThreshold.Value);
            }
            catch { }
        }

        private bool IsStartWithWindowsEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        private void ToggleStartWithWindows(bool enable)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true);
                if (key == null) return;

                if (enable)
                {
                    string? exePath = Application.ExecutablePath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to modify startup registry: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExitApplication()
        {
            _pollingTimer?.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            if (_currentHicon != IntPtr.Zero)
            {
                DestroyIcon(_currentHicon);
                _currentHicon = IntPtr.Zero;
            }
            Application.ExitThread();
        }
    }
}
