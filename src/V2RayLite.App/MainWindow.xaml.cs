using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using V2RayLite.Core;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfClipboard = System.Windows.Clipboard;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using WinForms = System.Windows.Forms;

namespace V2RayLite.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly AppPaths _paths = new();
    private readonly AppLogService _log;
    private readonly SettingsStore _store;
    private readonly SubscriptionService _subscriptionService;
    private readonly XrayProcessService _xrayService;
    private readonly SystemProxyService _systemProxyService = new();
    private readonly DelayTestService _delayTestService;
    private readonly XrayDownloadService _downloadService;
    private readonly ObservableCollection<ProxyNode> _nodes = [];
    private readonly WinForms.NotifyIcon _notifyIcon;

    private AppSettings _settings = new();
    private string _currentPage = "Home";
    private bool _isProxyEnabled;
    private string _toastMessage = string.Empty;
    private string _subscriptionStatus = "未更新";
    private string _searchText = string.Empty;
    private bool _isApplyingSettings;
    private WinForms.ToolStripMenuItem? _trayStatusItem;
    private WinForms.ToolStripMenuItem? _trayNodeItem;
    private WinForms.ToolStripMenuItem? _trayToggleItem;
    private WinForms.ToolStripMenuItem? _traySwitchNodeItem;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        var httpClient = new HttpClient();
        var configBuilder = new XrayConfigBuilder();
        _log = new AppLogService(_paths);
        _store = new SettingsStore(_paths);
        _subscriptionService = new SubscriptionService(httpClient, new SubscriptionParser(), _store);
        _xrayService = new XrayProcessService(_paths, configBuilder, _log);
        _delayTestService = new DelayTestService(_paths, configBuilder, _log);
        _downloadService = new XrayDownloadService(httpClient, _paths);
        _notifyIcon = CreateNotifyIcon();

        Opacity = 0;
        Loaded += async (_, _) =>
        {
            BeginIntroAnimation();
            await LoadAsync();
        };
        Closing += MainWindow_Closing;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProxyNode> FilteredNodes { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];

    public string CurrentPage
    {
        get => _currentPage;
        set
        {
            if (_currentPage == value) return;
            _currentPage = value;
            Notify();
            NotifyNavigation();
            if (string.Equals(value, "Logs", StringComparison.OrdinalIgnoreCase))
            {
                RefreshLogLines();
            }
        }
    }

    public string RuntimeStatus => _isProxyEnabled ? "已开启" : "未开启";
    public string SidebarStatus => _isProxyEnabled ? "已连接" : "未连接";
    public string ToggleProxyText => _isProxyEnabled ? "关闭代理" : "开启代理";
    public MediaBrush RuntimeStatusBrush => Brush(_isProxyEnabled ? "#18AE4D" : "#64748B");
    public MediaBrush SidebarStatusBrush => Brush(_isProxyEnabled ? "#2BCB66" : "#94A3B8");
    public string ActiveNodeName => ActiveNode?.Name ?? "未选择";
    public string ActiveNodeDelay => ActiveNode?.DisplayDelay ?? "—";
    public bool HasActiveNode => ActiveNode is not null;
    public string ProxyModeText => _settings.ProxyMode switch
    {
        ProxyMode.Rule => "规则模式",
        ProxyMode.Global => "全局模式",
        ProxyMode.Direct => "直连模式",
        _ => "规则模式"
    };
    public int NodeCount => _nodes.Count;
    public bool HasNodes => FilteredNodes.Count > 0;
    public bool HasLogs => LogLines.Count > 0;
    public bool IsSearchEmpty => string.IsNullOrWhiteSpace(_searchText);
    public string LastUpdateText => _settings.LastSubscriptionUpdate?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";

    public string SubscriptionStatus
    {
        get => _subscriptionStatus;
        set
        {
            _subscriptionStatus = value;
            Notify();
            Notify(nameof(SubscriptionStatusBrush));
        }
    }

    public MediaBrush SubscriptionStatusBrush =>
        SubscriptionStatus.Contains("失败", StringComparison.Ordinal) ||
        SubscriptionStatus.Contains("空", StringComparison.Ordinal)
            ? Brush("#F97316")
            : Brush("#18AE4D");

    public string ToastMessage
    {
        get => _toastMessage;
        set
        {
            _toastMessage = value;
            Notify();
        }
    }

    public MediaBrush HomeNavBackground => NavBackground("Home");
    public MediaBrush NodesNavBackground => NavBackground("Nodes");
    public MediaBrush SubscriptionNavBackground => NavBackground("Subscription");
    public MediaBrush LogsNavBackground => NavBackground("Logs");
    public MediaBrush SettingsNavBackground => NavBackground("Settings");
    public MediaBrush HomeNavForeground => NavForeground("Home");
    public MediaBrush NodesNavForeground => NavForeground("Nodes");
    public MediaBrush SubscriptionNavForeground => NavForeground("Subscription");
    public MediaBrush LogsNavForeground => NavForeground("Logs");
    public MediaBrush SettingsNavForeground => NavForeground("Settings");

    private ProxyNode? ActiveNode => _nodes.FirstOrDefault(node => node.Id == _settings.ActiveNodeId) ?? _nodes.FirstOrDefault();

    private async Task LoadAsync()
    {
        _settings = await _store.LoadSettingsAsync();
        var nodes = await _store.LoadNodesAsync();

        _nodes.Clear();
        foreach (var node in nodes)
        {
            _nodes.Add(node);
        }

        if (_settings.ActiveNodeId is null && _nodes.Count > 0)
        {
            _settings.ActiveNodeId = _nodes[0].Id;
        }

        ApplySettingsToControls();
        ApplyTheme();
        RefreshActiveFlags();
        RefreshFilteredNodes();
        RefreshLogLines();
        RefreshStatusProperties();

        if (_settings.AutoConnectOnLaunch && ActiveNode is not null && File.Exists(_xrayService.XrayExePath))
        {
            await ConnectActiveNodeAsync(enableSystemProxy: true);
        }
    }

    private void ApplySettingsToControls()
    {
        _isApplyingSettings = true;
        SubscriptionUrlTextBox.Text = _settings.SubscriptionUrl;
        HttpPortTextBox.Text = _settings.HttpPort.ToString();
        SocksPortTextBox.Text = _settings.SocksPort.ToString();
        DelayTestUrlTextBox.Text = _settings.DelayTestUrl;
        StartOnBootCheckBox.IsChecked = _settings.StartOnBoot;
        SidebarStartupCheckBox.IsChecked = _settings.StartOnBoot;
        AutoConnectCheckBox.IsChecked = _settings.AutoConnectOnLaunch;
        MinimizeToTrayCheckBox.IsChecked = _settings.MinimizeToTray;
        DarkModeCheckBox.IsChecked = _settings.DarkMode;
        RuleModeRadio.IsChecked = _settings.ProxyMode == ProxyMode.Rule;
        GlobalModeRadio.IsChecked = _settings.ProxyMode == ProxyMode.Global;
        DirectModeRadio.IsChecked = _settings.ProxyMode == ProxyMode.Direct;
        RoutingRuleModeComboBox.SelectedIndex = (int)_settings.RoutingRuleMode;
        BypassMainlandCheckBox.IsChecked = _settings.BypassMainland;
        DirectDomainsTextBox.Text = _settings.DirectDomains;
        DirectIpsTextBox.Text = _settings.DirectIps;
        ProxyDomainsTextBox.Text = _settings.ProxyDomains;
        ProxyIpsTextBox.Text = _settings.ProxyIps;
        BlockDomainsTextBox.Text = _settings.BlockDomains;
        BlockIpsTextBox.Text = _settings.BlockIps;
        EnableCustomDnsCheckBox.IsChecked = _settings.EnableCustomDns;
        EnableFakeDnsCheckBox.IsChecked = _settings.EnableFakeDns;
        EnableSplitDnsCheckBox.IsChecked = _settings.EnableSplitDns;
        DomesticDnsTextBox.Text = _settings.DomesticDns;
        ForeignDnsTextBox.Text = _settings.ForeignDns;
        DohDnsTextBox.Text = _settings.DohDns;
        DotDnsTextBox.Text = _settings.DotDns;
        _isApplyingSettings = false;
    }

    private WinForms.NotifyIcon CreateNotifyIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        _trayStatusItem = new WinForms.ToolStripMenuItem("当前状态：未连接") { Enabled = false };
        _trayNodeItem = new WinForms.ToolStripMenuItem("当前节点：未选择") { Enabled = false };
        _trayToggleItem = new WinForms.ToolStripMenuItem("开启代理");
        _trayToggleItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(async () => await ToggleProxyFromTrayAsync()));
        _traySwitchNodeItem = new WinForms.ToolStripMenuItem("切换节点");

        menu.Items.Add(_trayStatusItem);
        menu.Items.Add(_trayNodeItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_trayToggleItem);
        menu.Items.Add(_traySwitchNodeItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("打开主窗口", null, (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromTray)));
        menu.Items.Add("打开节点列表", null, (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            ShowFromTray();
            CurrentPage = "Nodes";
        })));
        menu.Items.Add("打开设置", null, (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            ShowFromTray();
            CurrentPage = "Settings";
        })));
        menu.Items.Add("查看日志", null, (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        {
            ShowFromTray();
            CurrentPage = "Logs";
        })));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.BeginInvoke(new Action(Close)));
        menu.Opening += (_, _) => UpdateTrayMenu();

        var notifyIcon = new WinForms.NotifyIcon
        {
            Text = "MyRay Lite",
            Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico")),
            Visible = true,
            ContextMenuStrip = menu
        };
        notifyIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromTray));
        return notifyIcon;
    }

    private async Task ToggleProxyFromTrayAsync()
    {
        if (_isProxyEnabled)
        {
            DisableProxy();
            return;
        }

        if (ActiveNode is null)
        {
            ShowFromTray();
            CurrentPage = "Subscription";
            ToastMessage = "请先更新订阅并选择节点。";
            return;
        }

        await ConnectActiveNodeAsync(enableSystemProxy: true);
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string page })
        {
            CurrentPage = page;
        }
    }

    private async void ToggleProxy_Click(object sender, RoutedEventArgs e)
    {
        if (_isProxyEnabled)
        {
            DisableProxy();
            return;
        }

        if (ActiveNode is null)
        {
            ToastMessage = "请先更新订阅并选择节点。";
            CurrentPage = "Subscription";
            return;
        }

        await ConnectActiveNodeAsync(enableSystemProxy: true);
    }

    private async Task ConnectActiveNodeAsync(bool enableSystemProxy)
    {
        try
        {
            ToastMessage = "正在启动 Xray...";
            await _xrayService.StartAsync(ActiveNode!, _settings);
            if (enableSystemProxy)
            {
                _systemProxyService.Enable(_settings);
            }

            _isProxyEnabled = true;
            ToastMessage = "代理已开启。";
            _notifyIcon.BalloonTipTitle = "MyRay Lite";
            _notifyIcon.BalloonTipText = $"已连接：{ActiveNodeName}";
            _notifyIcon.ShowBalloonTip(1000);
        }
        catch (Exception ex)
        {
            _isProxyEnabled = false;
            ToastMessage = ex.Message;
            RefreshLogLines();
        }
        finally
        {
            RefreshStatusProperties();
        }
    }

    private void DisableProxy()
    {
        _xrayService.Stop();
        _systemProxyService.Disable();
        _isProxyEnabled = false;
        ToastMessage = "代理已关闭。";
        RefreshStatusProperties();
    }

    private void SwitchNode_Click(object sender, RoutedEventArgs e)
    {
        if (_nodes.Count == 0)
        {
            CurrentPage = "Subscription";
            ToastMessage = "请先更新订阅。";
            return;
        }

        var currentIndex = Math.Max(0, _nodes.ToList().FindIndex(node => node.Id == _settings.ActiveNodeId));
        var next = _nodes[(currentIndex + 1) % _nodes.Count];
        _ = SetActiveNodeAsync(next, reconnect: _isProxyEnabled);
    }

    private async void ConnectNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string nodeId })
        {
            var node = _nodes.FirstOrDefault(item => item.Id == nodeId);
            if (node is not null)
            {
                await SetActiveNodeAsync(node, reconnect: _isProxyEnabled);
                if (!_isProxyEnabled)
                {
                    CurrentPage = "Home";
                }
            }
        }
    }

    private async Task SetActiveNodeAsync(ProxyNode node, bool reconnect)
    {
        _settings.ActiveNodeId = node.Id;
        await _store.SaveSettingsAsync(_settings);
        RefreshActiveFlags();
        RefreshFilteredNodes();
        RefreshStatusProperties();
        ToastMessage = $"已选择 {node.Name}";

        if (reconnect)
        {
            DisableProxy();
            await ConnectActiveNodeAsync(enableSystemProxy: true);
        }
    }

    private async void SaveSubscription_Click(object sender, RoutedEventArgs e)
    {
        _settings.SubscriptionUrl = SubscriptionUrlTextBox.Text.Trim();
        await _store.SaveSettingsAsync(_settings);
        ToastMessage = "订阅地址已保存。";
    }

    private async void UpdateSubscription_Click(object sender, RoutedEventArgs e)
    {
        _settings.SubscriptionUrl = SubscriptionUrlTextBox.Text.Trim();
        await _store.SaveSettingsAsync(_settings);
        ToastMessage = "正在更新订阅...";
        SubscriptionStatus = "更新中";
        _log.Info("开始更新订阅。");

        var snapshot = await _subscriptionService.UpdateAsync(_settings);
        _nodes.Clear();
        foreach (var node in snapshot.Nodes)
        {
            _nodes.Add(node);
        }

        if (_nodes.Count > 0 && (_settings.ActiveNodeId is null || _nodes.All(node => node.Id != _settings.ActiveNodeId)))
        {
            _settings.ActiveNodeId = _nodes[0].Id;
            await _store.SaveSettingsAsync(_settings);
        }

        SubscriptionStatus = snapshot.StatusText;
        ToastMessage = snapshot.StatusText;
        _log.Info($"订阅更新完成：{snapshot.StatusText}，节点 {snapshot.Nodes.Count} 个。");
        RefreshActiveFlags();
        RefreshFilteredNodes();
        RefreshStatusProperties();
    }

    private async void TestActiveNode_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveNode is null)
        {
            ToastMessage = "请先选择节点。";
            return;
        }

        await TestNodeAsync(ActiveNode);
    }

    private async void TestAllNodes_Click(object sender, RoutedEventArgs e)
    {
        ToastMessage = "正在测速...";
        await _delayTestService.TestManyAsync(_nodes, _settings.DelayTestUrl, _ =>
        {
            Dispatcher.Invoke(() =>
            {
                RefreshFilteredNodes();
                RefreshStatusProperties();
            });
        });
        await _store.SaveNodesAsync(_nodes);
        ToastMessage = "测速完成。";
        RefreshLogLines();
    }

    private async Task TestNodeAsync(ProxyNode node)
    {
        node.Status = NodeStatus.Testing;
        RefreshFilteredNodes();
        RefreshStatusProperties();

        var delay = await _delayTestService.TestProxyDelayAsync(node, _settings.DelayTestUrl, TimeSpan.FromSeconds(10));
        node.DelayMs = delay;
        node.Status = delay is null ? NodeStatus.Unavailable : NodeStatus.Available;
        node.LastTested = DateTimeOffset.Now;
        await _store.SaveNodesAsync(_nodes);

        RefreshFilteredNodes();
        RefreshStatusProperties();
        ToastMessage = delay is null ? "节点不可用。" : $"延迟 {delay}ms。";
        RefreshLogLines();
    }

    private void RefreshNodes_Click(object sender, RoutedEventArgs e)
    {
        RefreshFilteredNodes();
        ToastMessage = "节点列表已刷新。";
    }

    private void CopyNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string nodeId })
        {
            var node = _nodes.FirstOrDefault(item => item.Id == nodeId);
            if (node is not null)
            {
                WpfClipboard.SetText($"{node.Name} {node.Protocol} {node.Address}:{node.Port}");
                ToastMessage = "节点信息已复制。";
            }
        }
    }

    private async void ProxyMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is WpfRadioButton { Tag: string mode } && Enum.TryParse<ProxyMode>(mode, out var parsed))
        {
            _settings.ProxyMode = parsed;
            await _store.SaveSettingsAsync(_settings);
            RefreshStatusProperties();

            if (_isProxyEnabled && ActiveNode is not null)
            {
                DisableProxy();
                await ConnectActiveNodeAsync(enableSystemProxy: true);
            }
        }
    }

    private async void SettingsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoConnectOnLaunch = AutoConnectCheckBox.IsChecked == true;
        _settings.MinimizeToTray = MinimizeToTrayCheckBox.IsChecked == true;
        _settings.DarkMode = DarkModeCheckBox.IsChecked == true;
        await _store.SaveSettingsAsync(_settings);
        ApplyTheme();
        ToastMessage = "设置已保存。";
    }

    private async void AdvancedSettings_Changed(object sender, RoutedEventArgs e)
    {
        await SaveAdvancedSettingsAsync();
    }

    private async void AdvancedTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        await SaveAdvancedSettingsAsync();
    }

    private async void ApplyNetworkSettings_Click(object sender, RoutedEventArgs e)
    {
        ReadAdvancedSettingsFromControls();
        await _store.SaveSettingsAsync(_settings);

        if (_isProxyEnabled && ActiveNode is not null)
        {
            DisableProxy();
            await ConnectActiveNodeAsync(enableSystemProxy: true);
            ToastMessage = "路由和 DNS 设置已应用。";
        }
        else
        {
            ToastMessage = "路由和 DNS 设置已保存，下次连接时生效。";
        }
    }

    private async Task SaveAdvancedSettingsAsync()
    {
        if (_isApplyingSettings || !IsLoaded)
        {
            return;
        }

        ReadAdvancedSettingsFromControls();
        await _store.SaveSettingsAsync(_settings);
        ToastMessage = _isProxyEnabled ? "网络设置已保存，点击“应用网络设置”后生效。" : "网络设置已保存。";
    }

    private void ReadAdvancedSettingsFromControls()
    {
        _settings.RoutingRuleMode = RoutingRuleModeComboBox.SelectedIndex switch
        {
            1 => RoutingRuleMode.Whitelist,
            2 => RoutingRuleMode.Blacklist,
            _ => RoutingRuleMode.Smart
        };
        _settings.BypassMainland = BypassMainlandCheckBox.IsChecked == true;
        _settings.DirectDomains = DirectDomainsTextBox.Text.Trim();
        _settings.DirectIps = DirectIpsTextBox.Text.Trim();
        _settings.ProxyDomains = ProxyDomainsTextBox.Text.Trim();
        _settings.ProxyIps = ProxyIpsTextBox.Text.Trim();
        _settings.BlockDomains = BlockDomainsTextBox.Text.Trim();
        _settings.BlockIps = BlockIpsTextBox.Text.Trim();
        _settings.EnableCustomDns = EnableCustomDnsCheckBox.IsChecked == true;
        _settings.EnableFakeDns = EnableFakeDnsCheckBox.IsChecked == true;
        _settings.EnableSplitDns = EnableSplitDnsCheckBox.IsChecked == true;
        _settings.DomesticDns = DomesticDnsTextBox.Text.Trim();
        _settings.ForeignDns = ForeignDnsTextBox.Text.Trim();
        _settings.DohDns = DohDnsTextBox.Text.Trim();
        _settings.DotDns = DotDnsTextBox.Text.Trim();
    }

    private async void StartupCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.StartOnBoot = (sender as WpfCheckBox)?.IsChecked == true;
        StartOnBootCheckBox.IsChecked = _settings.StartOnBoot;
        SidebarStartupCheckBox.IsChecked = _settings.StartOnBoot;
        await _store.SaveSettingsAsync(_settings);

        var appPath = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(appPath))
        {
            _systemProxyService.SetStartOnBoot(_settings.StartOnBoot, appPath);
        }

        ToastMessage = "开机自启设置已保存。";
    }

    private async void PortTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (int.TryParse(HttpPortTextBox.Text, out var httpPort) && httpPort is > 0 and < 65536)
        {
            _settings.HttpPort = httpPort;
        }

        if (int.TryParse(SocksPortTextBox.Text, out var socksPort) && socksPort is > 0 and < 65536)
        {
            _settings.SocksPort = socksPort;
        }

        await _store.SaveSettingsAsync(_settings);
    }

    private async void DelayTestUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _settings.DelayTestUrl = string.IsNullOrWhiteSpace(DelayTestUrlTextBox.Text)
            ? "http://www.gstatic.com/generate_204"
            : DelayTestUrlTextBox.Text.Trim();
        await _store.SaveSettingsAsync(_settings);
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchTextBox.Text.Trim();
        Notify(nameof(IsSearchEmpty));
        RefreshFilteredNodes();
    }

    private async void DownloadXray_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ToastMessage = "正在下载 Xray-core...";
            var progress = new Progress<double>(value => ToastMessage = $"正在下载 Xray-core {value:P0}");
            var directory = await _downloadService.DownloadLatestWindowsX64Async(progress);
            ToastMessage = $"Xray-core 已下载到 {directory}";
            _log.Info($"Xray-core 已下载到 {directory}");
        }
        catch (Exception ex)
        {
            ToastMessage = $"下载失败：{ex.Message}";
            _log.Error(ToastMessage);
            RefreshLogLines();
        }
    }

    private void RefreshLogs_Click(object sender, RoutedEventArgs e)
    {
        RefreshLogLines();
        ToastMessage = "日志已刷新。";
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        _log.Clear();
        RefreshLogLines();
        ToastMessage = "日志已清空。";
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        _paths.Ensure();
        Process.Start(new ProcessStartInfo
        {
            FileName = _paths.LogDirectory,
            UseShellExecute = true
        });
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.MinimizeToTray)
        {
            Hide();
            _notifyIcon.BalloonTipTitle = "MyRay Lite";
            _notifyIcon.BalloonTipText = "已最小化到托盘。";
            _notifyIcon.ShowBalloonTip(1200);
        }
        else
        {
            WindowState = WindowState.Minimized;
        }
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void WindowDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _xrayService.Stop();
        _systemProxyService.Disable();
    }

    private void RefreshFilteredNodes()
    {
        FilteredNodes.Clear();
        foreach (var node in _nodes.Where(MatchesSearch))
        {
            FilteredNodes.Add(node);
        }

        Notify(nameof(FilteredNodes));
        Notify(nameof(HasNodes));
        Notify(nameof(NodeCount));
    }

    private void RefreshLogLines()
    {
        LogLines.Clear();
        foreach (var line in _log.ReadRecentLines())
        {
            LogLines.Add(line);
        }

        Notify(nameof(LogLines));
        Notify(nameof(HasLogs));
    }

    private bool MatchesSearch(ProxyNode node)
    {
        return string.IsNullOrWhiteSpace(_searchText)
            || node.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || node.Address.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || node.Raw.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshActiveFlags()
    {
        foreach (var node in _nodes)
        {
            node.IsActive = node.Id == _settings.ActiveNodeId;
        }
    }

    private void RefreshStatusProperties()
    {
        Notify(nameof(RuntimeStatus));
        Notify(nameof(SidebarStatus));
        Notify(nameof(ToggleProxyText));
        Notify(nameof(RuntimeStatusBrush));
        Notify(nameof(SidebarStatusBrush));
        Notify(nameof(ActiveNodeName));
        Notify(nameof(ActiveNodeDelay));
        Notify(nameof(HasActiveNode));
        Notify(nameof(ProxyModeText));
        Notify(nameof(NodeCount));
        Notify(nameof(LastUpdateText));
        UpdateTrayMenu();
    }

    private void NotifyNavigation()
    {
        Notify(nameof(HomeNavBackground));
        Notify(nameof(NodesNavBackground));
        Notify(nameof(SubscriptionNavBackground));
        Notify(nameof(LogsNavBackground));
        Notify(nameof(SettingsNavBackground));
        Notify(nameof(HomeNavForeground));
        Notify(nameof(NodesNavForeground));
        Notify(nameof(SubscriptionNavForeground));
        Notify(nameof(LogsNavForeground));
        Notify(nameof(SettingsNavForeground));
    }

    private MediaBrush NavBackground(string page) => Brush(CurrentPage == page ? "#E8F2FF" : "#00FFFFFF");
    private MediaBrush NavForeground(string page) => Brush(CurrentPage == page ? "#0875F8" : "#334155");
    private static MediaBrush Brush(string color) => new MediaSolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(color));

    private void ApplyTheme()
    {
        var dark = _settings.DarkMode;
        SetBrush("SurfaceBrush", dark ? "#101722" : "#F6F8FC");
        SetBrush("PanelBrush", dark ? "#172033" : "#FFFFFF");
        SetBrush("InputBrush", dark ? "#111B2B" : "#FFFFFF");
        SetBrush("TextBrush", dark ? "#EEF3FA" : "#111827");
        SetBrush("SubtleTextBrush", dark ? "#9FB0C6" : "#64748B");
        SetBrush("SidebarTextBrush", dark ? "#D8E2F0" : "#334155");
        SetBrush("LineBrush", dark ? "#27364D" : "#E1E8F2");
    }

    private void SetBrush(string key, string color)
    {
        if (Resources[key] is MediaSolidColorBrush brush)
        {
            brush.Color = (MediaColor)MediaColorConverter.ConvertFromString(color);
        }
    }

    private void BeginIntroAnimation()
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void UpdateTrayMenu()
    {
        if (_trayStatusItem is null || _trayNodeItem is null || _trayToggleItem is null || _traySwitchNodeItem is null)
        {
            return;
        }

        _trayStatusItem.Text = $"当前状态：{(_isProxyEnabled ? "已开启" : "未开启")}";
        _trayNodeItem.Text = $"当前节点：{ActiveNodeName}";
        _trayToggleItem.Text = _isProxyEnabled ? "关闭代理" : "开启代理";
        _notifyIcon.Text = TruncateTrayText($"MyRay Lite - {(_isProxyEnabled ? "已连接" : "未连接")} - {ActiveNodeName}");

        _traySwitchNodeItem.DropDownItems.Clear();
        if (_nodes.Count == 0)
        {
            _traySwitchNodeItem.DropDownItems.Add("暂无节点", null, (_, _) => { }).Enabled = false;
            return;
        }

        foreach (var node in _nodes.Take(24))
        {
            var label = node.IsActive ? $"✓ {node.Name}" : node.Name;
            var item = new WinForms.ToolStripMenuItem(label)
            {
                Checked = node.IsActive
            };
            item.Click += (_, _) => Dispatcher.BeginInvoke(new Action(async () =>
            {
                await SetActiveNodeAsync(node, reconnect: _isProxyEnabled);
            }));
            _traySwitchNodeItem.DropDownItems.Add(item);
        }
    }

    private static string TruncateTrayText(string value)
    {
        return value.Length <= 63 ? value : value[..60] + "...";
    }

    private void Notify([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
