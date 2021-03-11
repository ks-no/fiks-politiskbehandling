using Ks.Fiks.Maskinporten.Client;
using KS.Fiks.IO.Client;
using KS.Fiks.IO.Client.Configuration;
using KS.Fiks.IO.Client.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ks.fiks.io.eplansak.utvalg.sample
{
    class Program
    {
        static async Task Main(string[] args)
        {

            await new HostBuilder()
           .ConfigureServices((hostContext, services) =>
           {
               services.AddHostedService<ePlansakService>();
           })
           .RunConsoleAsync();


            IConfiguration config = new ConfigurationBuilder()
            
            .AddJsonFile("appsettings.json", true, true)
            .AddJsonFile("appsettings.development.json", true, true)
            .Build();


        }

        
    }
}
