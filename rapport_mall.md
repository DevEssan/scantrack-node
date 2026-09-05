# Rapport — ScanTrack Node

**Namn:** Essan Alshakerchi
**Stad (nod):** Trollhättan
**Datum:** 2026-09-05
**Kurs:** Administrera molnlösningar — ITHS

**Live-nod:** http://scantrack-trollhattan.swedencentral.azurecontainer.io:8080/status
**GitHub:** https://github.com/DevEssan/scantrack-node

---

## Arkitektur

```
 GITHUB
   marcusjobb/scantrack-node        Marcus startkod
        |  fork
        v
   DevEssan/scantrack-node          min fork, det är den jag lämnar in
        |  git clone / git push
        v
 MIN DATOR
   D:\scantrack-node                Dockerfile, .dockerignore, data/cities.csv
        |  docker build   steg 1: sdk:8.0 kompilerar (ca 900 MB)
        |                 steg 2: aspnet:8.0 + färdig app (ca 200 MB)
        v
   image: scantrack-node
        |  docker push :v1
        v
 AZURE (swedencentral)
   essanscantrackacr                Container Registry
        |  az container create --dns-name-label scantrack-trollhattan
        v
   scantrack-trollhattan            Container Instance, port 8080
   http://scantrack-trollhattan.swedencentral.azurecontainer.io:8080
        |                                      ^
        | POST /nodes                          | POST /paket
        | (vid start och sen varje timme)      |
        v                                      |
   Registret (Marcus)  ---- GET /nodes ---->  andra noder
```

Jag forkade Marcus repo till mitt eget GitHub-konto och klonade forken till min dator. Marcus repo ligger kvar som "upstream" i git så jag kunde hämta hans uppdateringar under tiden (han lade till Kiruna och att cities.csv hämtas från registret). Allt jag ändrar går till min fork.

På datorn bygger jag en image med docker build. Bygget är i två steg. Det första steget använder SDK-imagen och kompilerar koden, det andra steget startar från den mindre runtime-imagen och kopierar bara in den färdiga appen. Sen pushar jag imagen till mitt Container Registry i Azure och startar en Container Instance från den.

När containern startar registrerar den sig mot Marcus register med POST /nodes (stad + adress). HeartbeatService gör samma sak en gång i timmen, för registret tar bort noder som inte hört av sig på två timmar. Andra noder hämtar min adress från registret och skickar paket till POST /paket. Om destinationen är Trollhättan sparas paketet, annars räknar Dijkstra ut nästa stad och ForwardAsync skickar det vidare.

## NODE_URL-problemet

Noden måste skicka sin egen adress till registret när den startar. Problemet är att man inte vet IP-adressen förrän containern är skapad. Så för att skapa containern behöver man IP:n, och för att få IP:n måste containern finnas.

Dessutom byts IP:n varje gång containern skapas om. Min container har haft fyra olika IP-adresser på två dagar (57.174.180.254, 4.223.3.8, 74.241.187.34 och 4.223.3.0). Första gången jag deployade satte jag NODE_URL till en IP, och när containern skapades om pekade den på fel adress.

Lösningen var att inte använda IP-adressen. Med --dns-name-label scantrack-trollhattan väljer jag ett namn själv innan containern finns. Azure gör namnet till scantrack-trollhattan.swedencentral.azurecontainer.io (namnet + region + azurecontainer.io), och eftersom det mönstret är fast kan jag skriva in hela adressen som NODE_URL i samma kommando som skapar containern. Azure ser till att namnet pekar på rätt IP, även om IP:n byts.

En sak att tänka på: DNS-namn får bara ha a-z, siffror och bindestreck. Containern heter därför scantrack-trollhattan utan ä, men CITY_NAME är fortfarande Trollhättan med ä eftersom det måste matcha cities.csv.

---

## Del 1 — Tillvägagångssätt

### Hur du byggde och testade Docker-imagen lokalt

I Dockerfilen kopierar jag först bara .csproj-filen och kör dotnet restore, sen kopierar jag resten av koden och kör dotnet publish -c Release --no-restore. Jag bygger från repo-roten eftersom cities.csv ligger i data/ utanför projektmappen.

```
docker build -f ScanTrackNode/Dockerfile -t scantrack-node .
docker run -d --rm -p 8080:8080 -e CITY_NAME="Trollhättan" -e NODE_URL=http://localhost:8080 -e REGISTRY_URL=http://localhost:9999 scantrack-node
```

Sen testade jag GET /status (svarade Trollhättan), POST /paket med destination Trollhättan (svarade levererat och syntes i loggen) och POST /forceheartbeat två gånger. Andra gången fick jag 429 för att spärren på 10 minuter slog till, vilket var meningen. REGISTRY_URL pekade på en port där inget lyssnar så att jag inte skulle registrera localhost i det riktiga registret.

### Hur du publicerade imagen till ACR

Registret heter essanscantrackacr och ligger i skolans resursgrupp. Jag körde az acr login, taggade imagen som v1 och pushade. Bara två lager laddades upp, resten fanns redan från ett tidigare försök.

### Hur du startade noden i ACI

Kommandot finns i Del 4. Jag satte tre miljövariabler: CITY_NAME=Trollhättan, NODE_URL med DNS-namnet, och REGISTRY_URL till Marcus register. I ett försök föll REGISTRY_URL bort och då kraschade noden direkt vid start, så alla tre behövs.

### Hur du verifierade att noden fungerade

GET /status från Azure svarar Trollhättan med DNS-adressen. I loggen står "Nod registrerad: Trollhättan → http://scantrack-trollhattan..." och i registrets /nodes finns Trollhättan med. När jag skickar ett paket till noden står det "anlände till Trollhättan" och "levererat till Trollhättan!" i loggen.

Jag kunde inte testa att skicka vidare till en annan stad. Trollhättan har bara två grannar i kartan, Borås och Karlstad, och ingen av dem var online. Dijkstra går bara via städer som finns i registret, så paketet stoppas med 422 "ingen väg hittades". Det är rätt beteende, och när Borås, Karlstad eller Jukkasjärvi kommer upp funkar det utan att jag ändrar något.

---

## Del 2 — Reflektion

### Vad har du lärt dig?

1. **COPY-ordningen i Dockerfilen.** Docker sparar varje rad som ett lager och återanvänder lagret om inget före det har ändrats. Om man kopierar all kod innan dotnet restore så körs restore om vid varje kodändring och alla NuGet-paket laddas ner igen. Kopierar man bara .csproj först så ligger restore-lagret kvar tills paketlistan ändras. Jag såg det själv: första bygget tog 37 sekunder, och när jag ändrade en rad kod och byggde om tog det 7 sekunder. I utdatan stod CACHED på både COPY csproj och RUN dotnet restore. Jag märkte också att det inte funkar utan .dockerignore. Min obj-mapp från Windows följde med in i imagen och --no-restore kraschade på Windows-sökvägar inne i Linux-containern.

2. **Varför ACI och inte App Service eller AKS.** Container Instances startar en container på under två minuter, kostar per sekund och man behöver inte sätta upp någon infrastruktur. Det passar en nod som ska tas bort efter inlämning. App Service är gjort för webbappar och bestämmer mer åt en (portar, skalning, deploy-slots) än vad jag behöver här. Kubernetes ger självläkning, autoskalning och uppdateringar utan avbrott, men man måste ha ett helt kluster. För en enda container är det för mycket. Om ScanTrack skulle köra alla 23 städer på riktigt med krav på drifttid hade AKS varit rätt val.

3. **En image är en ögonblicksbild.** Ändrar jag koden måste jag bygga och pusha om, annars kör Azure den gamla koden. Min första image i Azure hade en ForwardAsync som inte var implementerad, och den låg kvar tills jag pushade v1.

### Vad var svårast?

Inte koden, den var nästan klar från början. Det svåraste var att förstå vad som faktiskt var fel, för felen såg sällan ut som det verkliga problemet.

Första paketet i Azure gav 500. Jag trodde det var nätverket. I loggen stod NotImplementedException i ForwardAsync, jag hade deployat en image där metoden inte var skriven. Sen fick jag 422 "ingen väg hittades" och la tid på att det stod Gothenburg istället för Göteborg i registret. Men det var inte det. Mina grannar Borås och Karlstad var inte online, och då finns det ingen väg oavsett hur Göteborg stavas.

Azure CLI gav också ett fel om att osType var ogiltigt fast jag inte hade skrivit något osType. Det var för att jag försökte skapa containern innan den gamla var borttagen. PowerShell tappade halva NODE_URL för att jag skrev $FQDN:8080 istället för ${FQDN}:8080. Och åäö i JSON blev "Goteborg" tills jag skickade bodyn som bytes från en fil istället för som text.

Det jag lärde mig av allt det här är att läsa loggen först innan jag gissar.

### Hur kan du ha nytta av det du lärt dig i framtiden?

Det här är sånt man gör hela tiden på ett jobb: bygga en image, pusha till ett registry, köra den i molnet och felsöka via loggar. Layercachning avgör om en pipeline tar en minut eller tio. Att peka på ett namn istället för en IP är samma tänk överallt, man litar inte på något som kan byta adress. Och .dockerignore är skillnaden mellan en image på 200 MB och en på flera GB med hela .git och bin inbakat.

### Vad saknas jämfört med en riktig produktionsmiljö?

1. Jag använder admin-lösenordet till ACR. Marcus kallade det huvudnyckeln till hela byggnaden. I produktion skulle containern ha en managed identity med AcrPull-rollen och hemligheter skulle ligga i Key Vault. All trafik går dessutom över vanlig http, inte https.

2. Det finns ingen health check. Utan livenessProbe räknas containern som frisk så fort processen har startat. Om appen hänger sig startar Azure inte om den. Registret tar bort noder som inte skickat heartbeat på två timmar, men själva containern övervakas inte.

3. Ingen persistens och ingen redundans. PackageStore skriver till /data/packages.json inne i containern, så vid omstart är historiken borta. På riktigt hade man haft en volym eller databas, flera instanser, och en CI/CD-pipeline istället för att skriva az-kommandon för hand.

### Vad är skillnaden mellan att köra en app i en container och direkt på en server?

En container har med sig exakt den runtime och de bibliotek appen behöver, och beter sig likadant på min dator som i Azure. På en server delar alla appar samma .NET-version och samma konfiguration, och då får man problemet "det funkar på min maskin". Nackdelen med container är att man måste bygga om den vid varje ändring, och det är därför cachningen är så viktig.

### Varför skickar varje nod med historiken i paketet?

För att undvika loopar. Dijkstra hoppar över städer som redan finns i History. Utan det hade ett paket till en stad som är offline kunnat studsa Trollhättan → Borås → Trollhättan → Borås hur länge som helst. Med historiken ser Borås att paketet redan varit i Trollhättan och svarar 422 istället för att skicka tillbaka det.

### Individuell reflektion (Essan)

Det jag förstår nu som jag inte förstod innan är hur mycket som händer runt omkring koden. Koden var nästan färdig från start. Allt jag fastnade på handlade om miljön: vad som följer med in i imagen, att localhost inne i containern är containern själv och inte min dator, att man inte kan lita på IP-adressen, och att ett rött felmeddelande i PowerShell ofta bara betyder att servern svarade nej på ett kontrollerat sätt. 429 från /forceheartbeat var till exempel min egen spärr som fungerade.

Hade jag gjort om det hade jag gjort .dockerignore och forken först och inte sist. Jag hade använt --dns-name-label redan första gången istället för en IP som sen byttes ut. Och jag hade testat allt lokalt innan jag pushade till Azure. Den första imagen jag la upp hade aldrig fungerat.

En sak i koden jag inte är nöjd med är att HeartbeatService loggar "Heartbeat sent successfully" även när registret inte svarar, för RegisterSelfAsync fångar felet och säger inget. Det är missvisande. Hade jag gjort om det hade jag låtit metoden returnera om det gick bra eller inte.

---

## Del 3 — Gruppreflektion

### Hur fungerade samarbetet i gruppen?

*(Fyll i: vem gjorde vad, jobbade ni parallellt eller tillsammans.)*

### Var det något som blockerade gruppen? Hur löste ni det?

Det som blockerade mest var att nätverket runt Trollhättan inte var uppe. Utan Borås eller Karlstad online går det inte att visa att paket skickas vidare. Jag testade allt som gick att testa (leverans till egen stad, registrering, heartbeat) och skrev ner varför resten väntar på andra noder.

### Vad skulle ni göra annorlunda?

Testa hela kedjan lokalt innan första deployen, och stämma av med Borås och Karlstad om när deras noder är uppe så att vi kan testa paket mellan oss.

---

## Del 4 — Teknisk logg

**Kommando 1:**
```bash
docker build -f ScanTrackNode/Dockerfile -t scantrack-node .
```
*Vad gör det:* Bygger imagen från repo-roten så att både ScanTrackNode/ och data/cities.csv kommer med. -f pekar ut Dockerfilen och -t sätter namnet. .dockerignore ser till att bin/, obj/, .git/ och .env inte följer med.

**Kommando 2:**
```bash
docker push essanscantrackacr.azurecr.io/scantrack-node:v1
```
*Vad gör det:* Laddar upp imagen till mitt Container Registry i Azure, ett lager i taget. Lager som redan finns hoppas över. Jag använder taggen v1 istället för latest så jag vet exakt vilken version som körs.

**Kommando 3:**
```bash
az container create --name scantrack-trollhattan --resource-group $RG --location swedencentral \
  --os-type Linux --cpu 1 --memory 1 \
  --image essanscantrackacr.azurecr.io/scantrack-node:v1 \
  --ports 8080 --ip-address Public --dns-name-label scantrack-trollhattan \
  --registry-login-server essanscantrackacr.azurecr.io \
  --registry-username essanscantrackacr --registry-password "$ACR_PASSWORD" \
  --environment-variables CITY_NAME="Trollhättan" \
    NODE_URL="http://scantrack-trollhattan.swedencentral.azurecontainer.io:8080" \
    REGISTRY_URL="http://scantrack-registry-iths.northeurope.azurecontainer.io:8080"
```
*Vad gör det:* Skapar containern i Azure från imagen i ACR, med publik IP och ett DNS-namn jag valt själv. Det är därför NODE_URL kan sättas rätt redan här. CPU och minne fick jag ange själv eftersom min version av Azure CLI inte fyllde i standardvärden.

---

## Bevis

Skärmdumpar bifogas:

1. GET /status från Azure som svarar Trollhättan med DNS-adressen
2. az container logs med "Nod registrerad", "anlände till Trollhättan" och "levererat till Trollhättan!"
3. docker build efter en kodändring med CACHED på COPY csproj och RUN dotnet restore
4. POST /forceheartbeat som svarar "heartbeat skickat"
