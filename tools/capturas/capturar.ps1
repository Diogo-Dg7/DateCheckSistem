<#
.SYNOPSIS
  Gera capturas de tela do DateCheck (Hoje, Calendário e formulário) para o README.

.DESCRIPTION
  Abre o aplicativo, usa UI Automation para navegar entre as telas e salva PNGs na pasta de saída:
  01-hoje.png, 02-calendario.png e 03-formulario.png.

  NÃO use o banco real: aponte o app para um banco de demonstração, descartável.

.EXAMPLE
  # 1) Compile o app
  dotnet build

  # 2) Crie um banco de demonstração (as migrations criam as tabelas) e popule com compromissos de exemplo
  dotnet ef database update --project src/DateCheck.Infrastructure `
      --connection "Server=.\SQLEXPRESS;Database=DateCheckDb_Capturas;Trusted_Connection=True;TrustServerCertificate=True;"
  # (insira alguns registros em dbo.Agendamentos com sqlcmd -f 65001, para os acentos não quebrarem)

  # 3) Rode o script com a connection string sobrescrita por variável de ambiente
  $env:ConnectionStrings__DateCheckDb = 'Server=.\SQLEXPRESS;Database=DateCheckDb_Capturas;Trusted_Connection=True;TrustServerCertificate=True;'
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/capturas/capturar.ps1 `
      -Exe "src\DateCheck.Desktop\bin\Debug\net10.0-windows10.0.17763.0\DateCheck.Desktop.exe" `
      -Saida "docs\imagens"

  # 4) Apague o banco de demonstração
  sqlcmd -S ".\SQLEXPRESS" -E -C -Q "ALTER DATABASE DateCheckDb_Capturas SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE DateCheckDb_Capturas;"

.NOTES
  - A pasta de saída precisa existir.
  - O script deve ser salvo em UTF-8 com BOM (Windows PowerShell 5.1), por causa dos acentos nos nomes dos botões.
  - Os nomes dos botões ("+ Novo compromisso", "Calendário", "Cancelar") são usados para localizar os controles.
#>
param([string]$Exe, [string]$Saida)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Nativo {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
[Nativo]::SetProcessDPIAware() | Out-Null

function Capturar($janela, $arquivo) {
  $h = [IntPtr]$janela.Current.NativeWindowHandle
  [Nativo]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Milliseconds 700
  $r = New-Object Nativo+RECT
  [Nativo]::GetWindowRect($h, [ref]$r) | Out-Null
  $w = $r.R - $r.L; $a = $r.B - $r.T
  $bmp = New-Object System.Drawing.Bitmap $w, $a
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size $w, $a))
  $bmp.Save($arquivo, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}

function Achar($raiz, $nome) {
  $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $nome
  $raiz.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Clicar($raiz, $nome) {
  $el = Achar $raiz $nome
  if ($null -eq $el) { throw "Controle '$nome' não encontrado" }
  $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

$p = Start-Process -FilePath $Exe -PassThru
try {
  $principal = $null
  for ($i = 0; $i -lt 60 -and $null -eq $principal; $i++) {
    Start-Sleep -Milliseconds 500
    $p.Refresh()
    if ($p.MainWindowHandle -ne [IntPtr]::Zero) {
      $principal = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    }
  }
  if ($null -eq $principal) { throw "Janela principal não apareceu" }
  Start-Sleep -Seconds 3

  Capturar $principal (Join-Path $Saida "01-hoje.png")

  Clicar $principal "+ Novo compromisso"
  Start-Sleep -Seconds 2
  $raizDesk = [System.Windows.Automation.AutomationElement]::RootElement
  $c1 = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), "Novo compromisso"
  $c2 = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Window)
  $cond = New-Object System.Windows.Automation.AndCondition $c1, $c2
  $form = $raizDesk.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
  if ($null -ne $form) {
    Capturar $form (Join-Path $Saida "03-formulario.png")
    Clicar $form "Cancelar"
    Start-Sleep -Seconds 1
  }

  Clicar $principal "Calendário"
  Start-Sleep -Seconds 3
  Capturar $principal (Join-Path $Saida "02-calendario.png")
}
finally {
  if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 1 }
  if (-not $p.HasExited) { $p.Kill() }
}



