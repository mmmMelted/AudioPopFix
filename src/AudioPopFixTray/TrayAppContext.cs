using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace AudioPopFixTray
{
    public class TrayAppContext : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private readonly Dictionary<string, SilentPlayer> _players = new();

        private readonly string _configDir;
        private readonly string _configPath;
        private AppConfig _config = new();

        private const string AppName = "AudioPopFixTray";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private readonly bool _portableMode;

        private readonly System.Threading.Timer _keepAliveTimer;

        public TrayAppContext()
        {
            // --- Portable mode detection ---
            var exeDir = Path.GetDirectoryName(Application.ExecutablePath)!;
            _portableMode =
                Environment.GetCommandLineArgs().Any(a => a.Equals("--portable", StringComparison.OrdinalIgnoreCase)) ||
                File.Exists(Path.Combine(exeDir, "portable.mode")) ||
                Environment.GetEnvironmentVariable("AUDIOPOPFIX_PORTABLE") == "1";

            var configOverride = Environment.GetEnvironmentVariable("AUDIOPOPFIX_CONFIG");
            _configDir = ResolveConfigDirectory(_portableMode, exeDir, configOverride);
            Directory.CreateDirectory(_configDir);
            _configPath = Path.Combine(_configDir, "config.json");

            _config = LoadConfig();

            _tray = new NotifyIcon
            {
                Icon = GetAppIcon(),
                Text = _portableMode ? "AudioPopFix (Portable)" : "AudioPopFix – keeping selected devices awake",
                Visible = true,
                ContextMenuStrip = BuildMenu()
            };

            // Power/resume awareness
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            // Gentle keep-alive: every 5s make sure streams are still playing
            _keepAliveTimer = new System.Threading.Timer(_ => SafeNudgeAll(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

            RefreshPlayers();
        }

        protected override void ExitThreadCore()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _keepAliveTimer?.Dispose();
            StopAll();
            _tray.Visible = false;
            base.ExitThreadCore();
        }

        // ---------- UI ----------

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();

            var devicesItem = new ToolStripMenuItem("Select Devices...", null, (_, __) => ShowDevicePicker());

            var startWithWindows = new ToolStripMenuItem("Start with Windows", null, (_, __) =>
            {
                if (_portableMode)
                {
                    MessageBox.Show(
                        "Start with Windows is disabled in Portable Mode.\n\n" +
                        "Tip: create a shortcut to this EXE in the Startup folder (shell:startup).",
                        "AudioPopFix Portable", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                SetStartupEnabled(!IsStartupEnabled());
                UpdateMenuChecks(menu);
            })
            {
                Checked = !_portableMode && IsStartupEnabled(),
                Enabled = !_portableMode
            };

            var openConfig = new ToolStripMenuItem("Open Config Folder", null, (_, __) =>
            {
                try { Process.Start("explorer.exe", _configDir); } catch { }
            });

            var restartItem = new ToolStripMenuItem("Restart Streams", null, (_, __) => HardKickAll());

            var aboutItem = new ToolStripMenuItem("About", null, (_, __) =>
            {
                MessageBox.Show(
                    "AudioPopFix Tray\n\n" +
                    "Keeps selected audio devices awake by playing silence.\n\n" +
                    $"Config: {_configPath}\n" +
                    $"Portable Mode: {_portableMode}\n\n" +
                    "Resume handling: auto-kick streams on wake from sleep/hibernate.",
                    "AudioPopFix", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });

            var exitItem = new ToolStripMenuItem("Exit", null, (_, __) =>
            {
                StopAll();
                _tray.Visible = false;
                Application.Exit();
            });

            menu.Items.Add(devicesItem);
            menu.Items.Add(startWithWindows);
            menu.Items.Add(openConfig);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(restartItem);
            menu.Items.Add(aboutItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            return menu;
        }

        private void UpdateMenuChecks(ContextMenuStrip menu)
        {
            foreach (ToolStripItem item in menu.Items)
            {
                if (item is ToolStripMenuItem mi && mi.Text.StartsWith("Start with Windows", StringComparison.Ordinal))
                {
                    mi.Checked = !_portableMode && IsStartupEnabled();
                    mi.Enabled = !_portableMode;
                }
            }
        }

        private void ShowDevicePicker()
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .OrderBy(d => d.FriendlyName)
                .ToList();

            using var dialog = new DevicePickerForm(devices, _config.DeviceIds);
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _config.DeviceIds = dialog.SelectedDeviceIds.ToList();
                SaveConfig();
                RefreshPlayers();
            }
        }

        // ---------- Power handling ----------

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                // Be aggressive: restart streams twice within ~1s
                HardKickAll();
            }
        }

        private void SafeNudgeAll()
        {
            try
            {
                foreach (var p in _players.Values)
                    p.Nudge();
            }
            catch { /* swallow */ }
        }

        private void HardKickAll()
        {
            Task.Run(async () =>
            {
                try
                {
                    // First pass: quick restart existing players
                    foreach (var p in _players.Values) p.Restart();

                    // Second pass after a short delay: re-enumerate in case device IDs changed on resume
                    await Task.Delay(300);
                    RefreshPlayers();

                    // Third pass: one more restart to ensure streams are hot after drivers settle
                    await Task.Delay(300);
                    foreach (var p in _players.Values) p.Nudge();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"HardKickAll error: {ex}");
                }
            });
        }

        // ---------- Core logic ----------

        private void RefreshPlayers()
        {
            try
            {
                // Stop players no longer selected
                var toStop = _players.Keys.Where(id => !_config.DeviceIds.Contains(id)).ToList();
                foreach (var id in toStop)
                {
                    _players[id].Dispose();
                    _players.Remove(id);
                }

                // Start players for newly selected devices
                using var enumerator = new MMDeviceEnumerator();
                foreach (var id in _config.DeviceIds.Distinct())
                {
                    if (_players.ContainsKey(id)) continue;

                    try
                    {
                        var device = enumerator.GetDevice(id);
                        var player = new SilentPlayer(device);
                        player.Start();
                        _players[id] = player;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to start player for {id}: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RefreshPlayers error: {ex}");
            }
        }

        private void StopAll()
        {
            foreach (var p in _players.Values)
                p.Dispose();
            _players.Clear();
        }

        // ---------- Startup (Run key) ----------

        private bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return (key?.GetValue(AppName) as string) is { Length: > 0 };
            }
            catch { return false; }
        }

        private void SetStartupEnabled(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (enable)
                {
                    var exe = Application.ExecutablePath;
                    key.SetValue(AppName, $"\"{exe}\"");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to update Startup setting.\n\n" + ex.Message,
                    "AudioPopFix", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---------- Config ----------

        private static string ResolveConfigDirectory(bool portable, string exeDir, string? overrideDir)
        {
            if (!string.IsNullOrWhiteSpace(overrideDir))
                return overrideDir;

            if (portable)
                return exeDir;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "AudioPopFix");
        }

        private AppConfig LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                    if (cfg != null && cfg.DeviceIds != null)
                        return cfg;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadConfig error: {ex}");
            }
            return new AppConfig { DeviceIds = new List<string>() };
        }

        private void SaveConfig()
        {
            try
            {
                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveConfig error: {ex}");
            }
        }

        // ---------- Icon helpers ----------

        private static Icon GetAppIcon()
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch { }
            return SystemIcons.Information;
        }
    }

    public class AppConfig
    {
        public List<string> DeviceIds { get; set; } = new();
    }
}
