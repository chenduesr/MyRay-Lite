using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
    private readonly SystemProxyService _systemProxyService;
    private readonly DelayTestService _delayTestService;
    private readonly DiagnosticsService _diagnosticsService;
    private readonly DiagnosticPackageService _diagnosticPackageService;
    private readonly XrayDownloadService _downloadService;
    private readonly UpdateCheckService _updateCheckService;
    private readonly NetworkTrafficService _trafficService = new();
    private readonly DispatcherTimer _runtimeTimer;
    private readonly ObservableCollection<ProxyNode> _nodes = [];
    private readonly WinForms.NotifyIcon _notifyIcon;

    private AppSettings _settings = new();
    private string _currentPage = "Home";
    private bool _isProxyEnabled;
    private string _toastMessage = string.Empty;
    private CancellationTokenSource? _toastAutoCloseCts;
    private CancellationTokenSource? _settingsSaveCts;
    private string _subscriptionStatus = "未更新";
    private string _searchText = string.Empty;
    private string _nodeFilter = "全部";
    private string _logSearchText = string.Empty;
    private string _logLevelFilter = "全部";
    private string _logSourceFilter = "全部";
    private bool _logAutoScroll = true;
    private string _settingsSection = "Basic";
    private string _nodeSortKey = "Status";
    private bool _nodeSortAscending;
    private bool _isApplyingSettings;
    private bool _isTestingNodes;
    private bool _isConnecting;
    private bool _isExplicitExit;
    private double _testProgress;
    private string _testProgressDetail = "等待开始测速";
    private ProxyNode? _selectedNodeDetail;
    private string _dialogTitle = string.Empty;
    private string _dialogMessage = string.Empty;
    private string _dialogPrimaryText = "确定";
    private string _dialogSecondaryText = "取消";
    private bool _isDialogOpen;
    private string? _pendingInstallerPath;
    private ReleaseAsset? _pendingInstallerAsset;
    private WinForms.ToolStripMenuItem? _trayStatusItem;
    private WinForms.ToolStripMenuItem? _trayNodeItem;
    private WinForms.ToolStripMenuItem? _trayToggleItem;
    private WinForms.ToolStripMenuItem? _traySwitchNodeItem;
    private WinForms.ContextMenuStrip? _trayMenu;
    private string? _latestReleaseUrl;
    private DateTimeOffset? _connectedAt;
    private NetworkTrafficSnapshot? _lastTrafficSnapshot;
    private double _downloadBytesPerSecond;
    private double _uploadBytesPerSecond;
    private int _trafficSaveTick;
    private string _xrayCoreVersionText = "未检测";
    private string _xrayLatestVersionText = "未检查";
    private string _geoDataStatusText = "未检测";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        var httpClient = new HttpClient();
        var configBuilder = new XrayConfigBuilder();
        _log = new AppLogService(_paths);
        _store = new SettingsStore(_paths);
        _systemProxyService = new SystemProxyService(_paths, _log);
        _subscriptionService = new SubscriptionService(httpClient, new SubscriptionParser(), _store);
        _xrayService = new XrayProcessService(_paths, configBuilder, _log);
        _xrayService.UnexpectedExit += XrayService_UnexpectedExit;
        _delayTestService = new DelayTestService(_paths, configBuilder, _log);
        _diagnosticsService = new DiagnosticsService(_paths, configBuilder);
        _diagnosticPackageService = new DiagnosticPackageService(_paths);
        _downloadService = new XrayDownloadService(httpClient, _paths);
        _updateCheckService = new UpdateCheckService(httpClient, _log);
        _notifyIcon = CreateNotifyIcon();
        _runtimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _runtimeTimer.Tick += (_, _) => UpdateRuntimeMetrics();
        _runtimeTimer.Start();

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
    public ObservableCollection<LogLineItem> LogLines { get; } = [];

    public string CurrentPage
    {
        get => _currentPage;
        set
        {
            if (_currentPage == value) return;
            _currentPage = value;
            Notify();
            NotifyNavigation();
            BeginPageAnimation();
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
    public string XrayStatusText => _xrayService.IsRunning ? "运行中" : "未运行";
    public string SystemProxyStatusText => _isProxyEnabled ? "已启用" : "未启用";
    public string HomeStatusTitle => _isConnecting ? "正在连接" : _isProxyEnabled ? "代理运行中" : "代理未开启";
    public string HomeStatusDetail => _isConnecting
        ? "正在启动核心并设置系统代理，请稍等。"
        : _isProxyEnabled
            ? "Xray 与系统代理正在工作，流量会按当前模式处理。"
            : ActiveNode is null
                ? "请先更新订阅并选择节点，然后开启代理。"
                : "节点已准备好，点击下方按钮即可开启代理。";
    public string ConnectionFlowTitle => _isConnecting ? "连接中" : _isProxyEnabled ? "已连接" : "未连接";
    public string ConnectionFlowDetail => _isConnecting
        ? "正在启动 Xray Core → 写入代理端口 → 启用系统代理"
        : _isProxyEnabled
            ? $"{ActiveNodeName} · {ProxyModeText} · 已运行 {ConnectionDurationText}"
            : ActiveNode is null ? "先添加/更新订阅，再选择一个节点。" : "节点已就绪，可以直接开启代理。";
    public string ConnectionDurationText => _connectedAt is null ? "00:00:00" : (DateTimeOffset.Now - _connectedAt.Value).ToString(@"hh\:mm\:ss");
    public string CurrentDownloadSpeedText => $"↓ {FormatTrafficSpeed(_downloadBytesPerSecond)}";
    public string CurrentUploadSpeedText => $"↑ {FormatTrafficSpeed(_uploadBytesPerSecond)}";
    public string TodayTrafficText => FormatTrafficBytes(_settings.TodayDownloadBytes + _settings.TodayUploadBytes);
    public string TrafficHintText => _isProxyEnabled ? "基于本机网卡计数器估算" : "连接后开始统计";
    public string XrayCoreVersionText => _xrayCoreVersionText;
    public string XrayLatestVersionText => _xrayLatestVersionText;
    public string GeoDataStatusText => _geoDataStatusText;
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
    public string NodeFilterSummary => _nodeFilter == "全部" ? $"共 {NodeCount} 个节点" : $"{_nodeFilter} · {FilteredNodes.Count} 个";
    public bool HasLogs => LogLines.Count > 0;
    public string LogLineCountText => LogLines.Count == 0 ? "暂无日志" : $"{LogLines.Count} 行日志";
    public bool IsSearchEmpty => string.IsNullOrWhiteSpace(_searchText);
    public bool IsLogSearchEmpty => string.IsNullOrWhiteSpace(_logSearchText);

    public bool LogAutoScroll
    {
        get => _logAutoScroll;
        set
        {
            _logAutoScroll = value;
            Notify();
        }
    }

    public string SettingsSection
    {
        get => _settingsSection;
        set
        {
            if (_settingsSection == value) return;
            _settingsSection = value;
            Notify();
            BeginSettingsSectionAnimation();
        }
    }
    public string AppVersionText => $"v{GetCurrentVersionText()}";
    public string EmptyNodesTitle => _nodes.Count == 0 ? "暂无节点" : "没有匹配节点";
    public string EmptyNodesMessage => _nodes.Count == 0 ? "请在订阅页保存并更新订阅" : "换个关键词，或点击刷新重新加载列表";
    public string LastUpdateText => _settings.LastSubscriptionUpdate?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";
    public string DelayTestModeText => _settings.DelayTestMode switch
    {
        DelayTestMode.Tcp => "TCP 延迟",
        DelayTestMode.Http => "HTTP 延迟",
        DelayTestMode.Download => "下载测速",
        _ => "TCP 延迟"
    };

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
            Notify(nameof(HasToast));
            ScheduleToastAutoClose(value);
            if (!string.IsNullOrWhiteSpace(value))
            {
                BeginToastAnimation();
            }
        }
    }

    public bool HasToast => !string.IsNullOrWhiteSpace(_toastMessage);
    public bool IsTestingNodes
    {
        get => _isTestingNodes;
        set
        {
            _isTestingNodes = value;
            Notify();
            Notify(nameof(HasTestProgress));
        }
    }

    public bool HasTestProgress => IsTestingNodes || TestProgress > 0;

    public double TestProgress
    {
        get => _testProgress;
        set
        {
            _testProgress = value;
            Notify();
            Notify(nameof(TestProgressText));
            Notify(nameof(HasTestProgress));
        }
    }

    public string TestProgressText => TestProgress <= 0 ? "准备测速" : $"测速进度 {TestProgress:P0}";
    public string TestProgressDetail
    {
        get => _testProgressDetail;
        set
        {
            _testProgressDetail = value;
            Notify();
        }
    }

    public ProxyNode? SelectedNodeDetail
    {
        get => _selectedNodeDetail;
        set
        {
            _selectedNodeDetail = value;
            Notify();
            Notify(nameof(IsNodeDetailOpen));
            Notify(nameof(SelectedNodeEndpointText));
            Notify(nameof(SelectedNodeTransportText));
            Notify(nameof(SelectedNodeSubscriptionText));
            Notify(nameof(SelectedNodeLastTestText));
            if (value is not null)
            {
                BeginNodeDetailAnimation();
            }
        }
    }

    public bool IsNodeDetailOpen => SelectedNodeDetail is not null;
    public string SelectedNodeEndpointText => SelectedNodeDetail is null ? "—" : $"{SelectedNodeDetail.Address}:{SelectedNodeDetail.Port}";
    public string SelectedNodeTransportText => SelectedNodeDetail is null ? "—" : $"{SelectedNodeDetail.Network} / {SelectedNodeDetail.Security}";
    public string SelectedNodeSubscriptionText => BuildSubscriptionSourceText();
    public string SelectedNodeLastTestText => SelectedNodeDetail?.LastTested?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "未测速";
    public bool IsDialogOpen
    {
        get => _isDialogOpen;
        set
        {
            _isDialogOpen = value;
            Notify();
        }
    }

    public string DialogTitle
    {
        get => _dialogTitle;
        set
        {
            _dialogTitle = value;
            Notify();
        }
    }

    public string DialogMessage
    {
        get => _dialogMessage;
        set
        {
            _dialogMessage = value;
            Notify();
        }
    }

    public string DialogPrimaryText
    {
        get => _dialogPrimaryText;
        set
        {
            _dialogPrimaryText = value;
            Notify();
        }
    }

    public string DialogSecondaryText
    {
        get => _dialogSecondaryText;
        set
        {
            _dialogSecondaryText = value;
            Notify();
            Notify(nameof(HasDialogSecondary));
        }
    }

    public bool HasDialogSecondary => !string.IsNullOrWhiteSpace(DialogSecondaryText);

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
        try
        {
            _systemProxyService.RestoreStaleProxyIfNeeded();
            SynchronizeStartOnBootSetting();
        }
        catch (Exception ex)
        {
            _log.Warn($"同步系统设置失败：{ex.Message}");
        }

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

        ResetTrafficDateIfNeeded();
        RefreshXrayCoreInfo();
        ApplySettingsToControls();
        ApplyTheme();
        RefreshActiveFlags();
        RefreshFilteredNodes();
        RefreshLogLines();
        RefreshStatusProperties();
        CheckPendingUpdateMarker();

        if (_settings.AutoConnectOnLaunch && ActiveNode is not null && File.Exists(_xrayService.XrayExePath))
        {
            await ConnectActiveNodeAsync(enableSystemProxy: true);
        }

        if (_settings.AutoCheckUpdates)
        {
            _ = CheckForUpdatesOnLaunchAsync();
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
        AutoCheckUpdatesCheckBox.IsChecked = _settings.AutoCheckUpdates;
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
        DelayTestModeComboBox.SelectedIndex = _settings.DelayTestMode switch
        {
            DelayTestMode.Http => 1,
            DelayTestMode.Download => 2,
            _ => 0
        };
        _isApplyingSettings = false;
    }

    private WinForms.NotifyIcon CreateNotifyIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        _trayMenu = menu;
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
        menu.Items.Add("退出", null, (_, _) => Dispatcher.BeginInvoke(new Action(RequestExit)));
        menu.Opening += (_, _) => UpdateTrayMenu();
        ApplyTrayTheme(menu);

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
            _isConnecting = true;
            RefreshStatusProperties();
            SetToast("正在连接", ActiveNodeName);
            await _xrayService.StartAsync(ActiveNode!, _settings);
            if (enableSystemProxy)
            {
                SetToast("正在设置系统代理", $"HTTP {_settings.HttpPort} · SOCKS {_settings.SocksPort}");
                _systemProxyService.Enable(_settings);
            }

            _isProxyEnabled = true;
            _connectedAt = DateTimeOffset.Now;
            _lastTrafficSnapshot = _trafficService.Sample();
            _downloadBytesPerSecond = 0;
            _uploadBytesPerSecond = 0;
            SetToast("连接成功", $"{ActiveNodeName} · {ProxyModeText}");
            _notifyIcon.BalloonTipTitle = "MyRay Lite";
            _notifyIcon.BalloonTipText = $"已连接：{ActiveNodeName}";
            _notifyIcon.ShowBalloonTip(1000);
        }
        catch (Exception ex)
        {
            _xrayService.Stop();
            _systemProxyService.Disable();
            _isProxyEnabled = false;
            _connectedAt = null;
            _downloadBytesPerSecond = 0;
            _uploadBytesPerSecond = 0;
            SetToast("连接失败", ex.Message);
            RefreshLogLines();
        }
        finally
        {
            _isConnecting = false;
            RefreshStatusProperties();
        }
    }    private void XrayService_UnexpectedExit(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _systemProxyService.Disable();
            _isProxyEnabled = false;
            SetToast("连接已断开", "Xray 意外退出，系统代理已恢复");
            RefreshLogLines();
            RefreshStatusProperties();
        }));
    }
    private void DisableProxy()
    {
        _xrayService.Stop();
        _systemProxyService.Disable();
        _isProxyEnabled = false;
        SetToast("代理已关闭", "系统代理已恢复");
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
        SetToast("已选择节点", node.Name);

        if (reconnect)
        {
            DisableProxy();
            await ConnectActiveNodeAsync(enableSystemProxy: true);
        }
    }

    private async void SaveSubscription_Click(object sender, RoutedEventArgs e)
    {
        _settings.SubscriptionUrl = SubscriptionUrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(_settings.SubscriptionUrl))
        {
            await ClearSubscriptionNodesAsync("订阅已清空，节点列表已清空。");
            return;
        }

        await _store.SaveSettingsAsync(_settings);
        ToastMessage = "订阅地址已保存。";
    }

    private async void UpdateSubscription_Click(object sender, RoutedEventArgs e)
    {
        _settings.SubscriptionUrl = SubscriptionUrlTextBox.Text.Trim();
        await _store.SaveSettingsAsync(_settings);
        if (string.IsNullOrWhiteSpace(_settings.SubscriptionUrl))
        {
            await ClearSubscriptionNodesAsync("订阅地址为空，节点列表已清空。");
            return;
        }

        SetToast("正在更新订阅");
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
        SetToast("订阅更新完成", $"{snapshot.Nodes.Count} 个节点 · {snapshot.StatusText}");
        _log.Info($"订阅更新完成：{snapshot.StatusText}，节点 {snapshot.Nodes.Count} 个。");
        RefreshActiveFlags();
        RefreshFilteredNodes();
        RefreshStatusProperties();
    }

    private async Task ClearSubscriptionNodesAsync(string message)
    {
        if (_isProxyEnabled)
        {
            DisableProxy();
        }

        _nodes.Clear();
        _settings.ActiveNodeId = null;
        _settings.LastSubscriptionUpdate = null;
        SelectedNodeDetail = null;
        await _store.SaveSettingsAsync(_settings);
        await _store.SaveNodesAsync(_nodes);
        SubscriptionStatus = "订阅地址为空";
        ToastMessage = message;
        _log.Info(message);
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
        SetToast("正在测速", $"{_nodes.Count} 个节点");
        IsTestingNodes = true;
        TestProgress = 0;
        TestProgressDetail = "正在准备批量测速...";
        var tested = 0;
        var total = Math.Max(1, _nodes.Count);
        try
        {
            await _delayTestService.TestManyAsync(_nodes, _settings, node =>
            {
                Dispatcher.Invoke(() =>
                {
                    tested++;
                    TestProgress = Math.Clamp(tested / (double)total, 0, 1);
                    var state = node.Status == NodeStatus.Available ? node.DisplayDelay : node.DisplayFailureReason;
                    TestProgressDetail = $"{tested}/{total}  {node.Name}  {state}";
                    RefreshFilteredNodes();
                    RefreshStatusProperties();
                });
            });
            await _store.SaveNodesAsync(_nodes);
            var available = _nodes.Count(node => node.Status == NodeStatus.Available);
            var unavailable = _nodes.Count(node => node.Status == NodeStatus.Unavailable);
            var unsupported = _nodes.Count(node => !node.IsSupportedByXray);
            var testSummary = unsupported > 0
                ? $"可用 {available}，不可用 {unavailable}，不支持 {unsupported}"
                : $"可用 {available}，不可用 {unavailable}";
            SetToast($"{DelayTestModeText}完成", testSummary);
            TestProgress = 1;
            TestProgressDetail = $"完成：可用 {available}，不可用 {unavailable}，不支持 {unsupported}。";
            RefreshLogLines();
        }
        catch (Exception ex)
        {
            SetToast("测速失败", ex.Message);
            TestProgressDetail = $"测速失败：{ex.Message}";
            _log.Error(ToastMessage);
            ShowDialog("测速失败", ex.Message, "知道了", string.Empty);
        }
        finally
        {
            IsTestingNodes = false;
        }
    }

    private async Task TestNodeAsync(ProxyNode node)
    {
        node.Status = NodeStatus.Testing;
        RefreshFilteredNodes();
        RefreshStatusProperties();

        var delay = await _delayTestService.TestProxyDelayAsync(node, _settings);
        await _store.SaveNodesAsync(_nodes);

        RefreshFilteredNodes();
        RefreshStatusProperties();
        if (delay is null)
        {
            SetToast("节点不可用", node.DisplayFailureReason);
        }
        else
        {
            SetToast("测速完成", $"{node.DisplayTestType} {node.DisplayDelay}");
        }
        RefreshLogLines();
    }

    private void RefreshNodes_Click(object sender, RoutedEventArgs e)
    {
        RefreshFilteredNodes();
        ToastMessage = "节点列表已刷新。";
    }

    private void SortNodes_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: string sortKey })
        {
            return;
        }

        if (string.Equals(_nodeSortKey, sortKey, StringComparison.OrdinalIgnoreCase))
        {
            _nodeSortAscending = !_nodeSortAscending;
        }
        else
        {
            _nodeSortKey = sortKey;
            _nodeSortAscending = sortKey != "Status";
        }

        RefreshFilteredNodes();
    }

    private void NodeFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string filter })
        {
            _nodeFilter = filter;
            RefreshFilteredNodes();
            SetToast("节点筛选", NodeFilterSummary);
        }
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
        _settings.AutoCheckUpdates = AutoCheckUpdatesCheckBox.IsChecked == true;
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

    private void AdvancedTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isApplyingSettings || !IsLoaded)
        {
            return;
        }

        ReadAdvancedSettingsFromControls();
        ScheduleSettingsSave();
    }

    private async void ApplyNetworkSettings_Click(object sender, RoutedEventArgs e)
    {
        CancelPendingSettingsSave();
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

    private void ValidateRoutingRules_Click(object sender, RoutedEventArgs e)
    {
        ReadAdvancedSettingsFromControls();
        var errors = new List<string>();
        ValidateRuleLines("直连域名", _settings.DirectDomains, false, errors);
        ValidateRuleLines("直连 IP", _settings.DirectIps, true, errors);
        ValidateRuleLines("代理域名", _settings.ProxyDomains, false, errors);
        ValidateRuleLines("代理 IP", _settings.ProxyIps, true, errors);
        ValidateRuleLines("阻止域名", _settings.BlockDomains, false, errors);
        ValidateRuleLines("阻止 IP", _settings.BlockIps, true, errors);

        if (errors.Count == 0)
        {
            SetToast("规则校验通过", "可以保存并应用网络设置");
            return;
        }

        ShowDialog("规则格式需要检查", string.Join(Environment.NewLine, errors.Take(8)), "知道了", string.Empty);
    }

    private static void ValidateRuleLines(string label, string value, bool ipRule, List<string> errors)
    {
        var lineNumber = 0;
        foreach (var raw in value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.StartsWith("#") || line.Length == 0)
            {
                continue;
            }

            var valid = ipRule
                ? line.Contains('.') || line.Contains(':') || line.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase)
                : line.StartsWith("domain:", StringComparison.OrdinalIgnoreCase) ||
                  line.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase) ||
                  line.StartsWith("regexp:", StringComparison.OrdinalIgnoreCase) ||
                  line.StartsWith("full:", StringComparison.OrdinalIgnoreCase) ||
                  line.StartsWith("keyword:", StringComparison.OrdinalIgnoreCase) ||
                  line.Contains('.');
            if (!valid)
            {
                errors.Add($"{label} 第 {lineNumber} 行格式可疑：{line}");
            }
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

        SynchronizeStartOnBootSetting();

        ToastMessage = "开机自启设置已保存。";
    }

    private void SynchronizeStartOnBootSetting()
    {
        var appPath = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(appPath))
        {
            _systemProxyService.SetStartOnBoot(_settings.StartOnBoot, appPath);
        }
    }

    private void PortTextBox_TextChanged(object sender, TextChangedEventArgs e)
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

        ScheduleSettingsSave();
    }

    private void DelayTestUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _settings.DelayTestUrl = string.IsNullOrWhiteSpace(DelayTestUrlTextBox.Text)
            ? "http://www.gstatic.com/generate_204"
            : DelayTestUrlTextBox.Text.Trim();
        ScheduleSettingsSave();
    }

    private async void DelayTestModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isApplyingSettings)
        {
            return;
        }

        _settings.DelayTestMode = DelayTestModeComboBox.SelectedIndex switch
        {
            1 => DelayTestMode.Http,
            2 => DelayTestMode.Download,
            _ => DelayTestMode.Tcp
        };

        if (_settings.DelayTestMode == DelayTestMode.Download &&
            (string.IsNullOrWhiteSpace(DelayTestUrlTextBox.Text) || DelayTestUrlTextBox.Text.Contains("generate_204", StringComparison.OrdinalIgnoreCase)))
        {
            DelayTestUrlTextBox.Text = "https://speed.cloudflare.com/__down?bytes=1048576";
            _settings.DelayTestUrl = DelayTestUrlTextBox.Text;
        }

        await _store.SaveSettingsAsync(_settings);
        Notify(nameof(DelayTestModeText));
        ToastMessage = $"测速模式已切换为 {DelayTestModeText}。";
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchTextBox.Text.Trim();
        Notify(nameof(IsSearchEmpty));
        RefreshFilteredNodes();
    }

    private void LogSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string source })
        {
            _logSourceFilter = source;
            RefreshLogLines();
            SetToast("日志视图", source);
        }
    }
    private void LogSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _logSearchText = LogSearchTextBox.Text.Trim();
        Notify(nameof(IsLogSearchEmpty));
        RefreshLogLines();
    }

    private void LogLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _logLevelFilter = (LogLevelComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "全部";
        RefreshLogLines();
    }

    private void LogAutoScroll_Click(object sender, RoutedEventArgs e)
    {
        LogAutoScroll = LogAutoScrollCheckBox.IsChecked == true;
        if (LogAutoScroll)
        {
            ScrollLogsToEnd();
        }
    }

    private void CopyLogs_Click(object sender, RoutedEventArgs e)
    {
        if (LogLines.Count == 0)
        {
            SetToast("暂无日志", "没有可复制的日志内容");
            return;
        }

        WpfClipboard.SetText(string.Join(Environment.NewLine, LogLines.Select(line => line.Text)));
        SetToast("日志已复制", $"已复制 {LogLines.Count} 行");
    }

    private void CopyErrorLogs_Click(object sender, RoutedEventArgs e)
    {
        var errorLines = LogLines
            .Where(line => string.Equals(line.Level, "ERROR", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(line.Level, "WARN", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.Text)
            .ToList();

        if (errorLines.Count == 0)
        {
            SetToast("没有错误日志", "当前筛选结果里没有 ERROR/WARN");
            return;
        }

        WpfClipboard.SetText(string.Join(Environment.NewLine, errorLines));
        SetToast("错误日志已复制", $"已复制 {errorLines.Count} 行");
    }

    private void SettingsSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string section })
        {
            SettingsSection = section;
        }
    }
    private async void DownloadXray_Click(object sender, RoutedEventArgs e)
    {
        await UpdateXrayCoreAsync();
    }

    private async void CheckXrayCore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetToast("正在检查 Xray-core");
            _xrayLatestVersionText = await _downloadService.CheckLatestVersionAsync();
            Notify(nameof(XrayLatestVersionText));
            SetToast("Xray-core 检查完成", $"最新版本：{_xrayLatestVersionText}");
        }
        catch (Exception ex)
        {
            SetToast("检查 Xray-core 失败", ex.Message);
            _log.Error(ToastMessage);
            RefreshLogLines();
        }
    }

    private async void UpdateXrayCore_Click(object sender, RoutedEventArgs e)
    {
        await UpdateXrayCoreAsync();
    }

    private async void UpdateGeoData_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetToast("正在更新 geoip/geosite 数据");
            var progress = new Progress<double>(value => SetToast("正在更新 geo 数据", value.ToString("P0")));
            var directory = await _downloadService.DownloadLatestGeoDataAsync(progress);
            RefreshXrayCoreInfo();
            SetToast("geo 数据已更新", directory);
            _log.Info($"geoip/geosite 数据已更新到 {directory}");
        }
        catch (Exception ex)
        {
            SetToast("geo 数据更新失败", ex.Message);
            _log.Error(ToastMessage);
            RefreshLogLines();
        }
    }

    private async Task UpdateXrayCoreAsync()
    {
        try
        {
            if (_isProxyEnabled)
            {
                DisableProxy();
            }

            SetToast("正在下载 Xray-core...");
            var progress = new Progress<double>(value => SetToast("正在下载 Xray-core", value.ToString("P0")));
            var directory = await _downloadService.DownloadLatestWindowsX64Async(progress);
            RefreshXrayCoreInfo();
            SetToast("Xray-core 已更新", directory);
            _log.Info($"Xray-core 已更新到 {directory}");
        }
        catch (Exception ex)
        {
            SetToast("Xray-core 更新失败", ex.Message);
            _log.Error(ToastMessage);
            RefreshLogLines();
        }
    }
    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ToastMessage = "正在检查更新...";
            var result = await _updateCheckService.CheckLatestAsync(GetCurrentVersion());
            HandleUpdateResult(result, showLatestDialog: true);
        }
        catch (Exception ex)
        {
            ToastMessage = $"检查更新失败：{ex.Message}";
            _log.Error(ToastMessage);
            ShowDialog("检查更新失败", ex.Message, "知道了", string.Empty);
            RefreshLogLines();
        }
    }

    private async Task CheckForUpdatesOnLaunchAsync()
    {
        try
        {
            await Task.Delay(1800);
            var result = await _updateCheckService.CheckLatestAsync(GetCurrentVersion());
            HandleUpdateResult(result, showLatestDialog: false);
        }
        catch (Exception ex)
        {
            _log.Warn($"启动时检查更新失败：{ex.Message}");
        }
    }

    private void HandleUpdateResult(UpdateCheckResult result, bool showLatestDialog)
    {
        _latestReleaseUrl = result.HasUpdate ? result.ReleaseUrl : null;

        if (!result.HasUpdate)
        {
            if (showLatestDialog)
            {
                ToastMessage = result.Message;
            }
            else
            {
                _log.Info(result.Message);
            }

            return;
        }

        ToastMessage = result.Message;
        _pendingInstallerAsset = result.InstallerAsset;
        _pendingInstallerPath = null;

        if (result.InstallerAsset is null)
        {
            ShowDialog("发现新版本", $"{result.Message}\n\n没有找到安装包资产，可以打开 Release 页面手动下载。", "打开页面", "稍后");
            return;
        }

        ShowDialog(
            "发现新版本",
            $"{result.Message}\n\n可下载：{result.InstallerAsset.Name}\n大小：{FormatBytes(result.InstallerAsset.Size)}\n\n点击“下载更新”会下载新版安装包，下载完成后可立即安装。",
            "下载更新",
            "稍后");
    }

    private async Task DownloadPendingUpdateAsync()
    {
        if (_pendingInstallerAsset is null)
        {
            return;
        }

        try
        {
            IsDialogOpen = false;
            ToastMessage = $"正在下载 {_pendingInstallerAsset.Name}...";
            var progress = new Progress<double>(value => ToastMessage = $"正在下载更新 {value:P0}");
            _pendingInstallerPath = await _updateCheckService.DownloadInstallerAsync(_pendingInstallerAsset, _paths, progress);
            _pendingInstallerAsset = null;
            ShowDialog(
                "更新已下载",
                $"安装包已下载到：\n{_pendingInstallerPath}\n\n点击“立即安装”后会静默运行安装程序，应用可能会自动关闭。",
                "立即安装",
                "稍后");
        }
        catch (Exception ex)
        {
            ToastMessage = $"下载更新失败：{ex.Message}";
            _log.Error(ToastMessage);
            ShowDialog(
                "下载更新失败",
                $"新版安装包下载失败，当前版本未受影响。\n\n你可以稍后重试，或到 Release 页面手动下载。\n\n错误：{ex.Message}",
                "知道了",
                string.Empty);
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

    private void CreateDiagnosticPackage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = _diagnosticPackageService.CreatePackage(_settings, _nodes, AppVersionText);
            WpfClipboard.SetText(file);
            ToastMessage = "诊断包已生成，路径已复制。";
            _log.Info($"诊断包已生成：{file}");
            RefreshLogLines();
            ShowDialog("诊断包已生成", $"已生成脱敏诊断包，并复制路径：\n{file}", "打开目录", "关闭");
            _pendingInstallerPath = null;
        }
        catch (Exception ex)
        {
            ToastMessage = $"诊断包生成失败：{ex.Message}";
            _log.Error(ToastMessage);
            ShowDialog("诊断包生成失败", ex.Message, "知道了", string.Empty);
        }
    }

    private void RunDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var issues = _diagnosticsService.Run(_settings, ActiveNode);
        foreach (var issue in issues)
        {
            var level = issue.Severity == DiagnosticSeverity.Error
                ? "ERROR"
                : issue.Severity == DiagnosticSeverity.Warning ? "WARN" : "INFO";
            _log.Write(level, issue.ToString());
        }

        RefreshLogLines();
        var errors = issues.Count(issue => issue.Severity == DiagnosticSeverity.Error);
        var warnings = issues.Count(issue => issue.Severity == DiagnosticSeverity.Warning);
        ToastMessage = errors > 0
            ? $"诊断完成：发现 {errors} 个错误，{warnings} 个警告。"
            : warnings > 0 ? $"诊断完成：发现 {warnings} 个警告。" : "诊断通过，未发现明显问题。";
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
        HideToTray("已隐藏到后台，右键托盘图标可以退出。");
    }

    private void WindowDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ViewNodeDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string nodeId })
        {
            SelectedNodeDetail = _nodes.FirstOrDefault(node => node.Id == nodeId);
        }
    }

    private async void ConnectSelectedNode_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNodeDetail is null)
        {
            return;
        }

        await SetActiveNodeAsync(SelectedNodeDetail, reconnect: _isProxyEnabled);
        if (!_isProxyEnabled)
        {
            CurrentPage = "Home";
        }
    }

    private async void TestSelectedNode_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNodeDetail is not null)
        {
            await TestNodeAsync(SelectedNodeDetail);
            Notify(nameof(SelectedNodeLastTestText));
        }
    }

    private void CopyNodeName_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNodeDetail is null)
        {
            return;
        }

        WpfClipboard.SetText(SelectedNodeDetail.Name);
        SetToast("节点名称已复制", SelectedNodeDetail.Name);
    }
    private async void SaveNodeDetails_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNodeDetail is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedNodeDetail.Name) ||
            string.IsNullOrWhiteSpace(SelectedNodeDetail.Address) ||
            SelectedNodeDetail.Port is < 1 or > 65535)
        {
            ShowDialog("节点信息不完整", "请确认节点名称、地址和端口正确。端口范围为 1-65535。", "知道了", string.Empty);
            return;
        }

        await _store.SaveNodesAsync(_nodes);
        RefreshFilteredNodes();
        RefreshStatusProperties();
        UpdateTrayMenu();

        if (_isProxyEnabled && SelectedNodeDetail.IsActive)
        {
            DisableProxy();
            await ConnectActiveNodeAsync(enableSystemProxy: true);
            ToastMessage = "节点已保存，并重新连接当前节点。";
        }
        else
        {
            ToastMessage = "节点已保存。";
        }
    }

    private void CloseNodeDetails_Click(object sender, RoutedEventArgs e)
    {
        SelectedNodeDetail = null;
    }

    private async void DialogPrimary_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingInstallerAsset is not null)
        {
            await DownloadPendingUpdateAsync();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_pendingInstallerPath) && File.Exists(_pendingInstallerPath))
        {
            try
            {
                WritePendingUpdateMarker(_pendingInstallerPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = _pendingInstallerPath,
                    Arguments = "/VERYSILENT /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                    UseShellExecute = true
                });
                _isExplicitExit = true;
                Close();
            }
            catch (Exception ex)
            {
                _log.Error($"启动更新安装包失败：{ex.Message}");
                ShowDialog(
                    "无法启动安装包",
                    $"安装包启动失败，当前版本未受影响。\n\n你可以手动运行：\n{_pendingInstallerPath}\n\n错误：{ex.Message}",
                    "知道了",
                    string.Empty);
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(_latestReleaseUrl) && DialogPrimaryText.Contains("页面", StringComparison.Ordinal))
        {
            Process.Start(new ProcessStartInfo { FileName = _latestReleaseUrl, UseShellExecute = true });
        }
        else if (DialogPrimaryText.Contains("打开", StringComparison.Ordinal) && Directory.Exists(_paths.DiagnosticDirectory))
        {
            Process.Start(new ProcessStartInfo { FileName = _paths.DiagnosticDirectory, UseShellExecute = true });
        }

        IsDialogOpen = false;
    }

    private void DialogSecondary_Click(object sender, RoutedEventArgs e)
    {
        IsDialogOpen = false;
    }

    private void ShowDialog(string title, string message, string primaryText, string secondaryText)
    {
        DialogTitle = title;
        DialogMessage = message;
        DialogPrimaryText = string.IsNullOrWhiteSpace(primaryText) ? "确定" : primaryText;
        DialogSecondaryText = secondaryText;
        IsDialogOpen = true;
        BeginDialogAnimation();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "未知";
        }

        var mb = bytes / 1024d / 1024d;
        return $"{mb:0.0} MB";
    }

    private static string FormatTrafficBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static string FormatTrafficSpeed(double bytesPerSecond)
    {
        return $"{FormatTrafficBytes((long)Math.Max(0, bytesPerSecond))}/s";
    }
    private void WritePendingUpdateMarker(string installerPath)
    {
        _paths.Ensure();
        var marker = new PendingUpdateMarker(
            GetCurrentVersion().ToString(),
            installerPath,
            DateTimeOffset.Now);
        File.WriteAllText(_paths.PendingUpdateFile, JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void CheckPendingUpdateMarker()
    {
        try
        {
            if (!File.Exists(_paths.PendingUpdateFile))
            {
                return;
            }

            var marker = JsonSerializer.Deserialize<PendingUpdateMarker>(File.ReadAllText(_paths.PendingUpdateFile));
            File.Delete(_paths.PendingUpdateFile);
            if (marker is null || !Version.TryParse(marker.PreviousVersion, out var previousVersion))
            {
                return;
            }

            var currentVersion = GetCurrentVersion();
            if (currentVersion <= previousVersion)
            {
                ShowDialog(
                    "更新可能未完成",
                    $"上次更新安装似乎没有完成，当前仍是 {currentVersion}。\n\n旧版本和配置已保留，可以继续使用。你也可以重新检查更新，或手动运行安装包：\n{marker.InstallerPath}",
                    "知道了",
                    string.Empty);
                _log.Warn($"更新可能未完成，当前版本 {currentVersion}，上次安装包：{marker.InstallerPath}");
            }
            else
            {
                _log.Info($"更新完成：{previousVersion} -> {currentVersion}");
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"读取更新状态失败：{ex.Message}");
        }
    }

    public sealed class LogLineItem
    {
        public string Text { get; init; } = string.Empty;
        public string Level { get; init; } = "INFO";
        public MediaBrush Foreground => Level switch
        {
            "ERROR" => Solid("#EF4444"),
            "WARN" => Solid("#F97316"),
            "XRAY" => Solid("#7C3AED"),
            _ => Solid("#263445")
        };

        public MediaBrush Background => Level switch
        {
            "ERROR" => Solid("#FFF5F5"),
            "WARN" => Solid("#FFF7ED"),
            "XRAY" => Solid("#F6F1FF"),
            _ => Solid("#FFFFFF")
        };

        public MediaBrush BadgeBackground => Level switch
        {
            "ERROR" => Solid("#FEE2E2"),
            "WARN" => Solid("#FFEDD5"),
            "XRAY" => Solid("#EDE9FE"),
            _ => Solid("#EAF3FF")
        };

        public MediaBrush BadgeForeground => Level switch
        {
            "ERROR" => Solid("#DC2626"),
            "WARN" => Solid("#EA580C"),
            "XRAY" => Solid("#6D28D9"),
            _ => Solid("#0875F8")
        };

        public static LogLineItem From(string line)
        {
            var level = line.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) || line.Contains("error", StringComparison.OrdinalIgnoreCase)
                ? "ERROR"
                : line.Contains("[WARN]", StringComparison.OrdinalIgnoreCase) || line.Contains("warning", StringComparison.OrdinalIgnoreCase)
                    ? "WARN"
                    : line.Contains("xray", StringComparison.OrdinalIgnoreCase) || line.StartsWith("=====") || !line.StartsWith("[")
                        ? "XRAY"
                        : "INFO";

            return new LogLineItem { Text = line, Level = level };
        }

        private static MediaSolidColorBrush Solid(string color)
        {
            var brush = new MediaSolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
    }
private sealed record PendingUpdateMarker(string PreviousVersion, string InstallerPath, DateTimeOffset StartedAt);

    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void HideToTray(string? balloonText = null)
    {
        Hide();

        if (!string.IsNullOrWhiteSpace(balloonText))
        {
            _notifyIcon.BalloonTipTitle = "MyRay Lite";
            _notifyIcon.BalloonTipText = balloonText;
            _notifyIcon.ShowBalloonTip(1200);
        }
    }

    private async void RequestExit()
    {
        CancelPendingSettingsSave();
        try
        {
            await _store.SaveSettingsAsync(_settings);
        }
        catch (Exception ex)
        {
            _log.Warn($"退出前保存设置失败：{ex.Message}");
        }

        _isExplicitExit = true;
        Close();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isExplicitExit)
        {
            e.Cancel = true;
            HideToTray("已隐藏到后台，右键托盘图标可以退出。");
            return;
        }

        CancelPendingSettingsSave();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _xrayService.Stop();
        _systemProxyService.Disable();
    }

    private void UpdateRuntimeMetrics()
    {
        Notify(nameof(ConnectionDurationText));
        Notify(nameof(ConnectionFlowDetail));

        if (!_isProxyEnabled)
        {
            if (_downloadBytesPerSecond != 0 || _uploadBytesPerSecond != 0)
            {
                _downloadBytesPerSecond = 0;
                _uploadBytesPerSecond = 0;
                NotifyTrafficProperties();
            }
            return;
        }

        ResetTrafficDateIfNeeded();
        var current = _trafficService.Sample();
        if (_lastTrafficSnapshot is not null)
        {
            var seconds = Math.Max(0.1, (current.Timestamp - _lastTrafficSnapshot.Timestamp).TotalSeconds);
            var downloadDelta = Math.Max(0, current.DownloadBytes - _lastTrafficSnapshot.DownloadBytes);
            var uploadDelta = Math.Max(0, current.UploadBytes - _lastTrafficSnapshot.UploadBytes);
            _downloadBytesPerSecond = downloadDelta / seconds;
            _uploadBytesPerSecond = uploadDelta / seconds;
            _settings.TodayDownloadBytes += downloadDelta;
            _settings.TodayUploadBytes += uploadDelta;
            _trafficSaveTick++;
            if (_trafficSaveTick >= 20)
            {
                _trafficSaveTick = 0;
                _ = _store.SaveSettingsAsync(_settings);
            }
        }

        _lastTrafficSnapshot = current;
        NotifyTrafficProperties();
    }

    private void NotifyTrafficProperties()
    {
        Notify(nameof(CurrentDownloadSpeedText));
        Notify(nameof(CurrentUploadSpeedText));
        Notify(nameof(TodayTrafficText));
        Notify(nameof(TrafficHintText));
    }

    private void ResetTrafficDateIfNeeded()
    {
        var today = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        if (string.Equals(_settings.TrafficDate, today, StringComparison.Ordinal))
        {
            return;
        }

        _settings.TrafficDate = today;
        _settings.TodayUploadBytes = 0;
        _settings.TodayDownloadBytes = 0;
    }

    private void RefreshXrayCoreInfo()
    {
        _xrayCoreVersionText = _downloadService.GetInstalledVersionText();
        _geoDataStatusText = _downloadService.GetGeoDataStatusText();
        Notify(nameof(XrayCoreVersionText));
        Notify(nameof(GeoDataStatusText));
    }

    private string BuildSubscriptionSourceText()
    {
        if (string.IsNullOrWhiteSpace(_settings.SubscriptionUrl))
        {
            return "本地/手动节点";
        }

        try
        {
            var first = _settings.SubscriptionUrl
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? _settings.SubscriptionUrl;
            return Uri.TryCreate(first.Trim(), UriKind.Absolute, out var uri) ? uri.Host : "当前订阅";
        }
        catch
        {
            return "当前订阅";
        }
    }
    private void RefreshFilteredNodes()
    {
        FilteredNodes.Clear();
        foreach (var node in SortNodes(_nodes.Where(MatchesSearch)))
        {
            FilteredNodes.Add(node);
        }

        Notify(nameof(FilteredNodes));
        Notify(nameof(HasNodes));
        Notify(nameof(NodeCount));
        Notify(nameof(NodeFilterSummary));
        Notify(nameof(EmptyNodesTitle));
        Notify(nameof(EmptyNodesMessage));
    }

    private IEnumerable<ProxyNode> SortNodes(IEnumerable<ProxyNode> nodes)
    {
        return _nodeSortKey switch
        {
            "Name" => _nodeSortAscending
                ? nodes.OrderBy(node => node.Name, StringComparer.CurrentCultureIgnoreCase)
                : nodes.OrderByDescending(node => node.Name, StringComparer.CurrentCultureIgnoreCase),
            "Delay" => _nodeSortAscending
                ? nodes.OrderBy(node => node.DelayMs ?? int.MaxValue)
                : nodes.OrderByDescending(node => node.DelayMs ?? -1),
            _ => _nodeSortAscending
                ? nodes.OrderBy(node => node.Status).ThenBy(node => node.DelayMs ?? int.MaxValue)
                : nodes.OrderByDescending(node => node.Status).ThenBy(node => node.DelayMs ?? int.MaxValue)
        };
    }

    private void RefreshLogLines()
    {
        LogLines.Clear();
        foreach (var line in _log.ReadRecentLines().Where(MatchesLogFilter))
        {
            LogLines.Add(LogLineItem.From(line));
        }

        Notify(nameof(LogLines));
        Notify(nameof(HasLogs));
        Notify(nameof(LogLineCountText));
        if (LogAutoScroll)
        {
            ScrollLogsToEnd();
        }
    }

    private bool MatchesLogFilter(string line)
    {
        if (!string.IsNullOrWhiteSpace(_logSearchText) && !line.Contains(_logSearchText, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_logSourceFilter == "应用" && (line.StartsWith("=====") || line.Contains("xray", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("[")))
        {
            return false;
        }

        if (_logSourceFilter == "Xray" && line.StartsWith("[") && !line.Contains("xray", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _logLevelFilter switch
        {
            "ERROR" => line.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) || line.Contains("error", StringComparison.OrdinalIgnoreCase),
            "WARN" => line.Contains("[WARN]", StringComparison.OrdinalIgnoreCase) || line.Contains("warning", StringComparison.OrdinalIgnoreCase),
            "INFO" => line.Contains("[INFO]", StringComparison.OrdinalIgnoreCase),
            "XRAY" => line.Contains("xray", StringComparison.OrdinalIgnoreCase) || line.StartsWith("=====") || !line.StartsWith("["),
            _ => true
        };
    }
    private void ScrollLogsToEnd()
    {
        Dispatcher.BeginInvoke(new Action(() => LogsScrollViewer?.ScrollToEnd()), DispatcherPriority.Background);
    }
    private bool MatchesSearch(ProxyNode node)
    {
        if (!MatchesNodeFilter(node))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(_searchText)
            || node.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || node.Address.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || node.Raw.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesNodeFilter(ProxyNode node)
    {
        return _nodeFilter switch
        {
            "可用" => node.Status == NodeStatus.Available,
            "不可用" => node.Status == NodeStatus.Unavailable || !node.IsSupportedByXray,
            "低延迟" => node.Status == NodeStatus.Available && node.DelayMs is > 0 and <= 200,
            "当前" => node.IsActive,
            _ => true
        };
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
        Notify(nameof(XrayStatusText));
        Notify(nameof(SystemProxyStatusText));
        Notify(nameof(HomeStatusTitle));
        Notify(nameof(HomeStatusDetail));
        Notify(nameof(HasActiveNode));
        Notify(nameof(ProxyModeText));
        Notify(nameof(NodeCount));
        Notify(nameof(LastUpdateText));
        Notify(nameof(DelayTestModeText));
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
        SetBrush("LogTextBrush", dark ? "#DCE7F5" : "#263445");
        if (_trayMenu is not null)
        {
            ApplyTrayTheme(_trayMenu);
        }
    }

    private void SetBrush(string key, string color)
    {
        var next = new MediaSolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(color));
        if (Resources[key] is MediaSolidColorBrush brush)
        {
            if (!brush.IsFrozen)
            {
                brush.Color = next.Color;
                return;
            }
        }

        Resources[key] = next;
    }

    private void BeginIntroAnimation()
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(110))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void BeginDialogAnimation()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            DialogCard.Opacity = 0;
            DialogCardTransform.Y = 10;
            DialogCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
            DialogCardTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void BeginPageAnimation()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            PageHost.Opacity = 0;
            PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }), DispatcherPriority.Loaded);
    }

    private void BeginSettingsSectionAnimation()
    {
        if (!string.Equals(CurrentPage, "Settings", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        BeginPageAnimation();
    }

    private void BeginNodeDetailAnimation()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            NodeDetailPanel.Opacity = 0;
            NodeDetailTransform.X = 22;
            NodeDetailPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
            NodeDetailTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }), DispatcherPriority.Loaded);
    }

    private void BeginToastAnimation()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ToastCard.Opacity = 0;
            ToastTransform.Y = -8;
            ToastCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
            ToastTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }), DispatcherPriority.Loaded);
    }
    private void SetToast(string title, string? detail = null)
    {
        ToastMessage = string.IsNullOrWhiteSpace(detail)
            ? title
            : $"{title} · {detail}";
    }
    private void ScheduleToastAutoClose(string message)
    {
        _toastAutoCloseCts?.Cancel();
        _toastAutoCloseCts?.Dispose();
        _toastAutoCloseCts = null;

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _toastAutoCloseCts = cts;
        _ = AutoCloseToastAsync(message, cts.Token);
    }

    private void ScheduleSettingsSave()
    {
        _settingsSaveCts?.Cancel();
        var cts = new CancellationTokenSource();
        _settingsSaveCts = cts;
        _ = SaveSettingsAfterDelayAsync(cts);
    }

    private void CancelPendingSettingsSave()
    {
        _settingsSaveCts?.Cancel();
        _settingsSaveCts = null;
    }

    private async Task SaveSettingsAfterDelayAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), cts.Token);
            await _store.SaveSettingsAsync(_settings, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log.Error($"自动保存设置失败：{ex.Message}");
            ToastMessage = "设置自动保存失败，请重试。";
        }
        finally
        {
            if (ReferenceEquals(_settingsSaveCts, cts))
            {
                _settingsSaveCts = null;
            }

            cts.Dispose();
        }
    }

    private async Task AutoCloseToastAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            await Dispatcher.InvokeAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested && string.Equals(_toastMessage, message, StringComparison.Ordinal))
                {
                    ToastMessage = string.Empty;
                }
            });
        }
        catch (TaskCanceledException)
        {
        }
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

        if (_trayMenu is not null)
        {
            ApplyTrayTheme(_trayMenu);
        }
    }

    private void ApplyTrayTheme(WinForms.ContextMenuStrip menu)
    {
        var dark = _settings.DarkMode;
        var back = dark ? System.Drawing.Color.FromArgb(23, 32, 51) : System.Drawing.Color.White;
        var fore = dark ? System.Drawing.Color.FromArgb(238, 243, 250) : System.Drawing.Color.FromArgb(17, 24, 39);
        var muted = dark ? System.Drawing.Color.FromArgb(159, 176, 198) : System.Drawing.Color.FromArgb(100, 116, 139);

        menu.BackColor = back;
        menu.ForeColor = fore;
        foreach (WinForms.ToolStripItem item in menu.Items)
        {
            ApplyTrayItemTheme(item, back, fore, muted);
        }
    }

    private void ApplyTrayItemTheme(WinForms.ToolStripItem item, System.Drawing.Color back, System.Drawing.Color fore, System.Drawing.Color muted)
    {
        item.BackColor = back;
        item.ForeColor = item.Enabled ? fore : muted;
        if (item is WinForms.ToolStripMenuItem menuItem)
        {
            menuItem.DropDown.BackColor = back;
            menuItem.DropDown.ForeColor = fore;
            foreach (WinForms.ToolStripItem child in menuItem.DropDownItems)
            {
                ApplyTrayItemTheme(child, back, fore, muted);
            }
        }
    }

    private static string TruncateTrayText(string value)
    {
        return value.Length <= 63 ? value : value[..60] + "...";
    }

    private static Version GetCurrentVersion()
    {
        var currentVersionText = GetCurrentVersionText();
        return Version.TryParse(currentVersionText, out var version)
            ? version
            : Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
    }

    private static string GetCurrentVersionText()
    {
        var informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+', StringSplitOptions.RemoveEmptyEntries)[0];
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        return version.Build >= 0 ? version.ToString(3) : version.ToString();
    }

    private void Notify([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
