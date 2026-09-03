using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MudServer;
using MudServer.Api;

// All the game's date/time output (login screens, "resident since", stats, etc.) calls
// plain DateTime.ToString()/ToShortDateString()/etc. with no explicit culture, so it
// renders using whatever the process's default culture is. Under .NET Framework on
// Windows that silently followed the host machine's regional settings; left alone on
// modern .NET it follows the deployment host's locale too, which means the exact same
// build shows US-style dates on one machine and UK-style on another. Pinning it here
// makes the display consistent (dd/MM/yyyy, as this talker is UK-run) regardless of
// what OS or locale it's actually deployed on.
CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-GB");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-GB");

// The telnet game loop (Server.RunAsync, driven by TelnetHostedService) always runs.
// The JSON web API is entirely optional and only binds a port when HTTPEnabled is set
// in app.config - same toggle the old HttpListener-based webserver used.
if (AppSettings.Default.HTTPEnabled)
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls("http://0.0.0.0:" + AppSettings.Default.HTTPPort);
    builder.Services.AddHostedService<TelnetHostedService>();

    var app = builder.Build();
    MudApiEndpoints.Map(app);

    Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] HTTP API listening on port " + AppSettings.Default.HTTPPort);
    await app.RunAsync();
}
else
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddHostedService<TelnetHostedService>();

    var host = builder.Build();
    await host.RunAsync();
}
