using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        private AppConfig _config;
        private const string AppName = "AudioPopFixTray";
        private readonly bool _portableMode;

        public TrayAppContext()
        {
            // Portable mode detection:
            // 1) If the exe is started with "--portable" OR
            // 2) If a file "portable.mode" exists next to the exe OR
            // 3) If an env var AUDIOPOPFIX_PORTABLE=1
            var exeDir = Path.GetDirectoryName(Application.ExecutablePath)!;
            _portableMode = Environment.GetCommandLineArgs().Any(a => a.Equals("--portable", StringComparison.OrdinalIgnoreCase))
                           || File.Exists(Path.Combine(exeDir, "portable.mode"))
                           || (Environment.GetEnvironmentVariable("AUDIOPOPFIX_PORTABLE") == "1");

            // Optional override: AUDIOPOPFIX_CONFIG points to a folder
            var configOverride = Environment.GetEnvironmentVariable("AUDIOPOPFIX_CONFIG");

            if (_portableMode)
            {
                _configDir = string.IsNullOrWhiteSpace(configOverride) ? exeDir : configOverride;
            }
            else
            {
                _configDir = string.IsNullOrWhiteSpace(configOverride)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioPopFix")
                    : configOverride;
            }

            Directory.CreateDirectory(_configDir);
            _configPath = Path.Combine(_configDir, "config.json");
            _config = LoadConfig();

            _tray = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                Text = _portableMode ? "AudioPopFix (Portable)" : "AudioPopFix – keeping selected devices awake",
                Visible = true,
                ContextMenuStrip = BuildMenu()
            };

            RefreshPlayers();
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();

            var devicesItem = new ToolStripMenuItem("Select Devices...", null, (_, __) => ShowDevicePicker());
            var startWithWindows = new ToolStripMenuItem("Start with Windows", null, (_, __) =>
            {
                if (_portableMode)
                {
                    MessageBox.Show("Start with Windows is disabled in Portable Mode.\n\nCreate a shortcut to this EXE in shell:startup if needed.",
                        "AudioPopFix Portable", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                bool enabled = !IsStartupEnabled();
                SetStartupEnabled(enabled);
                UpdateMenuChecks(menu);
            })
            {
                Checked = !_portableMode && IsStartupEnabled(),
                Enabled = !_portableMode
            };

            var showConfig = new ToolStripMenuItem("Open Config Folder", null, (_, __) =>
            {
                try { Process.Start("explorer.exe", _configDir); } catch { }
            });

            var restartItem = new ToolStripMenuItem("Restart Streams", null, (_, __) => RefreshPlayers());
            var aboutItem = new ToolStripMenuItem("About", null, (_, __) =>
            {
                MessageBox.Show("AudioPopFix Tray\n\nKeeps selected audio devices awake by playing silence.\n\n" +
                                $"Config: {_configPath}\n" +
                                $"Portable Mode: {_portableMode}\n\n" +
                                "Flags:\n  --portable   Force portable mode\nEnv Vars:\n  AUDIOPOPFIX_PORTABLE=1\n  AUDIOPOPFIX_CONFIG=<folder>",
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
            menu.Items.Add(showConfig);
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
                if (item is ToolStripMenuItem mi && mi.Text.StartsWith("Start with Windows"))
                {
                    mi.Checked = !_portableMode && IsStartupEnabled();
                }
            }
        }

        private void ShowDevicePicker()
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
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

        private void RefreshPlayers()
        {
            var toStop = _players.Keys.Where(id => !_config.DeviceIds.Contains(id)).ToList();
            foreach (var id in toStop)
            {
                _players[id].Dispose();
                _players.Remove(id);
            }

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

        private void StopAll()
        {
            foreach (var p in _players.Values) p.Dispose();
            _players.Clear();
        }

        private static string RunKeyPath => @"Software\Microsoft\Windows\CurrentVersion\Run";

        private bool IsStartupEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var val = key?.GetValue(AppName) as string;
            return !string.IsNullOrWhiteSpace(val);
        }

        private void SetStartupEnabled(bool enable)
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

        private AppConfig LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                    if (cfg != null && cfg.DeviceIds != null) return cfg;
                }
            }
            catch { }
            return new AppConfig { DeviceIds = new List<string>() };
        }

        private void SaveConfig()
        {
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
    }

    public class AppConfig
    {
        public List<string> DeviceIds { get; set; } = new();
    }
}
