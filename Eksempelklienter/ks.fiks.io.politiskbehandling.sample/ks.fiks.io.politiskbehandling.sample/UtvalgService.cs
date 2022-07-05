using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using KS.Fiks.ASiC_E;
using KS.Fiks.IO.Client;
using KS.Fiks.IO.Client.Configuration;
using KS.Fiks.IO.Client.Models;
using KS.Fiks.IO.Politiskbehandling.Client.Models;
using Ks.Fiks.Maskinporten.Client;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using RabbitMQ.Client;
using Serilog;

namespace ks.fiks.io.politiskbehandling.sample
{

    public class UtvalgService : IHostedService, IDisposable
    {
        FiksIOClient client;
        private readonly AppSettings appSettings;
        private static readonly ILogger Log = Serilog.Log.ForContext(MethodBase.GetCurrentMethod()?.DeclaringType);

        public UtvalgService(AppSettings appSettings)
        {
            this.appSettings = appSettings;
            client = CreateFiksIoClient();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Log.Information("Utvalg Service is starting");
            Log.Information("Setter opp FIKS integrasjon for politisk behandling");
            
            client.NewSubscription(OnReceivedMelding);

            Log.Information($"Abonnerer på meldinger på konto {appSettings.FiksIOConfig.FiksIoAccountId} ...");

            return Task.CompletedTask;
        }

        private FiksIOClient CreateFiksIoClient()
        {
            Log.Information("Setter opp FIKS integrasjon for arkivsystem...");
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

        private static void OnReceivedMelding(object sender, MottattMeldingArgs mottatt)
        {
            Log.Information("Melding med meldingid {MeldingID} og type {MeldingType} håndteres", mottatt.Melding.MeldingId, mottatt.Melding.MeldingType);
            switch (mottatt.Melding.MeldingType)
            {
                // Process the message
                case PolitiskBehandlingMeldingTypeV1.HentUtvalg:
                    HandleHentUtvalg(mottatt);
                    break;
                case PolitiskBehandlingMeldingTypeV1.HentMoeteplan:
                    HandleHentMoeteplan(mottatt);
                    break;
                case PolitiskBehandlingMeldingTypeV1.SendUtvalgssak:
                    HandleSendUtvalgssak(mottatt);
                    break;
                case PolitiskBehandlingMeldingTypeV1.SendOrienteringssak:
                    HandleSendOrienteringssak(mottatt);
                    break;
                case PolitiskBehandlingMeldingTypeV1.SendDelegertVedtak:
                    //TODO håndtere meldingen med ønsket funksjonalitet
                    var svarmsg = mottatt.SvarSender.Svar(PolitiskBehandlingMeldingTypeV1.SendDelegertVedtakKvittering).Result;
                    Log.Information("Svarmelding på {MeldingID} med svar {MeldingID} type {MeldingType} sendt", mottatt.Melding.MeldingId, svarmsg.MeldingId, svarmsg.MeldingType );
                    mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                    break;
                case PolitiskBehandlingMeldingTypeV1.SendVedtakFraUtvalgKvittering:
                    //TODO håndtere meldingen med ønsket funksjonalitet
                    Log.Information("Melding SendVedtakFraUtvalgKvittering er håndtert i ePlansak ok");
                    mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                    break;
                default:
                    Log.Error("OBS! Ubehandlet melding i køen {MeldingID} type {MeldingType}", mottatt.Melding.MeldingId,  mottatt.Melding.MeldingType);
                    //We dont ACK the message. Let it go to timeout in Fiks-IO
                    break;
            }
        }

        private static void HandleSendOrienteringssak(MottattMeldingArgs mottatt)
        {
            if (mottatt.Melding.HasPayload)
            {
                var errorMessages = ValidatePayload(mottatt, PolitiskBehandlingMeldingTypeV1.SendOrienteringssak);
                
                if (errorMessages[0].Count == 0)
                {
                    var svarmsg = mottatt.SvarSender.Svar(PolitiskBehandlingMeldingTypeV1.SendOrienteringssakKvittering).Result;
                    Log.Information("Svarmelding på {MeldingID} med svar {MeldingID} type {MeldingType} sendt", mottatt.Melding.MeldingId, svarmsg.MeldingId, svarmsg.MeldingType );
                    mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                }
                else
                {
                    Log.Error("Feil i validering av sendorienteringssak");
                    mottatt.SvarSender.Svar("", string.Join("\n ", errorMessages[0]),
                        "feil.txt");
                    Log.Error(string.Join("\n ", errorMessages[0]));
                    mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                }
            }
            else
            {
                var svarmsg = mottatt.SvarSender
                    .Svar(PolitiskBehandlingMeldingTypeV1.Ugyldigforespørsel, "Meldingen mangler innhold", "feil.txt").Result;
                Log.Error("Svarmelding på {MeldingID} med svar {MeldingID} type {MeldingType}, mottatt melding mangler innhold", mottatt.Melding.MeldingId, svarmsg.MeldingId, svarmsg.MeldingType);
                mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
            }
        }

        private static void HandleHentUtvalg(MottattMeldingArgs mottatt)
        {
            var payload = File.ReadAllText("sampleResultatUtvalg.json");

            var errorMessages = ValidateJsonFile(payload,
                Path.Combine("Schema", PolitiskBehandlingMeldingTypeV1.ResultatUtvalg + ".schema.json"));

            if (errorMessages[0].Count == 0)
            {
                var svarmsg = mottatt.SvarSender.Svar(PolitiskBehandlingMeldingTypeV1.ResultatUtvalg, payload, "resultat.json")
                    .Result;
                Log.Information("Svarmelding på {MeldingID} med svar {MeldingID} type {MeldingType} sendt", mottatt.Melding.MeldingId, svarmsg.MeldingId, svarmsg.MeldingType );
                Log.Debug(payload);
                mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
            }
            else
            {
                Log.Error("Feil i validering av utvalg");
                mottatt.SvarSender.Svar(PolitiskBehandlingMeldingTypeV1.Ugyldigforespørsel, string.Join("\n ", errorMessages[0]),
                    "feil.txt");
                Log.Error("Feilmeldinger:" + string.Join("\n ", errorMessages[0]));
                mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
            }
        }

        private static void HandleSendUtvalgssak(MottattMeldingArgs mottatt)
        {
            if (mottatt.Melding.HasPayload)
            {
                var errorMessages = ValidatePayload(mottatt, PolitiskBehandlingMeldingTypeV1.SendUtvalgssak);

                if (errorMessages[0].Count == 0)
                {
                    var svarmsg = mottatt.SvarSender.Svar(PolitiskBehandlingMeldingTypeV1.SendUtvalgssakKvittering).Result;
                    Log.Information("Svarmelding på {MeldingID} med svar {MeldingID} type {MeldingType} sendt", mottatt.Melding.MeldingId, svarmsg.MeldingId, svarmsg.MeldingType);
                    mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                }
                else
                {
                    Log.Error("Feil i validering av sendutvalgsak");
                    mottatt.SvarSender.Svar(PolitiskBehandlingMeldingTypeV1.Ugyldigforespørsel, string.Join("\n ", errorMessages[0]),
                        "feil.txt");
                    Log.Error("Feilmelding: " + string.Join("\n ", errorMessages[0]));
                    mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                }
            }
            else
            {
                var svarmsg = mottatt.SvarSender
                    .Svar(PolitiskBehandlingMeldingTypeV1.Ugyldigforespørsel, "Meldingen mangler innhold", "feil.txt").Result;
                Log.Error("Svarmelding på mottatt melding {MeldingID} med svar {MeldingID} type {MeldingType}, mottatt melding mangler innhold", mottatt.Melding.MeldingId, svarmsg.MeldingId, svarmsg.MeldingType);
                mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
            }
        }

        private static void HandleHentMoeteplan(MottattMeldingArgs mottatt)
        {
            if (mottatt.Melding.HasPayload)
            {
                var errorMessages = ValidatePayload(mottatt, PolitiskBehandlingMeldingTypeV1.HentMoeteplan);
                
                if (errorMessages[0].Count == 0)
                {
                    var payload = File.ReadAllText("sampleResultat.json");

                    errorMessages = ValidateJsonFile(payload,
                        Path.Combine("Schema", PolitiskBehandlingMeldingTypeV1.ResultatMoeteplan + ".schema.json"));

                    if (errorMessages[0].Count == 0)
                    {
                        var svarmsg = mottatt.SvarSender
                            .Svar(PolitiskBehandlingMeldingTypeV1.ResultatMoeteplan, payload, "resultat.json").Result;
                        Log.Information(
                            "Svarmelding på {MeldingID} med svar {MeldingID} type {MeldingType} sendt", mottatt.Melding.MeldingId, svarmsg.MeldingId, svarmsg.MeldingType);
                        Log.Information(payload);
                        mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                    }
                    else
                    {
                        Log.Error("Feil i validering av resultatmøteplan");
                        mottatt.SvarSender.Svar(PolitiskBehandlingMeldingTypeV1.Ugyldigforespørsel,
                            string.Join("\n ", errorMessages[0]), "feil.txt");
                        Log.Error("Feilmelding: " + string.Join("\n ", errorMessages[0]));
                        mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                    }
                }
                else
                {
                    Log.Error("Feil i validering av hentmøteplan");
                    mottatt.SvarSender.Svar(PolitiskBehandlingMeldingTypeV1.Ugyldigforespørsel, string.Join("\n ", errorMessages[0]),
                        "feil.txt");
                    Log.Error("Feilmelding: " + string.Join("\n ", errorMessages[0]));
                    mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
                }
            }
            else
            {
                var svarmsg = mottatt.SvarSender
                    .Svar(PolitiskBehandlingMeldingTypeV1.Ugyldigforespørsel, "Meldingen mangler innhold", "feil.txt").Result;
                Log.Error("Svarmelding på {MeldingID} med svar {MeldingID} type {MeldingType}, meldingen mangler innhold", mottatt.Melding.MeldingId, svarmsg.MeldingId, svarmsg.MeldingType);

                mottatt.SvarSender.Ack(); // Ack message to remove it from the queue
            }
        }

        private static List<List<string>> ValidatePayload(MottattMeldingArgs mottatt, string meldingsType)
        {
            var errorMessages = new List<List<string>>() {new(), new()};
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
                            errorMessages = ValidateJsonFile(new StreamReader(entryStream).ReadToEnd(),
                                Path.Combine("Schema", $"{meldingsType}.schema.json"));
                        }
                        else
                            Log.Information($"Mottatt vedlegg: {asiceReadEntry.FileName}");
                    }
                }
            }

            return errorMessages;
        }

        private static List<List<string>> ValidateJsonFile(string jsonString, string pathToSchema)
        {
            var validationErrorMessages = new List<List<string>>() { new List<string>(), new List<string>() };
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
            Log.Information("Politisk behandling Service is stopping");
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
