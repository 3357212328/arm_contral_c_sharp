using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArmControlSuite.Services;
using ArmControlSuite.ViewModels;
using ArmControlSuite.Views;

namespace ArmControlSuite.Tests;

[TestClass]
public class UiSmokeTests
{
    [TestMethod]
    public void MainWindow_RendersAllFiveTabsAndBothThemesWithoutExceptions()
    {
        Exception? threadException = null;

        string tempDir = Path.Combine(Path.GetTempPath(), $"ArmUiSmoke_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var staThread = new Thread(() =>
        {
            try
            {
                // 确保 WPF Application 资源字典加载完整
                if (Application.Current == null)
                {
                    var app = new App();
                    app.InitializeComponent();
                }

                var settingsService = new SettingsService(tempDir);
                var window = new MainWindow(settingsService);
                var vm = (MainViewModel)window.DataContext;

                // 注入一帧真实协议遥测报文以完整驱动仪表盘 / 手动控制 / 监控 / 设置状态
                vm.AppendLog("串口已连接 -> COM7 @ 115200 bps");
                vm.AppendLog("PC -> MCU (Serial): sync on");
                vm.SimulateTelemetryForTest("$MODE:1;CUR_S_IDX:2;ANGS:90,45,120,60,150,30,110,90,90,90,90,90,90,90,90,90;MASK:127;TEMP:26;HUMI:54;R_STEPS:8;P_STAT:0#\n");
                PumpDispatcher();

                Assert.IsTrue(vm.IsSpeed10x, "默认倍速应为 1.0x");

                window.Show();
                window.Width = 1380;
                window.Height = 880;
                window.UpdateLayout();

                string? previewDir = Environment.GetEnvironmentVariable("ARM_UI_PREVIEW_DIR");

                for (int tab = 0; tab < 5; tab++)
                {
                    vm.SelectedTabIndex = tab;
                    WaitAndPumpAnimations(280);
                    window.UpdateLayout();

                    if (!string.IsNullOrWhiteSpace(previewDir))
                    {
                        SaveVisualPng(window, Path.Combine(previewDir, $"light_tab{tab}.png"));
                    }
                }

                // 切换深色模式并验证关键页面渲染
                vm.ToggleThemeCommand.Execute(null);
                WaitAndPumpAnimations(200);
                foreach (int darkTab in new[] { 0, 2, 4 })
                {
                    vm.SelectedTabIndex = darkTab;
                    WaitAndPumpAnimations(380);
                    window.UpdateLayout();

                    if (!string.IsNullOrWhiteSpace(previewDir))
                    {
                        SaveVisualPng(window, Path.Combine(previewDir, $"dark_tab{darkTab}.png"));
                    }
                }

                // 恢复浅色并在紧凑高度+多条日志下验证滚动条非遮挡布局
                vm.ToggleThemeCommand.Execute(null);
                WaitAndPumpAnimations(200);
                for (int i = 0; i < 10; i++)
                {
                    vm.AppendLog($"MCU -> PC (Response): OK STEP={i + 1}");
                }
                window.Width = 1200;
                window.Height = 680;
                vm.SelectedTabIndex = 1;
                WaitAndPumpAnimations(420);
                window.UpdateLayout();

                if (!string.IsNullOrWhiteSpace(previewDir))
                {
                    SaveVisualPng(window, Path.Combine(previewDir, "scroll_check.png"));
                }

                // 恢复默认窗口尺寸后验证关闭流程
                window.Width = 1380;
                window.Height = 880;
                vm.SelectedTabIndex = 0;
                WaitAndPumpAnimations(280);
                PumpDispatcher();

                window.Close();
                PumpDispatcher();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();

        try { Directory.Delete(tempDir, recursive: true); } catch { }

        if (threadException != null)
            throw new AssertFailedException($"UI 渲染测试发生异常: {threadException}", threadException);
    }

    private static void WaitAndPumpAnimations(int milliseconds)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(milliseconds),
            System.Windows.Threading.DispatcherPriority.Background,
            (s, _) =>
            {
                ((System.Windows.Threading.DispatcherTimer)s!).Stop();
                frame.Continue = false;
            },
            System.Windows.Threading.Dispatcher.CurrentDispatcher);
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        PumpDispatcher();
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static void SaveVisualPng(FrameworkElement element, string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(filePath);
        encoder.Save(fs);
    }
}
