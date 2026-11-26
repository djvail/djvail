using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

namespace VailITMonitor.Service
{
    /// <summary>
    /// Lets the classic InstallUtil.exe tool install the service:
    ///   %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe VailITMonitor.exe
    ///   %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe /u VailITMonitor.exe
    ///
    /// You don't need this if you use scripts\Install-VailITMonitor.ps1 (which uses sc.exe and
    /// works the same for both editions) - it is here to show the traditional approach.
    /// .NET 10 has no InstallUtil; services are installed with sc.exe or New-Service.
    /// </summary>
    [RunInstaller(true)]
    public sealed class ProjectInstaller : Installer
    {
        public ProjectInstaller()
        {
            var process = new ServiceProcessInstaller { Account = ServiceAccount.LocalSystem };
            var service = new ServiceInstaller
            {
                ServiceName = MonitorService.Name,
                DisplayName = "VailIT Monitor",
                Description = "Records CPU, memory, disk and network readings and produces PDF reports.",
                StartType = ServiceStartMode.Automatic,
                DelayedAutoStart = true,
            };
            Installers.Add(process);
            Installers.Add(service);
        }
    }
}
