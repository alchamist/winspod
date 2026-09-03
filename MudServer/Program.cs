using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MudServer;
using MudServer.Api;

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
