# 👹 Monster Door Soundboard

A touch-screen soundboard for a Halloween "monster door" act: the door is the monster, the
mailbox is its mouth, and a wireless speaker outside does the talking. Open the page on a
tablet, connect the tablet to the speaker (Bluetooth/AirPlay), and tap.

- 20 big labelled buttons (growls, eating noises, spoken lines), colour-coded per group
- Sounds are preloaded into memory and fire on touch-down, so there's no delay
- **Layer** toggle: off = a new sound cuts off the previous one (natural for one mouth), on = sounds play over each other
- **STOP** button silences everything
- Keeps the screen awake, goes fullscreen, and works offline once loaded (if the Wi-Fi at the door drops, it keeps working)
- Keyboard shortcuts on a laptop: `1`–`0` and `Q`–`P`, `Space`/`Esc` = stop

## Deploy on Railway

1. Push this repo to GitHub.
2. In Railway: **New Project → Deploy from GitHub repo** → pick this repo.
3. Once deployed: **Settings → Networking → Generate Domain**.
4. Open that URL on the tablet.

Railway detects Node from `package.json` and runs `node server.js` (see `railway.json`).
There are no npm dependencies. The server listens on `$PORT` and has a `/health` endpoint.

Or with the Railway CLI:

```bash
railway init
railway up
railway domain
```

## On the tablet

1. Open the URL and **add it to the home screen** (Safari: Share → Add to Home Screen;
   Chrome: ⋮ → Add to Home screen). Launched from there it runs fullscreen like an app.
2. Tap "Tap to wake the monster" once — browsers only allow audio after a first tap.
3. Connect the tablet to the outdoor speaker and set the tablet volume to max; control the
   loudness on the speaker.
4. Turn off auto-lock as a backup (the page also asks the screen to stay on).
5. Bluetooth speakers add ~0.1–0.3 s of delay. Tap a little *before* you want the sound.

## Running locally

```bash
npm start
```

Then open http://localhost:3000.

## Changing the sounds

Buttons are defined in `public/sounds.json` (label, emoji, colour group, file). To use your
own recordings, drop an `.mp3` into `public/sounds/` and point an entry at it. Groups are
`growl` (red), `eat` (green), `body` (purple) and `talk` (orange). With more or fewer than 20
buttons, adjust the grid in `public/style.css` (`#board`).

After changing files, bump `CACHE` in `public/sw.js` so tablets that already cached the old
version pick up the new one.

### Regenerating the built-in sounds

All 20 sounds were generated, not downloaded, so there's no licensing to worry about:

- `tools/MonsterSynth.cs` synthesizes the growls, roar, chomps, slurp, burp, snore, etc.
- The spoken lines are recorded with Windows text-to-speech and turned into a monster voice
  by ffmpeg (pitched down, layered with a sub-voice, gravelly tremolo, distortion, echo).

To tweak and rebuild them (Windows, needs `ffmpeg` on PATH):

```bash
powershell -ExecutionPolicy Bypass -File tools/generate-sounds.ps1
```

The spoken phrases are listed at the top of that script. Change them to say anything,
in any language your Windows text-to-speech voices support.
