<# 
  Generates OopsLayout.Core/Bigrams.cs from OpenSubtitles frequency lists.

  For each language we count character-bigram frequencies (weighted by word
  frequency), turn them into log10 joint probabilities, and bake the result
  into a C# file. The switcher scores a word under both the EN and RU model to
  decide whether it was typed in the wrong keyboard layout.

  Run:  powershell -ExecutionPolicy Bypass -File tools/gen-bigrams.ps1
#>

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$root   = Split-Path $PSScriptRoot -Parent
$outCs  = Join-Path $root 'OopsLayout.Core/Bigrams.cs'
$cache  = Join-Path $env:TEMP 'oopslayout-freq'
New-Item -ItemType Directory -Force -Path $cache | Out-Null

# build alphabets from code points so the script stays pure-ASCII (PS 5.1 reads .ps1 as ANSI)
$enAlphabet = 'abcdefghijklmnopqrstuvwxyz'
$ruAlphabet = -join (0x0430..0x044F | ForEach-Object { [char]$_ })  # а..я (ё folded to е elsewhere)
# Ukrainian alphabet — і ї є ґ live outside the а..я range, so list code points.
$ukAlphabet = -join (@(
  0x0430,0x0431,0x0432,0x0433,0x0491,0x0434,0x0435,0x0454,0x0436,0x0437,0x0438,
  0x0456,0x0457,0x0439,0x043A,0x043B,0x043C,0x043D,0x043E,0x043F,0x0440,0x0441,
  0x0442,0x0443,0x0444,0x0445,0x0446,0x0447,0x0448,0x0449,0x044C,0x044E,0x044F
) | ForEach-Object { [char]$_ })

$sources = @{
  en = @{ url = 'https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/en/en_50k.txt'; alphabet = $enAlphabet }
  ru = @{ url = 'https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/ru/ru_50k.txt'; alphabet = $ruAlphabet }
  uk = @{ url = 'https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/uk/uk_50k.txt'; alphabet = $ukAlphabet }
}

function Build-Model($name, $url, $alphabet) {
  $file = Join-Path $cache "$name.txt"
  if (-not (Test-Path $file)) {
    Write-Host "downloading $name ..."
    Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing -TimeoutSec 60
  }
  $allowed = @{}
  foreach ($ch in $alphabet.ToCharArray()) { $allowed[$ch] = $true }

  $counts = @{}          # bigram -> weighted count
  [double]$total = 0
  foreach ($line in [IO.File]::ReadAllLines($file, [Text.Encoding]::UTF8)) {
    $parts = $line.Split(' ')
    if ($parts.Count -lt 2) { continue }
    $word = $parts[0].ToLowerInvariant().Replace([char]0x0451, [char]0x0435) # ё -> е
    [double]$freq = 0
    if (-not [double]::TryParse($parts[1], [ref]$freq)) { continue }

    # keep only words made entirely of in-alphabet characters
    $ok = $true
    foreach ($ch in $word.ToCharArray()) { if (-not $allowed.ContainsKey($ch)) { $ok = $false; break } }
    if (-not $ok -or $word.Length -lt 2) { continue }

    for ($i = 0; $i -lt $word.Length - 1; $i++) {
      $bg = $word.Substring($i, 2)
      $counts[$bg] = ($counts[$bg]) + $freq
      $total += $freq
    }
  }

  # log10 joint probability; floor for unseen bigrams (add-1 over full pair space)
  $V = $alphabet.Length * $alphabet.Length
  $floor = [Math]::Log10(1.0 / ($total + $V))
  $entries = New-Object System.Collections.Generic.List[string]
  foreach ($bg in ($counts.Keys | Sort-Object)) {
    $lp = [Math]::Log10($counts[$bg] / $total)
    $entries.Add($bg + ':' + $lp.ToString('F3', [Globalization.CultureInfo]::InvariantCulture))
  }
  Write-Host "$name : $($entries.Count) bigrams, floor=$($floor.ToString('F3',[Globalization.CultureInfo]::InvariantCulture))"
  return [pscustomobject]@{ Packed = ($entries -join ' '); Floor = $floor }
}

$en = Build-Model 'en' $sources.en.url $sources.en.alphabet
$ru = Build-Model 'ru' $sources.ru.url $sources.ru.alphabet
$uk = Build-Model 'uk' $sources.uk.url $sources.uk.alphabet

$inv = [Globalization.CultureInfo]::InvariantCulture
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('using System.Globalization;')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('namespace OopsLayout.Core;')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('/// <summary>')
[void]$sb.AppendLine('/// Character-bigram language models for EN, RU and UK.')
[void]$sb.AppendLine('/// A word is scored under both models to decide whether it was typed in the')
[void]$sb.AppendLine('/// wrong keyboard layout. Generated from OpenSubtitles frequency lists by')
[void]$sb.AppendLine('/// tools/gen-bigrams.ps1 -- do not edit by hand.')
[void]$sb.AppendLine('/// </summary>')
[void]$sb.AppendLine('public static class Bigrams')
[void]$sb.AppendLine('{')
[void]$sb.AppendLine("    private const double EnFloor = $($en.Floor.ToString('F3', $inv));")
[void]$sb.AppendLine("    private const double RuFloor = $($ru.Floor.ToString('F3', $inv));")
[void]$sb.AppendLine("    private const double UkFloor = $($uk.Floor.ToString('F3', $inv));")
[void]$sb.AppendLine('')
[void]$sb.AppendLine("    private static readonly Dictionary<string, double> En = Parse(`"$($en.Packed)`");")
[void]$sb.AppendLine("    private static readonly Dictionary<string, double> Ru = Parse(`"$($ru.Packed)`");")
[void]$sb.AppendLine("    private static readonly Dictionary<string, double> Uk = Parse(`"$($uk.Packed)`");")
[void]$sb.AppendLine('')
[void]$sb.AppendLine('    /// <summary>Average log10 bigram probability of <paramref name="word"/> under the EN model.</summary>')
[void]$sb.AppendLine('    public static double ScoreEn(string word) => Score(word.ToLowerInvariant(), En, EnFloor);')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('    /// <summary>Average log10 bigram probability of <paramref name="word"/> under the RU model.</summary>')
[void]$sb.AppendLine('    public static double ScoreRu(string word) => Score(word.ToLowerInvariant(), Ru, RuFloor);')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('    /// <summary>Average log10 bigram probability of <paramref name="word"/> under the UK model.</summary>')
[void]$sb.AppendLine('    public static double ScoreUk(string word) => Score(word.ToLowerInvariant(), Uk, UkFloor);')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('    private static double Score(string w, Dictionary<string, double> model, double floor)')
[void]$sb.AppendLine('    {')
[void]$sb.AppendLine('        if (w.Length < 2) return floor;')
[void]$sb.AppendLine('        double sum = 0;')
[void]$sb.AppendLine('        for (int i = 0; i < w.Length - 1; i++)')
[void]$sb.AppendLine('        {')
[void]$sb.AppendLine('            var bg = w.Substring(i, 2);')
[void]$sb.AppendLine('            sum += model.TryGetValue(bg, out var lp) ? lp : floor;')
[void]$sb.AppendLine('        }')
[void]$sb.AppendLine('        return sum / (w.Length - 1);')
[void]$sb.AppendLine('    }')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('    private static Dictionary<string, double> Parse(string packed)')
[void]$sb.AppendLine('    {')
[void]$sb.AppendLine('        var d = new Dictionary<string, double>();')
[void]$sb.AppendLine('        foreach (var tok in packed.Split('' '', StringSplitOptions.RemoveEmptyEntries))')
[void]$sb.AppendLine('        {')
[void]$sb.AppendLine('            var c = tok.LastIndexOf('':'');')
[void]$sb.AppendLine('            d[tok[..c]] = double.Parse(tok[(c + 1)..], CultureInfo.InvariantCulture);')
[void]$sb.AppendLine('        }')
[void]$sb.AppendLine('        return d;')
[void]$sb.AppendLine('    }')
[void]$sb.AppendLine('}')

# UTF-8 *with* BOM so the C# compiler always reads the Cyrillic literals as UTF-8
$utf8Bom = New-Object System.Text.UTF8Encoding($true)
[IO.File]::WriteAllText($outCs, $sb.ToString(), $utf8Bom)
Write-Host "wrote $outCs"
