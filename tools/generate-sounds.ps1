# Regenerates every sound in public/sounds/.
# Requirements: Windows PowerShell 5.1 (built in) and ffmpeg on PATH.
#   powershell -ExecutionPolicy Bypass -File tools\generate-sounds.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'public\sounds'
$raw = Join-Path ([IO.Path]::GetTempPath()) 'monster-soundboard-raw'
New-Item -ItemType Directory -Force $out, $raw | Out-Null

# 1. Synthesized growls, eating noises etc.
Add-Type -TypeDefinition (Get-Content -Raw (Join-Path $PSScriptRoot 'MonsterSynth.cs'))
[Synth]::RenderAll($raw)
Write-Host 'Synthesized raw sounds.'

# 2. Spoken lines, turned into a monster voice by ffmpeg below.
$lines = [ordered]@{
    'evil-laugh'      = @{ text = 'Mwah ha ha ha ha ha haaa!'; rate = -1 }
    'feed-me'         = @{ text = 'Feed me!'; rate = -4 }
    'i-smell-you'     = @{ text = 'I smell... children.'; rate = -3 }
    'trick-or-treat'  = @{ text = 'Trick or treat!'; rate = -2 }
    'come-closer'     = @{ text = 'Come closer... closer...'; rate = -4 }
    'give-me-candy'   = @{ text = 'Give me your candy!'; rate = -2 }
    'delicious'       = @{ text = 'Mmmm... delicious!'; rate = -3 }
    'happy-halloween' = @{ text = 'Happy Halloween!'; rate = -2 }
}
Add-Type -AssemblyName System.Speech
$tts = New-Object System.Speech.Synthesis.SpeechSynthesizer
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050, 'Sixteen', 'Mono')
foreach ($id in $lines.Keys) {
    $tts.Rate = $lines[$id].rate
    $tts.SetOutputToWaveFile((Join-Path $raw "$id.voice.wav"), $fmt)
    $tts.Speak($lines[$id].text)
}
$tts.SetOutputToNull()
$tts.Dispose()
Write-Host 'Recorded spoken lines.'

# 3. Post-process and encode to MP3.
$master = 'aecho=0.8:0.4:40|75:0.2|0.12,apad=pad_dur=0.25,loudnorm=I=-15:TP=-1.5:LRA=11,aresample=44100'
# Two pitched-down copies of the voice (one an extra fifth lower with a gravelly tremolo),
# both stretched to the same length, mixed and lightly overdriven.
$voice = '[0:a]asplit=2[a][b];' +
    '[a]asetrate=13671,aresample=44100,atempo=1.2[a2];' +
    '[b]asetrate=10143,aresample=44100,atempo=1.6174,tremolo=f=28:d=0.4[b2];' +
    '[a2][b2]amix=inputs=2:weights=''1 0.7'':normalize=0,volume=2.5,asoftclip=type=tanh,lowpass=f=5000,' + $master

Get-ChildItem $raw -Filter *.wav | ForEach-Object {
    if ($_.Name -like '*.voice.wav') {
        $id = $_.Name -replace '\.voice\.wav$', ''
        $ffArgs = @('-filter_complex', $voice)
    } else {
        $id = $_.BaseName
        $ffArgs = @('-af', "lowpass=f=7500,$master")
    }
    & ffmpeg -hide_banner -loglevel error -y -i $_.FullName @ffArgs -ac 1 -c:a libmp3lame -q:a 3 (Join-Path $out "$id.mp3")
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed on $($_.Name)" }
    Write-Host "  $id.mp3"
}
Remove-Item -Recurse -Force $raw
Write-Host "Done: $out"
