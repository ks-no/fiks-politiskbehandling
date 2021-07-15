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
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace ks.fiks.io.politiskbehandling.sample
{

    public class UtvalgService : IHostedService, IDisposable
    {
        FiksIOClient client;
        private readonly AppSettings appSettings;


        public UtvalgService(AppSettings appSettings)
        {
            this.appSettings = appSettings;
            client = CreateFiksIoClient();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("Utvalg Service is starting.");
            Console.WriteLine("Setter opp FIKS integrasjon for politisk behandling...");

            client.NewSubscription(OnReceivedMelding);

            Console.WriteLine("Abonnerer på meldinger på konto " + appSettings.FiksIOConfig.FiksIoAccountId + " ...");

            return Task.CompletedTask;
        }

        private FiksIOClient CreateFiksIoClient()
        {
            Console.WriteLine("Setter opp FIKS integrasjon for arkivsystem...");
            var accountId = appSettings.FiksIOConfig.FiksIoAccountId;
            var privateKey = File.ReadAllText(appSettings.FiksIOConfig.FiksIoPrivateKey);
            var integrationId = appSettings.FiksIOConfig.FiksIoIntegrationId;
            var integrationPassword = appSettings.FiksIOConfig.FiksIoIntegrationPassword;
            var scope = appSettings.FiksIOConfig.FiksIoIntegrationScope;
            var audience = appSettings.FiksIOConfig.MaskinPortenAudienceUrl;
            var tokenEndpoint = appSettings.FiksIOConfig.MaskinPortenTokenUrl;
            var issuer = appSettings.FiksIOConfig.MaskinPortenIssuer;

            var ignoreSSLError = Environment.GetEnvironmentVariable("AMQP_IGNORE_SSL_ERROR");


            // Fiks IO account configuration
            var account = new KontoConfiguration(
                                accountId,
                                privateKey);

            // Id and password for integration associated to the Fiks IO account.
            var integration = new IntegrasjonConfiguration(
                                    integrationId,
                                    integrationPassword, scope);

            // ID-porten machine to machine configuration
            var maskinporten = new MaskinportenClientConfiguration(
                audience: audience,
                tokenEndpoint: tokenEndpoint,
                issuer: issuer,
                numberOfSecondsLeftBeforeExpire: 10,
                certificate: GetCertificate(appSettings));

            // Optional: Use custom api host (i.e. for connecting to test api)
            var api = new ApiConfiguration(
                scheme: appSettings.FiksIOConfig.ApiScheme,
                host: appSettings.FiksIOConfig.ApiHost,
                port: appSettings.FiksIOConfig.ApiPort);

            var sslOption1 = (!string.IsNullOrEmpty(ignoreSSLError) && ignoreSSLError == "true")
                ? new SslOption()
                {
                    Enabled = true,
                    ServerName = appSettings.FiksIOConfig.AmqpHost,
                    CertificateValidationCallback =
                        (RemoteCertificateValidationCallback)((sender, certificate, chain, errors) => true)
                }
                : null;


            // Optional: Use custom amqp host (i.e. for connection to test queue)
            var amqp = new AmqpConfiguration(
                host: appSettings.FiksIOConfig.AmqpHost, //"io.fiks.test.ks.no",
                port: appSettings.FiksIOConfig.AmqpPort,
                sslOption1);

            // Combine all configurations
            var configuration = new FiksIOConfiguration(account, integration, maskinporten, api, amqp);
            return new FiksIOClient(configuration);
        }

        static void OnReceivedMelding(object sender, MottattMeldingArgs mottatt)
        {
            //Se oversikt over meldingstyper på https://github.com/ks-no/fiks-io-meldingstype-katalog/tree/test/schema

            // Process the message
            if (mottatt.Melding.MeldingType == "no.ks.fiks.politisk.behandling.klient.hentutvalg.v1")
            {
                Console.WriteLine("Melding " + mottatt.Melding.MeldingId + " " + mottatt.Melding.MeldingType + " mottas...");

                string payload = File.ReadAllText("sampleResultatUtvalg.json");

                var errorMessages = ValidateJsonFile(payload, Path.Combine("schema", "no.ks.fiks.politisk.behandling.resultatutvalg.v1.schema.json"));

                if (errorMessages[0].Count == 0)
                {
                    var svarmsg = mottatt.SvarSender.Svar("no.ks.fiks.politisk.behandling.tjener.resultatutvalg.v1", payload, "resultat.json").Result;
                    Console.WriteLine("Svarmelding " + svarmsg.MeldingId + " " + svarmsg.MeldingType + " sendt...");
                    Console.WriteLine(payload);
                    mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                }
                else
                {
                    Console.WriteLine("Feil i validering av utvalg");
                    mottatt.SvarSender.Svar("no.ks.fiks.kvittering.ugyldigforespørsel.v1", String.Join("\n ", errorMessages[0]), "feil.txt");
                    Console.WriteLine(String.Join("\n ", errorMessages[0]));
                    mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
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
                            Console.WriteLine("Feil i validering av resultatmøteplan");
                            mottatt.SvarSender.Svar("no.ks.fiks.kvittering.ugyldigforespørsel.v1", String.Join("\n ", errorMessages[0]), "feil.txt");
                            Console.WriteLine(String.Join("\n ", errorMessages[0]));
                            mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                        }
                    }
                    else
                    {
                        Console.WriteLine("Feil i validering av hentmøteplan");
                        mottatt.SvarSender.Svar("no.ks.fiks.kvittering.ugyldigforespørsel.v1", String.Join("\n ", errorMessages[0]), "feil.txt");
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
                        mottatt.SvarSender.Svar("no.ks.fiks.kvittering.ugyldigforespørsel.v1", String.Join("\n ", errorMessages[0]), "feil.txt");
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
                        mottatt.SvarSender.Svar("no.ks.fiks.kvittering.ugyldigforespørsel.v1", String.Join("\n ", errorMessages[0]), "feil.txt");
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

        private static X509Certificate2 GetCertificate(AppSettings appSettings)
        {
            if (!string.IsNullOrEmpty(appSettings.FiksIOConfig.MaskinPortenCompanyCertificatePath))
            {
                return new X509Certificate2(File.ReadAllBytes(appSettings.FiksIOConfig.MaskinPortenCompanyCertificatePath), appSettings.FiksIOConfig.MaskinPortenCompanyCertificatePassword);
            }

            var store = new X509Store(StoreLocation.CurrentUser);

            store.Open(OpenFlags.ReadOnly);

            var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, appSettings.FiksIOConfig.MaskinPortenCompanyCertificateThumbprint, false);

            store.Close();

            return certificates.Count > 0 ? certificates[0] : null;
        }
    }

}
