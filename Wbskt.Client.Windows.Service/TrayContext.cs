using System.Windows.Forms;
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
    private readonly ConfigurationStore _store;
    private IWbsktClient? _client;

    public TrayContext()
    {
        _store = new ConfigurationStore();
        
        // 1. Setup Engine & Handlers
        var handlers = new List<IActionHandler>
        {
            new ProcessStartHandler(),
            new PowerShellHandler(),
            new SystemControlHandler()
        };
        _engine = new MappingEngine(handlers);

        // 2. Load Mappings
        var mappings = _store.LoadMappings();
        _engine.UpdateMappings(mappings);

        // 3. Setup Tray Icon
        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "WBSKT Edge Agent (Connecting...)",
            ContextMenuStrip = new ContextMenuStrip(),
            Visible = true
        };

        _trayIcon.ContextMenuStrip.Items.Add("Configure", null, OnConfigure);
        _trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, OnExit);
        
        _trayIcon.DoubleClick += (s, e) => OnConfigure(s, e);

        // 4. Initialize SDK
        InitializeClient();
    }

    private void InitializeClient()
    {
        var settings = _store.LoadSettings();
        if (settings == null)
        {
            _trayIcon.Text = "WBSKT Edge Agent (Not Configured)";
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
        if (_client != null) await _client.DisposeAsync();
        _trayIcon.Visible = false;
        Application.Exit();
    }
}
