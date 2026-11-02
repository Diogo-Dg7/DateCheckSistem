<#
.SYNOPSIS
  Gera o ícone do aplicativo (calendário azul com marca de verificação) em vários tamanhos.

.DESCRIPTION
  Desenha o ícone com System.Drawing e grava um .ico com quadros PNG (16 a 256 px).
  O resultado é versionado em src/DateCheck.Desktop/Recursos/app.ico; rode este script só para refazê-lo.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/icone/gerar-icone.ps1 `
      -Saida src\DateCheck.Desktop\Recursos\app.ico
#>
param([string]$Saida = "src\DateCheck.Desktop\Recursos\app.ico")

Add-Type -AssemblyName System.Drawing

function Novo-CaminhoArredondado([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
  $d = $r * 2
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $p.AddArc($x, $y, $d, $d, 180, 90)
  $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
  $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $p.CloseFigure()
  return $p
}

function Desenhar([int]$t) {
  $bmp = New-Object System.Drawing.Bitmap $t, $t, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.Clear([System.Drawing.Color]::Transparent)

  $azul = [System.Drawing.ColorTranslator]::FromHtml("#2563EB")
  $branco = [System.Drawing.Color]::White

  # fundo azul arredondado (a "capa" do calendário)
  $fundo = Novo-CaminhoArredondado ($t * 0.04) ($t * 0.08) ($t * 0.92) ($t * 0.88) ($t * 0.18)
  $g.FillPath((New-Object System.Drawing.SolidBrush $azul), $fundo)

  # folha branca
  $folha = Novo-CaminhoArredondado ($t * 0.14) ($t * 0.30) ($t * 0.72) ($t * 0.58) ($t * 0.08)
  $g.FillPath((New-Object System.Drawing.SolidBrush $branco), $folha)

  # argolas
  $pincelArgola = New-Object System.Drawing.SolidBrush $branco
  foreach ($cx in @(0.30, 0.70)) {
    $g.FillEllipse($pincelArgola, ($t * ($cx - 0.055)), ($t * 0.03), ($t * 0.11), ($t * 0.2))
  }

  # marca de verificação
  $caneta = New-Object System.Drawing.Pen $azul, ([Math]::Max(1.5, $t * 0.09))
  $caneta.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
  $caneta.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
  $caneta.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
  $pontos = @(
    (New-Object System.Drawing.PointF ($t * 0.32), ($t * 0.60)),
    (New-Object System.Drawing.PointF ($t * 0.46), ($t * 0.74)),
    (New-Object System.Drawing.PointF ($t * 0.70), ($t * 0.48))
  )
  $g.DrawLines($caneta, $pontos)

  $g.Dispose()
  return $bmp
}

$tamanhos = 16, 24, 32, 48, 64, 128, 256
$quadros = foreach ($t in $tamanhos) {
  $bmp = Desenhar $t
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  [pscustomobject]@{ Tamanho = $t; Bytes = $ms.ToArray() }
}

# Formato ICO: cabeçalho, diretório de imagens e os dados (PNG dentro do ICO é aceito desde o Vista).
$saidaCompleta = [System.IO.Path]::GetFullPath($Saida)
New-Item -ItemType Directory -Force ([System.IO.Path]::GetDirectoryName($saidaCompleta)) | Out-Null
$fs = [System.IO.File]::Create($saidaCompleta)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$quadros.Count)
$deslocamento = 6 + 16 * $quadros.Count
foreach ($q in $quadros) {
  $dim = if ($q.Tamanho -ge 256) { 0 } else { $q.Tamanho }
  $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$q.Bytes.Length); $w.Write([uint32]$deslocamento)
  $deslocamento += $q.Bytes.Length
}
foreach ($q in $quadros) { $w.Write($q.Bytes) }
$w.Dispose(); $fs.Dispose()

"Ícone gravado em $saidaCompleta ($((Get-Item $saidaCompleta).Length) bytes, $($quadros.Count) tamanhos)"

