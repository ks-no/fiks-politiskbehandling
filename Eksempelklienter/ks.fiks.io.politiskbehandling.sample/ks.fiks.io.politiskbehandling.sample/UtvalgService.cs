using Ks.Fiks.Maskinporten.Client;
using KS.Fiks.IO.Client;
using KS.Fiks.IO.Client.Configuration;
using KS.Fiks.IO.Client.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ks.fiks.io.politiskbehandling.sample
{

        public class UtvalgService : IHostedService, IDisposable
        {
            FiksIOClient client;

            public UtvalgService()
            {
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                Console.WriteLine("Utvalg Service is starting.");
                IConfiguration config = new ConfigurationBuilder()

                .AddJsonFile("appsettings.json", true, true)
                .AddJsonFile("appsettings.development.json", true, true)
                .Build();

                Console.WriteLine("Setter opp FIKS integrasjon for politisk behandling...");
                Guid accountId = Guid.Parse(config["accountId"]);  /* Fiks IO accountId as Guid Banke kommune digitalt planregister konto*/
                string privateKey = File.ReadAllText("privkey.pem"); ; /* Private key for offentlig nøkkel supplied to Fiks IO account */
                Guid integrationId = Guid.Parse(config["integrationId"]); /* Integration id as Guid ePlansak system X */
                string integrationPassword = config["integrationPassword"];  /* Integration password */

                // Fiks IO account configuration
                var account = new KontoConfiguration(
                                    accountId,
                                    privateKey);

                // Id and password for integration associated to the Fiks IO account.
                var integration = new IntegrasjonConfiguration(
                                        integrationId,
                                        integrationPassword, "ks:fiks");

                // ID-porten machine to machine configuration
                var maskinporten = new MaskinportenClientConfiguration(
                    audience: @"https://oidc-ver2.difi.no/idporten-oidc-provider/", // ID-porten audience path
                    tokenEndpoint: @"https://oidc-ver2.difi.no/idporten-oidc-provider/token", // ID-porten token path
                    issuer: @"arkitektum_test",  // issuer name
                    numberOfSecondsLeftBeforeExpire: 10, // The token will be refreshed 10 seconds before it expires
                    certificate: GetCertificate(config["ThumbprintIdPortenVirksomhetssertifikat"]));

                // Optional: Use custom api host (i.e. for connecting to test api)
                var api = new ApiConfiguration(
                                scheme: "https",
                                host: "api.fiks.test.ks.no",
                                port: 443);

                // Optional: Use custom amqp host (i.e. for connection to test queue)
                var amqp = new AmqpConfiguration(
                                host: "io.fiks.test.ks.no",
                                port: 5671);

                // Combine all configurations
                var configuration = new FiksIOConfiguration(account, integration, maskinporten, api, amqp);
                client = new FiksIOClient(configuration); // See setup of configuration below

               

                client.NewSubscription(OnReceivedMelding);

                Console.WriteLine("Abonnerer på meldinger på konto " + accountId.ToString() + " ...");

                return Task.CompletedTask;
            }

            static void OnReceivedMelding(object sender, MottattMeldingArgs fileArgs)
            {
            //Se oversikt over meldingstyper på https://github.com/ks-no/fiks-io-meldingstype-katalog/tree/test/schema

            // Process the message

            if (fileArgs.Melding.MeldingType == "no.ks.fiks.politisk.behandling.avsender.hentmøteplan.v1")
            {
                Console.WriteLine("Melding " + fileArgs.Melding.MeldingId + " " + fileArgs.Melding.MeldingType + " mottas...");
                
                //TODO håndtere meldingen med ønsket funksjonalitet

                string payload = File.ReadAllText("sampleResultat.json");
                
                var svarmsg = fileArgs.SvarSender.Svar("no.ks.fiks.politisk.behandling.mottak.resultatmøteplan.v1", payload, "resultat.json").Result;
                Console.WriteLine("Svarmelding " + svarmsg.MeldingId + " " + svarmsg.MeldingType + " sendt...");
                Console.WriteLine(payload);

                fileArgs.SvarSender.Ack(); // Ack message to remove it from the queue
            }
            else if (fileArgs.Melding.MeldingType == "no.ks.fiks.politisk.behandling.avsender.sendutvalgssak.v1")
            {
                Console.WriteLine("Melding " + fileArgs.Melding.MeldingId + " " + fileArgs.Melding.MeldingType + " mottas...");

                //TODO håndtere meldingen med ønsket funksjonalitet
                
                var svarmsg = fileArgs.SvarSender.Svar("no.ks.geointegrasjon.ok.v1").Result;
                Console.WriteLine("Svarmelding " + svarmsg.MeldingId + " " + svarmsg.MeldingType + " sendt...");
               

                fileArgs.SvarSender.Ack(); // Ack message to remove it from the queue
            }
            else if (fileArgs.Melding.MeldingType == "no.ks.fiks.politisk.behandling.avsender.sendorienteringssak.v1")
            {
                Console.WriteLine("Melding " + fileArgs.Melding.MeldingId + " " + fileArgs.Melding.MeldingType + " mottas...");

                //TODO håndtere meldingen med ønsket funksjonalitet

                var svarmsg = fileArgs.SvarSender.Svar("no.ks.geointegrasjon.ok.v1").Result;
                Console.WriteLine("Svarmelding " + svarmsg.MeldingId + " " + svarmsg.MeldingType + " sendt...");


                fileArgs.SvarSender.Ack(); // Ack message to remove it from the queue
            }
            else if (fileArgs.Melding.MeldingType == "no.ks.fiks.politisk.behandling.avsender.senddelegertvedtak.v1")
            {
                Console.WriteLine("Melding " + fileArgs.Melding.MeldingId + " " + fileArgs.Melding.MeldingType + " mottas...");

                //TODO håndtere meldingen med ønsket funksjonalitet

                var svarmsg = fileArgs.SvarSender.Svar("no.ks.geointegrasjon.ok.v1").Result;
                Console.WriteLine("Svarmelding " + svarmsg.MeldingId + " " + svarmsg.MeldingType + " sendt...");


                fileArgs.SvarSender.Ack(); // Ack message to remove it from the queue
            }
            else if (fileArgs.Melding.MeldingType == "no.ks.geointegrasjon.ok.v1")
            {
                Console.WriteLine("Melding " + fileArgs.Melding.MeldingId + " " + fileArgs.Melding.MeldingType + " mottas...");

                //TODO håndtere meldingen med ønsket funksjonalitet

                Console.WriteLine("Melding er håndtert i ePlansak ok ......");

                fileArgs.SvarSender.Ack(); // Ack message to remove it from the queue

            }
            else {
                Console.WriteLine("Ubehandlet melding i køen " + fileArgs.Melding.MeldingId + " " + fileArgs.Melding.MeldingType);
                //fileArgs.SvarSender.Ack(); // Ack message to remove it from the queue
            }
            }


            

            public Task StopAsync(CancellationToken cancellationToken)
            {
                Console.WriteLine("Planregister Service is stopping.2");
                //Client?
                return Task.CompletedTask;
            }

            public void Dispose()
            {
            //Client?
            client.Dispose();
            }

            private static X509Certificate2 GetCertificate(string ThumbprintIdPortenVirksomhetssertifikat)
            {

                //Det samme virksomhetssertifikat som er registrert hos ID-porten
                X509Store store = new X509Store(StoreLocation.CurrentUser);
                X509Certificate2 cer = null;
                store.Open(OpenFlags.ReadOnly);
                //Henter Arkitektum sitt virksomhetssertifikat
                X509Certificate2Collection cers = store.Certificates.Find(X509FindType.FindByThumbprint, ThumbprintIdPortenVirksomhetssertifikat, false);
                if (cers.Count > 0)
                {
                    cer = cers[0];
                };
                store.Close();

                return cer;
            }
        }
    
}
