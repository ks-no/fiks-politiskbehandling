# fiks.io.eplansak.sample

## Oppsett av prosjekt
- Oppdater prosjektet med en egen appsettings.development.json som har med integrasjons passord i FIKS
- Oppdater med egen selvsignert krypteringsnøkkel (privkey.pem) som har lastet offentlig nøkkel opp i FIKS
- Bruk eget virksomhetssertifikat mot ID porten (ThumbprintIdPortenVirksomhetssertifikat)

## Bakgrunn
Dette er et forarbeid til arbeidsoppgaver i fornying av geointegrasjon for å vise muligheter og eksempler på FIKS IO integrasjon.
Flyten i meldinger baserer seg på brukstilfeller og prosessdiagram definert i ePlansak, Nasjonal produktspesifikasjon for arealplan og digitalt planregister(NPAD), samt arbeid med SOSI Plan 5.0.

 ![Meldingsflyt fra ePlansak til digitalt planregister](ks.fiks.io.eplansak.sample/doc/ePlansakflytmotplanregister.png)
 ![Meldingsflyt fra eByggesak til digitalt planregister](ks.fiks.io.eplansak.sample/doc/eByggesakflytmotplanregister.png)

## Oppsett i FIKS Integrasjon
TBC

## FIKS IO meldingsprotokoll
For ePlansak systemer så er meldingsprotokoll no.geointegrasjon.plan.oppdatering aktuell å støttes som avsender.
For digitalt planregister systemer så må meldingsprotokoll no.geointegrasjon.plan.oppdatering støttes som mottaker
En kan benytte lookup tjenesten for å finne ut hvilken kommune og konto som støtter aktuelle meldingsprotokoller

### Meldinger som sendes fra ePlansak til digitalt planregister
- Ved aktivitet Holde oppstartsmøte(2.2): [no.geointegrasjon.plan.oppdatering.oppretteplan.v1](ks.fiks.io.eplansak.sample/schema/no.geointegrasjon.plan.oppdatering.oppretteplan.v1.schema.json) [Eksempel json](ks.fiks.io.eplansak.sample/sampleOpprettePlan.json)
- Ved aktivitet Registrere planavgrensning(2.2.13): [no.geointegrasjon.plan.oppdatering.planavgrensning.v1](ks.fiks.io.eplansak.sample/schema/no.geointegrasjon.plan.oppdatering.planavgrensning.v1.schema.json) [Eksempel json](ks.fiks.io.eplansak.sample/samplePlanavgrensning.json)
- Ved aktivitet Varsel om planoppstart(2.3): [no.geointegrasjon.plan.oppdatering.planleggingigangsatt.v1](ks.fiks.io.eplansak.sample/schema/no.geointegrasjon.plan.oppdatering.planleggingigangsatt.v1.schema.json) [Eksempel json](ks.fiks.io.eplansak.sample/samplePlanleggingIgangsatt.json)
- Ved aktivitet Motta planforslag(3.3): [no.geointegrasjon.plan.oppdatering.planforslagkomplett.v1](ks.fiks.io.eplansak.sample/schema/no.geointegrasjon.plan.oppdatering.planforslagkomplett.v1.schema.json) [Eksempel json](ks.fiks.io.eplansak.sample/samplePlanforslagKomplett.json)
- Ved aktivitet Offentlig ettersyn(5): [no.geointegrasjon.plan.oppdatering.besluttetoffentligettersyn.v1](ks.fiks.io.eplansak.sample/schema/no.geointegrasjon.plan.oppdatering.besluttetoffentligettersyn.v1.schema.json) [Eksempel json](ks.fiks.io.eplansak.sample/sampleOffentligEttersyn.json)
- Ved aktivitet Planvedtak(7.5): [no.geointegrasjon.plan.oppdatering.planvedtak.v1](ks.fiks.io.eplansak.sample/schema/no.geointegrasjon.plan.oppdatering.planvedtak.v1.schema.json) [Eksempel json](ks.fiks.io.eplansak.sample/samplePlanvedtak.json)
- TBC

### Mottak fra digitalt planregister til ePlansak
- Svarmelding med ny planident: no.geointegrasjon.plan.oppdatering.meldingomplanident.v1
- Svar når en melding er utført: no.ks.geointegrasjon.ok.v1
- TBC

### Meldinger som sendes fra eByggesak til digitalt planregister
- Ved vedtak i byggesak som inneholder dispensasjon fra arealplan: no.geointegrasjon.plan.oppdatering.registrereplandispensasjon.v1
- Svar når en melding er utført: no.ks.geointegrasjon.ok.v1
- TBC
