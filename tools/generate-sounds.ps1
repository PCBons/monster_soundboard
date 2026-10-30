# Maakt alle geluiden in public/sounds/ opnieuw aan.
# Vereist: Windows PowerShell 5.1 (standaard aanwezig), ffmpeg in het PATH en de
# Nederlandse Windows-stem "Microsoft Frank" (Instellingen > Tijd en taal > Spraak).
#   powershell -ExecutionPolicy Bypass -File tools\generate-sounds.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'public\sounds'
$raw = Join-Path ([IO.Path]::GetTempPath()) 'monster-soundboard-raw'
New-Item -ItemType Directory -Force $out, $raw | Out-Null
Remove-Item (Join-Path $out '*.mp3') -ErrorAction SilentlyContinue

# 1. Gesynthetiseerd gegrom, eetgeluiden enz.
Add-Type -TypeDefinition (Get-Content -Raw (Join-Path $PSScriptRoot 'MonsterSynth.cs'))
[Synth]::RenderAll($raw)
Write-Host 'Geluiden gesynthetiseerd.'

# 2. Gesproken zinnen (SSML), hieronder door ffmpeg omgevormd tot monsterstem.
$lines = [ordered]@{
    'lach'               = @{ text = 'Mwoe ha ha ha ha ha haaa!'; rate = '-5%' }
    'voer-mij'           = @{ text = 'Voer mij!'; rate = '-25%' }
    'ik-ruik-kinderen'   = @{ text = 'Ik ruik<break time="500ms"/> kinderen.'; rate = '-20%' }
    'snoep-of-ik-eet-je' = @{ text = 'Snoep, of ik eet je op!'; rate = '-10%' }
    'kom-dichterbij'     = @{ text = 'Kom maar dichterbij<break time="400ms"/> dichterbij.'; rate = '-25%' }
    'geef-snoep'         = @{ text = 'Geef mij je snoep!'; rate = '-10%' }
    'heerlijk'           = @{ text = 'Mmmmm<break time="300ms"/> heerlijk!'; rate = '-20%' }
    'fijne-halloween'    = @{ text = 'Fijne Halloween!'; rate = '-10%' }
}

Add-Type -AssemblyName System.Runtime.WindowsRuntime
$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
} | Select-Object -First 1
function Await($op, [Type]$type) {
    $task = $asTask.MakeGenericMethod($type).Invoke($null, @($op))
    $task.Wait(-1) | Out-Null
    $task.Result
}
[Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media.SpeechSynthesis, ContentType = WindowsRuntime] | Out-Null
$tts = New-Object Windows.Media.SpeechSynthesis.SpeechSynthesizer
$voiceInfo = [Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices | Where-Object { $_.Language -eq 'nl-NL' } | Select-Object -First 1
if (-not $voiceInfo) { throw 'Geen Nederlandse stem gevonden. Installeer de spraak voor Nederlands in Windows.' }
$tts.Voice = $voiceInfo
Write-Host "Stem: $($voiceInfo.DisplayName)"

foreach ($id in $lines.Keys) {
    $ssml = "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='nl-NL'>" +
        "<prosody rate='$($lines[$id].rate)'>$($lines[$id].text)</prosody></speak>"
    $stream = Await ($tts.SynthesizeSsmlToStreamAsync($ssml)) ([Windows.Media.SpeechSynthesis.SpeechSynthesisStream])
    $file = [IO.File]::Create((Join-Path $raw "$id.voice.wav"))
    [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($stream).CopyTo($file)
    $file.Close()
    $stream.Dispose()
}
$tts.Dispose()
Write-Host 'Zinnen ingesproken.'

# 3. Nabewerken en naar MP3 omzetten.
$master = 'aecho=0.8:0.4:40|75:0.2|0.12,apad=pad_dur=0.25,loudnorm=I=-15:TP=-1.5:LRA=11,aresample=44100'
# Twee verlaagde kopieën van de stem (de tweede nog lager, met een rauwe tremolo),
# allebei even lang gemaakt, gemengd en licht oversturend.
$voice = '[0:a]aresample=22050,asplit=2[a][b];' +
    '[a]asetrate=15435,aresample=44100,atempo=1.1[a2];' +
    '[b]asetrate=11466,aresample=44100,atempo=1.4808,tremolo=f=28:d=0.4[b2];' +
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
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg mislukt bij $($_.Name)" }
    Write-Host "  $id.mp3"
}
Remove-Item -Recurse -Force $raw
Write-Host "Klaar: $out"
