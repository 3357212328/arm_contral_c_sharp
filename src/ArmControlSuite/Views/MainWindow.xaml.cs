using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ArmControlSuite.Helpers;
using ArmControlSuite.Models;
using ArmControlSuite.Services;
using ArmControlSuite.ViewModels;
using ArmControlSuite.Views.Dialogs;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ArmControlSuite.Views;

public partial class MainWindow : FluentWindow, IUiService
{
    private static readonly SolidColorBrush TimestampBrush = CreateFrozenBrush("#64748B");
    private static readonly SolidColorBrush TxTagBrush = CreateFrozenBrush("#38BDF8");
    private static readonly SolidColorBrush TxMsgBrush = CreateFrozenBrush("#BAE6FD");
    private static readonly SolidColorBrush PktTagBrush = CreateFrozenBrush("#34D399");
    private static readonly SolidColorBrush PktMsgBrush = CreateFrozenBrush("#A7F3D0");
    private static readonly SolidColorBrush RxTagBrush = CreateFrozenBrush("#C084FC");
    private static readonly SolidColorBrush RxMsgBrush = CreateFrozenBrush("#E9D5FF");
    private static readonly SolidColorBrush ErrTagBrush = CreateFrozenBrush("#F87171");
    private static readonly SolidColorBrush ErrMsgBrush = CreateFrozenBrush("#FECACA");
    private static readonly SolidColorBrush SysTagBrush = CreateFrozenBrush("#FBBF24");
    private static readonly SolidColorBrush SysMsgBrush = CreateFrozenBrush("#FDE68A");

    private readonly MainViewModel _viewModel;
    private double _lastExpandedLogHeight = FirmwareConstants.DefaultLogHeight;
    private int _lastTabIndex;
    private bool _isClosingCompleted;
    private bool _isUpdatingLogHeight;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(SettingsService? settingsService)
    {
        InteractiveMotion.RegisterGlobalButtonMotion();
        InitializeComponent();

        settingsService ??= new SettingsService();
        var discoveryService = new DeviceDiscoveryService();
        _viewModel = new MainViewModel(settingsService, discoveryService, this);
        DataContext = _viewModel;
        _lastTabIndex = _viewModel.SelectedTabIndex;

        Width = _viewModel.WindowWidth;
        Height = _viewModel.WindowHeight;
        _lastExpandedLogHeight = Math.Max(FirmwareConstants.MinLogHeight, _viewModel.LogHeight);

        _viewModel.LogAppended += OnLogAppended;
        _viewModel.LogsCleared += OnLogsCleared;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        ApplyLogDrawerLayout();

        SizeChanged += MainWindow_SizeChanged;
        Closing += MainWindow_Closing;
    }

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectedTabIndex))
        {
            int nextIndex = _viewModel.SelectedTabIndex;
            int direction = nextIndex >= _lastTabIndex ? 1 : -1;
            _lastTabIndex = nextIndex;
            FrameworkElement? activeTab = nextIndex switch
            {
                0 => TabDashboardView,
                1 => TabManualView,
                2 => TabSequenceView,
                3 => TabMonitorView,
                4 => TabSettingsView,
                _ => null
            };
            if (activeTab != null)
            {
                InteractiveMotion.AnimateTabEntrance(activeTab, direction);
            }
        }
        else if (e.PropertyName is nameof(MainViewModel.IsToastVisible) && _viewModel.IsToastVisible)
        {
            InteractiveMotion.AnimateToastEntrance(ToastBanner);
        }
        else if (e.PropertyName is nameof(MainViewModel.IsConnected))
        {
            InteractiveMotion.SetConnectedDotPulse(ConnectionPulseDot, _viewModel.IsConnected);
        }
        else if (e.PropertyName is nameof(MainViewModel.IsLogCollapsed))
        {
            ApplyLogDrawerLayout();
            if (!_viewModel.IsLogCollapsed)
            {
                InteractiveMotion.AnimateExpandFade(LogBodyGrid);
            }
        }
        else if (e.PropertyName is nameof(MainViewModel.IsAutoScroll) && _viewModel.IsAutoScroll && !_viewModel.IsLogCollapsed)
        {
            LogRichTextBox.ScrollToEnd();
        }
    }

    private void ApplyLogDrawerLayout()
    {
        _isUpdatingLogHeight = true;
        try
        {
            if (_viewModel.IsLogCollapsed)
            {
                LogDrawerRow.MinHeight = FirmwareConstants.CollapsedLogHeight;
                LogDrawerRow.Height = new GridLength(FirmwareConstants.CollapsedLogHeight);
            }
            else
            {
                double maxH = GetMaxLogHeight();
                double targetH = Math.Clamp(_lastExpandedLogHeight, FirmwareConstants.MinLogHeight, maxH);
                LogDrawerRow.MinHeight = FirmwareConstants.MinLogHeight;
                LogDrawerRow.Height = new GridLength(targetH);
                if (_viewModel.IsAutoScroll)
                {
                    Dispatcher.BeginInvoke(() => LogRichTextBox.ScrollToEnd());
                }
            }
        }
        finally
        {
            _isUpdatingLogHeight = false;
        }
    }

    private double GetMaxLogHeight()
    {
        double winH = ActualHeight > 0 ? ActualHeight : Height;
        return Math.Max(200, winH - 280);
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            _viewModel.WindowWidth = Math.Max(FirmwareConstants.MinWindowWidth, ActualWidth);
            _viewModel.WindowHeight = Math.Max(FirmwareConstants.MinWindowHeight, ActualHeight);
        }

        if (!_viewModel.IsLogCollapsed && !_isUpdatingLogHeight)
        {
            double maxH = GetMaxLogHeight();
            if (LogDrawerRow.Height.IsAbsolute && LogDrawerRow.Height.Value > maxH)
            {
                _lastExpandedLogHeight = Math.Max(FirmwareConstants.MinLogHeight, maxH);
                LogDrawerRow.Height = new GridLength(_lastExpandedLogHeight);
                _viewModel.SaveLogGeometry(_lastExpandedLogHeight, false);
            }
        }
    }

    private void LogGridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        double actual = LogDrawerRow.ActualHeight;
        if (actual < 72)
        {
            _viewModel.SaveLogGeometry(_lastExpandedLogHeight, collapsed: true);
        }
        else
        {
            double clamped = Math.Clamp(actual, FirmwareConstants.MinLogHeight, GetMaxLogHeight());
            _lastExpandedLogHeight = clamped;
            LogDrawerRow.Height = new GridLength(clamped);
            _viewModel.SaveLogGeometry(clamped, collapsed: false);
        }
    }

    private void LogHeader_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.ToggleLogCollapseCommand.Execute(null);
    }

    private void LogTitle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _viewModel.ToggleLogCollapseCommand.Execute(null);
        }
    }

    private void SppMacTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveSppAddressOnBlur();
    }

    private void QuickCommandInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.SendQuickCommandCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            _viewModel.NavigateCommandHistory(-1);
            QuickCommandInputBox.CaretIndex = QuickCommandInputBox.Text.Length;
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            _viewModel.NavigateCommandHistory(1);
            QuickCommandInputBox.CaretIndex = QuickCommandInputBox.Text.Length;
            e.Handled = true;
        }
    }

    private void Toast_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _viewModel.DismissToastCommand.Execute(null);
    }

    private void OnLogAppended(LogEntry entry)
    {
        var doc = LogRichTextBox.Document;
        var (tagBrush, msgBrush) = entry.Category switch
        {
            LogCategory.Tx => (TxTagBrush, TxMsgBrush),
            LogCategory.Packet => (PktTagBrush, PktMsgBrush),
            LogCategory.Rx => (RxTagBrush, RxMsgBrush),
            LogCategory.Error => (ErrTagBrush, ErrMsgBrush),
            _ => (SysTagBrush, SysMsgBrush)
        };

        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 1, 0, 1)
        };
        paragraph.Inlines.Add(new Run($"[{entry.Timestamp}] ") { Foreground = TimestampBrush });
        paragraph.Inlines.Add(new Run($"[{entry.TagLabel.Trim()}] ") { Foreground = tagBrush, FontWeight = FontWeights.Bold });
        paragraph.Inlines.Add(new Run(entry.Message) { Foreground = msgBrush });

        doc.Blocks.Add(paragraph);

        while (doc.Blocks.Count > FirmwareConstants.MaxUiLogItems)
        {
            if (doc.Blocks.FirstBlock != null)
                doc.Blocks.Remove(doc.Blocks.FirstBlock);
            else
                break;
        }

        if (_viewModel.IsAutoScroll && !_viewModel.IsLogCollapsed)
        {
            LogRichTextBox.ScrollToEnd();
        }
    }

    private void OnLogsCleared()
    {
        LogRichTextBox.Document.Blocks.Clear();
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isClosingCompleted)
            return;

        e.Cancel = true;
        if (WindowState == WindowState.Normal)
        {
            _viewModel.WindowWidth = Math.Max(FirmwareConstants.MinWindowWidth, ActualWidth);
            _viewModel.WindowHeight = Math.Max(FirmwareConstants.MinWindowHeight, ActualHeight);
        }

        await _viewModel.ShutdownAsync();
        _isClosingCompleted = true;
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }

    // =================== IUiService 实现 ===================
    public void InvokeOnUiThread(Action action)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.BeginInvoke(action);
    }

    public void ApplyTheme(bool isDark)
    {
        ApplicationThemeManager.Apply(
            isDark ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.Mica,
            true);

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        string asmName = typeof(MainWindow).Assembly.GetName().Name ?? "ArmControlSuite";
        string targetThemeUri = isDark
            ? $"pack://application:,,,/{asmName};component/Themes/DarkTheme.xaml"
            : $"pack://application:,,,/{asmName};component/Themes/LightTheme.xaml";

        for (int i = 0; i < dictionaries.Count; i++)
        {
            string? src = dictionaries[i].Source?.OriginalString;
            if (src != null && (src.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase)
                             || src.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase)))
            {
                dictionaries[i] = new ResourceDictionary
                {
                    Source = new Uri(targetThemeUri, UriKind.Absolute)
                };
                break;
            }
        }
    }

    public void CopyTextToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // 剪贴板占用时静默重试一次
            try { Clipboard.SetDataObject(text, true); } catch { }
        }
    }

    public void OpenFolderInExplorer(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }

    public Task<(BleCharacteristicItem? WriteChar, BleCharacteristicItem? NotifyChar)> ShowBleCharSelectorAsync(
        IReadOnlyList<BleCharacteristicItem> writeChars,
        IReadOnlyList<BleCharacteristicItem> notifyChars,
        string? defaultWriteUuid,
        string? defaultNotifyUuid)
    {
        var tcs = new TaskCompletionSource<(BleCharacteristicItem?, BleCharacteristicItem?)>();
        InvokeOnUiThread(() =>
        {
            var vm = new BleCharSelectorViewModel(writeChars, notifyChars, defaultWriteUuid, defaultNotifyUuid);
            var dlg = new BleCharSelectorWindow(vm) { Owner = this };
            bool? res = dlg.ShowDialog();
            if (res == true)
                tcs.SetResult((vm.SelectedWriteCharacteristic, vm.SelectedNotifyCharacteristic));
            else
                tcs.SetResult((null, null));
        });
        return tcs.Task;
    }

    public Task ShowSppPairingGuideAsync()
    {
        var tcs = new TaskCompletionSource();
        InvokeOnUiThread(() =>
        {
            var dlg = new SppPairingGuideWindow { Owner = this };
            dlg.ShowDialog();
            tcs.SetResult();
        });
        return tcs.Task;
    }

    public Task<bool> ShowConfirmDialogAsync(string title, string message, string confirmText, bool isDanger)
    {
        var tcs = new TaskCompletionSource<bool>();
        InvokeOnUiThread(() =>
        {
            var dlg = new ConfirmDialogWindow(title, message, confirmText, isDanger) { Owner = this };
            bool? res = dlg.ShowDialog();
            tcs.SetResult(res == true);
        });
        return tcs.Task;
    }

    public Task<string?> ShowSaveLogFileDialogAsync(string defaultFileName)
    {
        var tcs = new TaskCompletionSource<string?>();
        InvokeOnUiThread(() =>
        {
            var sfd = new SaveFileDialog
            {
                FileName = defaultFileName,
                Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                DefaultExt = ".log"
            };
            bool? ok = sfd.ShowDialog(this);
            tcs.SetResult(ok == true ? sfd.FileName : null);
        });
        return tcs.Task;
    }
}
