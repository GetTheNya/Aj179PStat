using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Aj179PStat
{
    public class MainForm : Form
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        private const int DBT_DEVNODES_CHANGED = 0x0007;

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
        private bool _wasActive = false;
        private bool _isUpdating = false;

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

            // Single Left-Click refreshes status immediately
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    UpdateBatteryStatusAsync(isManualRefresh: true);
                }
            };

            // Double Left-Click opens/restores Dashboard form
            _notifyIcon.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    RestoreFromTray();
                }
            };

            // Timer initialized FIRST before initial battery fetch
            _pollingTimer = new System.Threading.Timer(_ => 
            {
                if (InvokeRequired)
                    BeginInvoke(new Action(() => UpdateBatteryStatusAsync()));
                else
                    UpdateBatteryStatusAsync();
            }, null, TimeSpan.FromMinutes(_pollingIntervalMinutes), TimeSpan.FromMinutes(_pollingIntervalMinutes));

            // Initial fetch AFTER _pollingTimer is instantiated
            UpdateBatteryStatusAsync();
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

        protected override void WndProc(ref Message m)
        {
            // Instant auto-reconnect when Windows detects USB or HID device arrival/removal
            if (m.Msg == WM_DEVICECHANGE)
            {
                int wParam = m.WParam.ToInt32();
                if (wParam == DBT_DEVICEARRIVAL || wParam == DBT_DEVICEREMOVECOMPLETE || wParam == DBT_DEVNODES_CHANGED)
                {
                    bool isArrival = wParam == DBT_DEVICEARRIVAL;
                    if (InvokeRequired)
                        BeginInvoke(new Action(() => UpdateBatteryStatusAsync(isDeviceArrival: isArrival)));
                    else
                        UpdateBatteryStatusAsync(isDeviceArrival: isArrival);
                }
            }
            base.WndProc(ref m);
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
            _btnRefresh.Click += (s, e) => UpdateBatteryStatusAsync(isManualRefresh: true);

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
            menu.Items.Add(new ToolStripMenuItem("Refresh Now", null, (s, e) => UpdateBatteryStatusAsync(isManualRefresh: true)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication()));

            return menu;
        }

        public async void UpdateBatteryStatusAsync(bool isDeviceArrival = false, bool isManualRefresh = false)
        {
            if (_isUpdating) return;
            _isUpdating = true;

            try
            {
                var matchingPaths = HidDeviceManager.GetMatchingDevicePaths(HidDeviceManager.TargetVendorId, HidDeviceManager.TargetProductId);

                // 2-second delay if mouse just woke up or reconnected
                if (matchingPaths.Count > 0 && (!_wasActive || isDeviceArrival) && !isManualRefresh)
                {
                    if (_lblStatusState != null)
                        _lblStatusState.Text = "Mouse waking up! Initializing (2s delay)...";

                    await Task.Delay(2000);
                }

                BatteryStatus status = _hidManager.ReadBatteryStatus();
                _wasActive = status.IsConnected && status.IsMouseActive;

                // Dynamic Polling adjustment:
                if (_pollingTimer != null)
                {
                    if (!_wasActive)
                    {
                        // Fast poll every 10 seconds when mouse is asleep/idle (data[4] = 1) or disconnected
                        _pollingTimer.Change(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
                    }
                    else
                    {
                        // Restore normal polling interval when mouse is active (data[4] = 0)
                        _pollingTimer.Change(TimeSpan.FromMinutes(_pollingIntervalMinutes), TimeSpan.FromMinutes(_pollingIntervalMinutes));
                    }
                }

                // Update Form UI
                if (_lblBatteryPercent != null && _lblStatusState != null && _txtLog != null)
                {
                    if (!status.IsConnected)
                    {
                        _lblBatteryPercent.Text = "N/A";
                        _lblBatteryPercent.ForeColor = Color.Gray;
                        _lblStatusState.Text = "Disconnected (Receiver not found)";
                        _txtLog.Text = $"[{DateTime.Now:HH:mm:ss}] Receiver Disconnected.\r\nSearching for VID_3151 & PID_402D...";
                    }
                    else if (!status.IsMouseActive)
                    {
                        // Asleep / Idle: Display last known battery percentage in Gray
                        _lblBatteryPercent.Text = $"{status.BatteryPercent}%";
                        _lblBatteryPercent.ForeColor = Color.Gray;
                        _lblStatusState.Text = "Status: Mouse Asleep / Idle (data[4] = 1)";
                        _txtLog.Text = $"[{DateTime.Now:HH:mm:ss}] Mouse Asleep / Idle (data[4] = {status.RawDockStatusByte}).\r\n" +
                                       $"Showing last reported battery level: {status.BatteryPercent}% (may not be 100% real-time).\r\n" +
                                       $"Raw Payload Hex: {status.RawDataHex}";
                    }
                    else
                    {
                        _lblBatteryPercent.Text = $"{status.BatteryPercent}%";
                        _lblBatteryPercent.ForeColor = status.BatteryPercent > 50 ? Color.LightGreen :
                                                       status.BatteryPercent > 20 ? Color.Gold : Color.OrangeRed;
                        _lblStatusState.Text = "Status: Mouse Active (2.4GHz Dock)";
                        
                        _txtLog.Text = $"[{DateTime.Now:HH:mm:ss}] Battery: {status.BatteryPercent}% (data[4] = {status.RawDockStatusByte})\r\n" +
                                       $"Message: {status.StatusMessage}\r\n" +
                                       $"Raw Payload Hex: {status.RawDataHex}";

                        // Low battery notification check
                        CheckLowBatteryNotification(status.BatteryPercent);
                    }
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

                string tooltip = !status.IsConnected
                    ? "Ajazz AJ179 Pro: Disconnected"
                    : !status.IsMouseActive
                        ? $"Ajazz AJ179 Pro: {status.BatteryPercent}% (Asleep / Idle)"
                        : $"Ajazz AJ179 Pro: {status.BatteryPercent}%";

                _notifyIcon.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
            }
            catch (Exception ex)
            {
                if (_txtLog != null)
                    _txtLog.Text = $"[{DateTime.Now:HH:mm:ss}] Error: {ex.Message}";
            }
            finally
            {
                _isUpdating = false;
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
            _pollingTimer?.Change(TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(minutes));
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
