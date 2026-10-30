# 👹 Monsterdeur Geluidenbord

Een geluidenbord voor een touchscreen, voor een Halloween-act met een "monsterdeur": de deur
is het monster, de brievenbus is de mond en een draadloze speaker buiten laat het monster
praten. Open de pagina op een tablet, verbind de tablet met de speaker (Bluetooth/AirPlay) en tik.

- 20 grote knoppen met tekst (gegrom, eetgeluiden, gesproken zinnen), met een kleur per groep
- Geluiden worden vooraf in het geheugen geladen en starten zodra je het scherm aanraakt, dus zonder vertraging
- **Stapelen**-knop: uit = een nieuw geluid onderbreekt het vorige (logisch voor één mond), aan = geluiden spelen over elkaar heen
- **STOP**-knop zet alles stil
- Houdt het scherm aan, gaat naar volledig scherm en blijft werken als de wifi wegvalt (zodra alles één keer geladen is)
- Sneltoetsen op een laptop: `1`–`0` en `Q`–`P`, `Spatie`/`Esc` = stop

## Publiceren op Railway

1. Push deze repo naar GitHub.
2. In Railway: **New Project → Deploy from GitHub repo** → kies deze repo.
3. Na het deployen: **Settings → Networking → Generate Domain**.
4. Open die URL op de tablet.

Railway herkent Node aan `package.json` en start `node server.js` (zie `railway.json`).
Er zijn geen npm-afhankelijkheden. De server luistert op `$PORT` en heeft een `/health`-endpoint.

Of met de Railway CLI:

```bash
railway init
railway up
railway domain
```

## Op de tablet

1. Open de URL en **zet hem op het beginscherm** (Safari: Deel → Zet op beginscherm;
   Chrome: ⋮ → Toevoegen aan startscherm). Vanaf daar opent hij schermvullend, als een app.
2. Tik één keer op "Tik om het monster te wekken": browsers staan geluid pas toe na een eerste tik.
3. Verbind de tablet met de speaker buiten en zet het volume van de tablet op maximaal; regel
   het geluidsniveau op de speaker.
4. Zet automatische vergrendeling uit als extra zekerheid (de pagina vraagt zelf ook om het scherm aan te houden).
5. Bluetooth-speakers hebben ~0,1–0,3 s vertraging. Tik dus net *vóór* het moment dat je het geluid wilt.

## Lokaal draaien

```bash
npm start
```

Open daarna http://localhost:3000.

## Geluiden aanpassen

De knoppen staan in `public/sounds.json` (tekst, emoji, kleurgroep, bestand). Wil je eigen
opnames gebruiken, zet dan een `.mp3` in `public/sounds/` en laat een knop ernaar verwijzen.
De groepen zijn `growl` (rood), `eat` (groen), `body` (paars) en `talk` (oranje). Gebruik je
meer of minder dan 20 knoppen, pas dan het raster aan in `public/style.css` (`#board`).

Verhoog na het wijzigen van bestanden `CACHE` in `public/sw.js`, zodat tablets die de oude
versie al bewaard hebben de nieuwe ophalen.

### De ingebouwde geluiden opnieuw maken

Alle 20 geluiden zijn zelf gemaakt, niet gedownload, dus er zijn geen licentieproblemen:

- `tools/MonsterSynth.cs` synthetiseert het grommen, brullen, happen, slurpen, boeren, snurken enz.
- De gesproken zinnen worden ingesproken door de Nederlandse Windows-stem "Microsoft Frank" en
  door ffmpeg omgevormd tot monsterstem (verlaagd, gelaagd met een nog lagere stem, rauwe
  tremolo, vervorming en echo).

Om ze aan te passen en opnieuw te maken (Windows, `ffmpeg` moet in het PATH staan):

```bash
powershell -ExecutionPolicy Bypass -File tools/generate-sounds.ps1
```

De gesproken zinnen staan bovenaan dat script; je kunt het monster daar alles laten zeggen.
