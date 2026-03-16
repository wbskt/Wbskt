using System.IO;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.Client.Windows.Models;
using Wbskt.Client.Windows.Service.Engine;
using Wbskt.Client.Windows.Service.Handlers;

namespace Wbskt.Client.Windows.Service;

public class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly MappingEngine _engine;
    private IWbsktClient? _client;

    public TrayContext()
    {
        // 1. Setup Engine & Handlers
        var handlers = new List<IActionHandler>
        {
            new ProcessStartHandler(),
            new ProcessKillHandler(),
            new PowerShellHandler(),
            new SystemPowerHandler(),
            new ToastHandler(),
            new VolumeControlHandler(),
            new OpenFileHandler()
        };
        _engine = new MappingEngine(handlers);

        // 2. Load Mappings
        var mappings = ConfigurationStore.LoadMappings();
        _engine.UpdateMappings(mappings);

        // 3. Setup Tray Icon
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "WBSKT Windows Agent",
            ContextMenuStrip = new ContextMenuStrip(),
            Visible = true
        };

        _trayIcon.ContextMenuStrip.Items.Add("Configure Commands", null, OnConfigure);
        _trayIcon.ContextMenuStrip.Items.Add("Open Settings File", null, OnOpenSettings);
        _trayIcon.ContextMenuStrip.Items.Add("Reconnect", null, OnReconnect);
        _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, OnExit);
        
        _trayIcon.DoubleClick += OnConfigure;

        // 4. Initialize SDK
        InitializeClient();
    }

    private void OnOpenSettings(object? sender, EventArgs e)
    {
        if (!File.Exists(ConfigurationStore.SettingsPath))
        {
            ConfigurationStore.SaveSettings(ConfigurationStore.LoadSettings()); // Ensure default exists
        }
        
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ConfigurationStore.SettingsPath) 
        { 
            UseShellExecute = true 
        });
    }

    private async void OnReconnect(object? sender, EventArgs e)
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
        }

        InitializeClient();
    }

    private void InitializeClient()
    {
        var settings = ConfigurationStore.LoadSettings();
        if (string.IsNullOrEmpty(settings.PolicyPin) && !File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wbskt", "security", "client.id")))
        {
            _trayIcon.Text = "WBSKT Edge Agent (No PIN in settings.json)";
            return;
        }

        var config = new ClientConfig(
            settings.BaseApiUrl,
            settings.BaseSocketUrl,
            settings.DeviceName,
            settings.PolicyPin
        );

        var storage = new SecureClientStorage();
        _client = new WbsktClient(config, storage);

        _client.OnConnected += () => {
            _trayIcon.Text = "WBSKT Edge Agent (Connected)";
            
            // Build and send current capabilities
            var caps = CapabilityDiscovery.BuildCapabilities(ConfigurationStore.LoadMappings());
            _ = _client.UpdateCapabilitiesAsync(caps);
        };

        _client.OnDisconnected += () => {
            _trayIcon.Text = "WBSKT Edge Agent (Disconnected)";
        };

        _client.OnCommandReceived += (command, payload) => {
            var json = payload?.ToString() ?? "{}";
            _ = _engine.ProcessCommandAsync(command, json);
        };

        Task.Run(async () => {
            try 
            {
                await _client.StartAsync();
            }
            catch (Exception ex)
            {
                // TODO: Show error in UI
                Console.WriteLine($"SDK Start Failed: {ex.Message}");
            }
        });
    }

    private void OnConfigure(object? sender, EventArgs e)
    {
        // TODO: Launch Wbskt.Client.Windows.UI
        MessageBox.Show("Configuration UI will be launched here.");
    }

    private async void OnExit(object? sender, EventArgs e)
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
        }

        _trayIcon.Visible = false;
        Application.Exit();
    }
}
