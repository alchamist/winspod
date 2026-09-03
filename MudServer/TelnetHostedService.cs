using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace MudServer
{
    /// <summary>
    /// Adapts the telnet accept loop (<see cref="Server.RunAsync"/>) to the generic
    /// host's lifecycle, so it starts/stops alongside the (optional) web API host.
    /// </summary>
    public class TelnetHostedService : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Server.RunAsync(stoppingToken);
        }
    }
}
