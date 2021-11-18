﻿using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Serilog;
using Serilog.Events;

namespace ks.fiks.io.politiskbehandling.sample
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var aspnetcoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            var logstashDestination = Environment.GetEnvironmentVariable("LOGSTASH_DESTINATION");
            var hostname = Environment.GetEnvironmentVariable("HOSTNAME");
            var kubernetesNode = Environment.GetEnvironmentVariable("KUBERNETES_NODE");
            var environment = Environment.GetEnvironmentVariable("ENVIRONMENT");
            
            var loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore.Localization", LogEventLevel.Error)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("app", "politisk-behandling-simulator")
                .Enrich.WithProperty("env", environment)
                .Enrich.WithProperty("logsource", hostname)
                .Enrich.WithProperty("node", kubernetesNode)
                .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level}] [{RequestId}] [{requestid}] - {Message} {NewLine} {Exception}");

            Log.Logger = loggerConfiguration.CreateLogger();

            Log.Information("Starting host with env variables:");
            Log.Information("ASPNETCORE_ENVIRONMENT: {AspnetcoreEnvironment}", aspnetcoreEnvironment);
            Log.Information("HOSTNAME: {Hostname}", hostname);
            Log.Information("KUBERNETES_NODE: {KubernetesNode}", kubernetesNode);
            Log.Information("ENVIRONMENT: {Environment}",environment);
            Log.Information("LOGSTASH_DESTINATION: {LogstashDestination}", logstashDestination);
            Log.Information("Path.PathSeparator: {PathSeparator}", Path.PathSeparator);
            
            await WebHost.CreateDefaultBuilder(args)
                .UseKestrel(c => c.AddServerHeader = false)
                .ConfigureAppConfiguration((hostBuilder, config) =>
                {
                    config.AddEnvironmentVariables("DOTNET_");
                    config.SetBasePath(Directory.GetCurrentDirectory());
                    config.AddJsonFile("appsettings.json", optional: true);
                    config.AddJsonFile($"appsettings.{aspnetcoreEnvironment}.json", optional: true);
                    config.AddEnvironmentVariables("fiksPolitiskBehandlingMock_");
                    
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddSingleton(AppSettingsBuilder.CreateAppSettings(hostContext.Configuration));
                    services.AddHostedService<UtvalgService>();
                    services.AddHealthChecks();
                })
                .UseStartup<Startup>()
                .UseSerilog()
                .Build().RunAsync();
        }
    }
}
