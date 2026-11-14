using System.Windows;
using ArmControlSuite.Helpers;

namespace ArmControlSuite;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        InteractiveMotion.RegisterGlobalButtonMotion();
        base.OnStartup(e);
    }
}
