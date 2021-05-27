using Ks.Fiks.Maskinporten.Client;
using KS.Fiks.IO.Client;
using KS.Fiks.IO.Client.Configuration;
using KS.Fiks.IO.Client.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace ks.fiks.io.politiskbehandling.sample
{
    class Program
    {
        static async Task Main(string[] args)
        {
            await new HostBuilder()
                .ConfigureHostConfiguration((configHost) =>
                {
                    configHost.AddEnvironmentVariables("DOTNET_");
                })
                .ConfigureAppConfiguration((hostBuilder, config) =>
                {
                    config.SetBasePath(Directory.GetCurrentDirectory());
                    config.AddJsonFile("appsettings.json", optional: true);
                    config.AddJsonFile($"appsettings.{hostBuilder.HostingEnvironment.EnvironmentName}.json", optional: true);
                    config.AddEnvironmentVariables("fiksPolitiskBehandlingMock_");
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddSingleton(AppSettingsBuilder.CreateAppSettings(hostContext.Configuration));
                    services.AddHostedService<UtvalgService>();
                })
                .RunConsoleAsync();
        }

    }
}
