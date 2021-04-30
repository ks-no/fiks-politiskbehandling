using Ks.Fiks.Maskinporten.Client;
using KS.Fiks.ASiC_E;
using KS.Fiks.IO.Client;
using KS.Fiks.IO.Client.Configuration;
using KS.Fiks.IO.Client.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
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

        static void OnReceivedMelding(object sender, MottattMeldingArgs mottatt)
        {
            //Se oversikt over meldingstyper på https://github.com/ks-no/fiks-io-meldingstype-katalog/tree/test/schema

            // Process the message
            if (mottatt.Melding.MeldingType == "no.ks.fiks.politisk.behandling.klient.hentutvalg.v1")
            {
                Console.WriteLine("Melding " + mottatt.Melding.MeldingId + " " + mottatt.Melding.MeldingType + " mottas...");

                
                    List<List<string>> errorMessages = new List<List<string>>() { new List<string>(), new List<string>() };

                    if (errorMessages[0].Count == 0)
                    {
                        string payload = File.ReadAllText("sampleResultatUtvalg.json");

                        errorMessages = ValidateJsonFile(payload, Path.Combine("schema", "no.ks.fiks.politisk.behandling.resultatutvalg.v1.schema.json"));

                        if (errorMessages[0].Count == 0)
                        {
                            var svarmsg = mottatt.SvarSender.Svar("no.ks.fiks.politisk.behandling.tjener.resultatutvalg.v1", payload, "resultat.json").Result;
                            Console.WriteLine("Svarmelding " + svarmsg.MeldingId + " " + svarmsg.MeldingType + " sendt...");
                            Console.WriteLine(payload);
                            mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                        }
                        else
                        {
                            //TODO: Håndtere feil ved resultatmøteplanvalidering
                            Console.WriteLine("Feil i validering av utvalg");
                            foreach (string message in errorMessages[0])
                            {
                                Console.WriteLine(message);
                            }
                        mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                        }
                    }
            }
            else if (mottatt.Melding.MeldingType == "no.ks.fiks.politisk.behandling.klient.hentmøteplan.v1")
            {
                Console.WriteLine("Melding " + mottatt.Melding.MeldingId + " " + mottatt.Melding.MeldingType + " mottas...");

                if (mottatt.Melding.HasPayload)
                { // Verify that message has payload
                    List<List<string>> errorMessages = new List<List<string>>() { new List<string>(), new List<string>() };
                    IAsicReader reader = new AsiceReader();
                    using (var inputStream = mottatt.Melding.DecryptedStream.Result)
                    using (var asice = reader.Read(inputStream))
                    {
                        foreach (var asiceReadEntry in asice.Entries)
                        {
                            using (var entryStream = asiceReadEntry.OpenStream())
                            {
                                if (asiceReadEntry.FileName.Contains(".json"))
                                {
                                    errorMessages = ValidateJsonFile(new StreamReader(entryStream).ReadToEnd(), Path.Combine("schema", "no.ks.fiks.politisk.behandling.hentmøteplan.v1.schema.json"));
                                }
                                else
                                    Console.WriteLine("Mottatt vedlegg: " + asiceReadEntry.FileName);
                            }
                        }
                    }

                    if (errorMessages[0].Count == 0)
                    {
                        string payload = File.ReadAllText("sampleResultat.json");

                        errorMessages = ValidateJsonFile(payload, Path.Combine("schema", "no.ks.fiks.politisk.behandling.resultatmøteplan.v1.schema.json"));

                        if (errorMessages[0].Count == 0)
                        {
                            var svarmsg = mottatt.SvarSender.Svar("no.ks.fiks.politisk.behandling.tjener.resultatmøteplan.v1", payload, "resultat.json").Result;
                            Console.WriteLine("Svarmelding " + svarmsg.MeldingId + " " + svarmsg.MeldingType + " sendt...");
                            Console.WriteLine(payload);
                            mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                        }
                        else
                        {
                            //TODO: Håndtere feil ved resultatmøteplanvalidering
                            Console.WriteLine("Feil i validering av resultatmøteplan");
                            foreach (string message in errorMessages[0])
                            {
                                Console.WriteLine(message);
                            }
                            mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                        }
                    }
                    else
                    {
                        Console.WriteLine("Feil i validering av hentmøteplan");
                        foreach (string message in errorMessages[0])
                        {
                            //TODO: Håndtere feil ved validering av hentemøteplan
                            Console.WriteLine(message);
                            mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                        }
                    }
                }
                else
                {
                    var svarmsg = mottatt.SvarSender.Svar("no.ks.fiks.kvittering.ugyldigforespørsel.v1", "Meldingen mangler innhold", "feil.txt").Result;
                    Console.WriteLine("Svarmelding " + svarmsg.MeldingId + " " + svarmsg.MeldingType + " Meldingen mangler innhold");

                    mottatt.SvarSender.Ack(); // Ack message to remove it from the queue

                }
            }
            else if (mottatt.Melding.MeldingType == "no.ks.fiks.politisk.behandling.klient.sendutvalgssak.v1")
            {
                Console.WriteLine("Melding " + mottatt.Melding.MeldingId + " " + mottatt.Melding.MeldingType + " mottas...");

                if (mottatt.Melding.HasPayload)
                { 
                    List<List<string>> errorMessages = new List<List<string>>() { new List<string>(), new List<string>() };
                    IAsicReader reader = new AsiceReader();
                    using (var inputStream = mottatt.Melding.DecryptedStream.Result)
                    using (var asice = reader.Read(inputStream))
                    {
                        foreach (var asiceReadEntry in asice.Entries)
                        {
                            using (var entryStream = asiceReadEntry.OpenStream())
                            {
                                if (asiceReadEntry.FileName.Contains(".json"))
                                {
                                    errorMessages = ValidateJsonFile(new StreamReader(entryStream).ReadToEnd(), Path.Combine("schema", "no.ks.fiks.politisk.behandling.sendutvalgssak.v1.schema.json"));
                                }
                                else
                                    Console.WriteLine("Mottatt vedlegg: " + asiceReadEntry.FileName);
                            }
                        }
                    }

                    if (errorMessages[0].Count == 0)
                    {
                            var svarmsg2 = mottatt.SvarSender.Svar("no.ks.fiks.politisk.behandling.mottatt.v1").Result;
                            Console.WriteLine("Svarmelding " + svarmsg2.MeldingId + " " + svarmsg2.MeldingType + " sendt...");
                            mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                    }
                    else
                    {
                        Console.WriteLine("Feil i validering av sendutvalgsak");
                        foreach (string message in errorMessages[0])
                        {
                            var svarmsg2 = mottatt.SvarSender.Svar("no.ks.fiks.kvittering.ugyldigforespørsel.v1").Result;
                            //TODO: Håndtere feil ved validering av hentemøteplan
                            Console.WriteLine(message);
                            mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                        }
                    }
                }
                else
                {
                    var svarmsg = mottatt.SvarSender.Svar("no.ks.fiks.kvittering.ugyldigforespørsel.v1", "Meldingen mangler innhold", "feil.txt").Result;
                    Console.WriteLine("Svarmelding " + svarmsg.MeldingId + " " + svarmsg.MeldingType + " Meldingen mangler innhold");

                    mottatt.SvarSender.Ack(); // Ack message to remove it from the queue

                }
            }
            else if (mottatt.Melding.MeldingType == "no.ks.fiks.politisk.behandling.klient.sendorienteringssak.v1")
            {
                Console.WriteLine("Melding " + mottatt.Melding.MeldingId + " " + mottatt.Melding.MeldingType + " mottas...");

                if (mottatt.Melding.HasPayload)
                {
                    List<List<string>> errorMessages = new List<List<string>>() { new List<string>(), new List<string>() };
                    IAsicReader reader = new AsiceReader();
                    using (var inputStream = mottatt.Melding.DecryptedStream.Result)
                    using (var asice = reader.Read(inputStream))
                    {
                        foreach (var asiceReadEntry in asice.Entries)
                        {
                            using (var entryStream = asiceReadEntry.OpenStream())
                            {
                                if (asiceReadEntry.FileName.Contains(".json"))
                                {
                                    errorMessages = ValidateJsonFile(new StreamReader(entryStream).ReadToEnd(), Path.Combine("schema", "no.ks.fiks.politisk.behandling.sendorienteringssak.v1.schema.json"));
                                }
                                else
                                    Console.WriteLine("Mottatt vedlegg: " + asiceReadEntry.FileName);
                            }
                        }
                    }

                    if (errorMessages[0].Count == 0)
                    {
                        var svarmsg2 = mottatt.SvarSender.Svar("no.ks.fiks.politisk.behandling.mottatt.v1").Result;
                        Console.WriteLine("Svarmelding " + svarmsg2.MeldingId + " " + svarmsg2.MeldingType + " sendt...");
                        mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                    }
                    else
                    {
                        Console.WriteLine("Feil i validering av sendorienteringssak");
                        var errorMessage = mottatt.SvarSender.Svar("no.ks.fiks.kvittering.ugyldigforespørsel.v1", String.Join("\n ", errorMessages[0]), "feil.txt").Result;
                        Console.WriteLine(String.Join("\n ", errorMessages[0]));
                        mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                    }
                }
                else
                {
                    var svarmsg = mottatt.SvarSender.Svar("no.ks.fiks.kvittering.ugyldigforespørsel.v1", "Meldingen mangler innhold", "feil.txt").Result;
                    Console.WriteLine("Svarmelding " + svarmsg.MeldingId + " " + svarmsg.MeldingType + " Meldingen mangler innhold");

                    mottatt.SvarSender.Ack(); // Ack message to remove it from the queue

                }
            }
            else if (mottatt.Melding.MeldingType == "no.ks.fiks.politisk.behandling.klient.senddelegertvedtak.v1")
            {
                Console.WriteLine("Melding " + mottatt.Melding.MeldingId + " " + mottatt.Melding.MeldingType + " mottas...");

                //TODO håndtere meldingen med ønsket funksjonalitet

                var svarmsg = mottatt.SvarSender.Svar("no.ks.fiks.politisk.behandling.mottatt.v1").Result;
                Console.WriteLine("Svarmelding " + svarmsg.MeldingId + " " + svarmsg.MeldingType + " sendt...");


                    mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
            }
            else if (mottatt.Melding.MeldingType == "no.ks.fiks.politisk.behandling.mottatt.v1")
            {
                Console.WriteLine("Melding " + mottatt.Melding.MeldingId + " " + mottatt.Melding.MeldingType + " mottas...");

                //TODO håndtere meldingen med ønsket funksjonalitet

                Console.WriteLine("Melding er håndtert i ePlansak ok ......");

                    mottatt.SvarSender.Ack(); // Ack message to remove it from the queue

            }
            else
            {
                Console.WriteLine("Ubehandlet melding i køen " + mottatt.Melding.MeldingId + " " + mottatt.Melding.MeldingType);
                //mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
            }
        }

        private static List<List<string>> ValidateJsonFile(string jsonString, string pathToSchema)
        {
            List<List<string>> validationErrorMessages = new List<List<string>>() { new List<string>(), new List<string>() };
            using (TextReader file = File.OpenText(pathToSchema))
            {
                JObject jObject = JObject.Parse(jsonString);
                JSchema schema = JSchema.Parse(file.ReadToEnd());
                //TODO:Skille mellom errors og warnings hvis det er 
                jObject.Validate(schema, (o, a) =>
                {
                    validationErrorMessages[0].Add(a.Message);
                });
            }
            return validationErrorMessages;
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
