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

ApplyEnvironmentOverrides();

// The telnet game loop (Server.RunAsync, driven by TelnetHostedService) always runs.
// The JSON web API is entirely optional and only binds a port when HTTPEnabled is set
// in app.config - same toggle the old HttpListener-based webserver used.
if (AppSettings.Default.HTTPEnabled)
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls("http://0.0.0.0:" + AppSettings.Default.HTTPPort);
    builder.Services.AddHostedService<TelnetHostedService>();

    var app = builder.Build();
    app.UseDefaultFiles();
    app.UseStaticFiles();
    MudApiEndpoints.Map(app);
    TelnetWebSocketBridge.Map(app);

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

// app.config's ApplicationSettingsBase settings are baked in at build time - fine for a
// bare-metal/systemd deployment where editing app.config and rebuilding is normal, but
// the wrong shape for a container image, which should be configurable per-deployment
// without a rebuild. This overrides the handful of settings that actually vary between
// deployments (network config, talker identity) from environment variables when present;
// everything else still comes from app.config as before. Deliberately not a general
// "any setting via env var" mechanism - only what Docker actually needs.
static void ApplyEnvironmentOverrides()
{
    string port = Environment.GetEnvironmentVariable("MUD_PORT");
    if (int.TryParse(port, out int parsedPort))
        AppSettings.Default.Port = parsedPort;

    string httpEnabled = Environment.GetEnvironmentVariable("MUD_HTTP_ENABLED");
    if (bool.TryParse(httpEnabled, out bool parsedHttpEnabled))
        AppSettings.Default.HTTPEnabled = parsedHttpEnabled;

    string httpPort = Environment.GetEnvironmentVariable("MUD_HTTP_PORT");
    if (int.TryParse(httpPort, out int parsedHttpPort))
        AppSettings.Default.HTTPPort = parsedHttpPort;

    string talkerName = Environment.GetEnvironmentVariable("MUD_TALKER_NAME");
    if (!string.IsNullOrEmpty(talkerName))
        AppSettings.Default.TalkerName = talkerName;

    string talkerAddress = Environment.GetEnvironmentVariable("MUD_TALKER_ADDRESS");
    if (!string.IsNullOrEmpty(talkerAddress))
        AppSettings.Default.TalkerAddress = talkerAddress;

    string talkerEmail = Environment.GetEnvironmentVariable("MUD_TALKER_EMAIL");
    if (!string.IsNullOrEmpty(talkerEmail))
        AppSettings.Default.TalkerEmail = talkerEmail;
}
