<#
.SYNOPSIS
  Gera capturas de tela do DateCheck (Hoje, Calendário, formulário com aviso de conflito e Configurações) para o README.

.DESCRIPTION
  Abre o aplicativo, usa UI Automation para navegar entre as telas e salva PNGs na pasta de saída:
  01-hoje.png, 02-calendario.png, 03-formulario.png e 04-configuracoes.png.

  NÃO use o banco real: aponte o app para um banco de demonstração, descartável.

.EXAMPLE
  # 1) Compile o app
  dotnet build

  # 2) Crie o banco de demonstração (migrations + compromissos de exemplo relativos a agora)
  dotnet tool restore
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/capturas/criar-banco-demo.ps1

  # 3) Rode o script com a connection string sobrescrita por variável de ambiente
  $env:ConnectionStrings__DateCheckDb = 'Server=.\SQLEXPRESS;Database=DateCheckDb_Capturas;Trusted_Connection=True;TrustServerCertificate=True;'
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/capturas/capturar.ps1 `
      -Exe "src\DateCheck.Desktop\bin\Debug\net10.0-windows10.0.17763.0\DateCheck.Desktop.exe" `
      -Saida "docs\imagens"

  # 4) Apague o banco de demonstração
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/capturas/criar-banco-demo.ps1 -Apagar

.NOTES
  - A pasta de saída precisa existir.
  - A captura usa PrintWindow: só a janela do app é copiada, nunca a tela ao redor.
  - O script deve ser salvo em UTF-8 com BOM (Windows PowerShell 5.1), por causa dos acentos nos nomes dos botões.
  - Os nomes dos botões ("+ Novo compromisso", "Salvar", "Não", "Cancelar", "Calendário", "Configurações") são usados para localizar os controles.
#>
param([string]$Exe, [string]$Saida)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Nativo {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
[Nativo]::SetProcessDPIAware() | Out-Null

# PrintWindow (PW_RENDERFULLCONTENT) pede à própria janela que se desenhe: não copia a tela, então nada que
# esteja por cima ou ao redor (outros apps, toasts) entra na imagem.
function Capturar($janela, $arquivo) {
  $h = [IntPtr]$janela.Current.NativeWindowHandle
  Start-Sleep -Milliseconds 700
  $r = New-Object Nativo+RECT
  [Nativo]::GetWindowRect($h, [ref]$r) | Out-Null
  $w = $r.R - $r.L; $a = $r.B - $r.T
  $bmp = New-Object System.Drawing.Bitmap $w, $a
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  [Nativo]::PrintWindow($h, $hdc, 2) | Out-Null
  $g.ReleaseHdc($hdc)
  $bmp.Save($arquivo, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}

function Achar($raiz, $nome) {
  $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $nome
  $raiz.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Clicar($raiz, $nome) {
  # Prefere botões: o nome pode coincidir com um painel (ex.: caixa de mensagem do Windows).
  $AE = [System.Windows.Automation.AutomationElement]
  $botao = New-Object System.Windows.Automation.AndCondition `
    (New-Object System.Windows.Automation.PropertyCondition ($AE::NameProperty), $nome),
    (New-Object System.Windows.Automation.PropertyCondition ($AE::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Button))
  $el = $raiz.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $botao)
  if ($null -eq $el) { $el = Achar $raiz $nome }
  if ($null -eq $el) { throw "Controle '$nome' não encontrado" }
  try { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
  catch { throw "Controle '$nome' ($($el.Current.ControlType.ProgrammaticName)) não aceita clique: $($_.Exception.Message)" }
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

  $raizDesk = [System.Windows.Automation.AutomationElement]::RootElement
  function JanelaPorNome($nome) {
    $c1 = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $nome
    $c2 = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty), $p.Id
    $raizDesk.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.AndCondition $c1, $c2))
  }
  function Esperar($bloco, $segundos = 10) {
    $fim = (Get-Date).AddSeconds($segundos)
    while ((Get-Date) -lt $fim) { $r = & $bloco; if ($r) { return $r }; Start-Sleep -Milliseconds 300 }
    return $null
  }

  # Formulário com aviso de conflito: um compromisso que começa 15 min depois do "próximo" da demonstração
  # (criar-banco-demo.ps1 cria "Reunião de planejamento" 45 min à frente, em múltiplos de 5 min).
  Clicar $principal "+ Novo compromisso"
  $form = Esperar { JanelaPorNome "Novo compromisso" }
  if ($null -eq $form) { throw "Formulário não abriu" }
  Start-Sleep -Seconds 1
  $agora = Get-Date
  $base = $agora.AddMinutes(-($agora.Minute % 5)).AddSeconds(-$agora.Second)
  $inicio = $base.AddMinutes(45 + 15); $fim = $inicio.AddMinutes(45)
  $editores = @($form.FindAll([System.Windows.Automation.TreeScope]::Descendants,
      (New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Edit))) |
    Where-Object { $_.GetSupportedPatterns() -contains [System.Windows.Automation.ValuePattern]::Pattern })
  $campos = $editores | Where-Object { $_.Current.IsKeyboardFocusable }
  $campos[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("Retorno ao cliente")
  $campos[1].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("Ligar para confirmar a proposta")
  $horas = $editores | Where-Object { $_.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -match '^\d{1,2}:\d{2}$' }
  $horas[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($inicio.ToString("HH:mm"))
  $horas[1].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($fim.ToString("HH:mm"))
  Start-Sleep -Milliseconds 500

  Clicar $form "Salvar"
  $aviso = Esperar { JanelaPorNome "Conflito de horários" }
  if ($null -eq $aviso) { throw "Aviso de conflito não apareceu" }
  Start-Sleep -Milliseconds 800

  # Duas janelas sobrepostas: captura cada uma e compõe (sem copiar o resto da tela).
  $arqForm = Join-Path $Saida "_form.png"; $arqAviso = Join-Path $Saida "_aviso.png"
  Capturar $form $arqForm
  Capturar $aviso $arqAviso
  # A caixa de mensagem tem uma moldura invisível (sombra) que sai preta no PrintWindow: recorta pelos limites visíveis.
  $ra = New-Object Nativo+RECT; [Nativo]::GetWindowRect([IntPtr]$aviso.Current.NativeWindowHandle, [ref]$ra) | Out-Null
  $dv = New-Object Nativo+RECT; [Nativo]::DwmGetWindowAttribute([IntPtr]$aviso.Current.NativeWindowHandle, 9, [ref]$dv, 16) | Out-Null
  $imgF = [System.Drawing.Image]::FromFile($arqForm); $imgA = [System.Drawing.Bitmap]::FromFile($arqAviso)
  $recorte = New-Object System.Drawing.Rectangle ($dv.L - $ra.L), ($dv.T - $ra.T), ($dv.R - $dv.L), ($dv.B - $dv.T)
  $margem = 24; $desvioX = 90; $desvioY = 150
  $larg = [Math]::Max($imgF.Width, $desvioX + $recorte.Width) + 2 * $margem
  $alt = [Math]::Max($imgF.Height, $desvioY + $recorte.Height) + 2 * $margem
  $bmp = New-Object System.Drawing.Bitmap $larg, $alt
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear([System.Drawing.Color]::FromArgb(236, 238, 242))
  $g.DrawImage($imgF, $margem, $margem)
  $g.DrawImage($imgA, (New-Object System.Drawing.Rectangle ($margem + $desvioX), ($margem + $desvioY), $recorte.Width, $recorte.Height), $recorte, [System.Drawing.GraphicsUnit]::Pixel)
  $bmp.Save((Join-Path $Saida "03-formulario.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose(); $imgF.Dispose(); $imgA.Dispose()
  Remove-Item $arqForm, $arqAviso

  # Responde "Não" ao aviso e cancela o formulário, sem gravar nada.
  # Os botões da caixa de mensagem do Windows não expõem Invoke: usa a tecla de acesso (N = Não).
  $ha = [IntPtr]$aviso.Current.NativeWindowHandle
  [Nativo]::SetForegroundWindow($ha) | Out-Null
  Start-Sleep -Milliseconds 300
  # Só envia a tecla se a caixa de mensagem é mesmo a janela em primeiro plano (nunca para outro programa).
  if ([Nativo]::GetForegroundWindow() -ne $ha) { throw "A caixa de mensagem não está em primeiro plano; abortando sem enviar teclas." }
  [System.Windows.Forms.SendKeys]::SendWait("n")
  Start-Sleep -Milliseconds 800
  Clicar $form "Cancelar"
  Start-Sleep -Seconds 1

  Clicar $principal "Calendário"
  Start-Sleep -Seconds 3
  Capturar $principal (Join-Path $Saida "02-calendario.png")

  Clicar $principal "Configurações"
  $cfg = Esperar { JanelaPorNome "Configurações" }
  if ($null -eq $cfg) { throw "Configurações não abriu" }
  Start-Sleep -Seconds 1
  Capturar $cfg (Join-Path $Saida "04-configuracoes.png")
  Clicar $cfg "Cancelar"
}
finally {
  if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 1 }
  if (-not $p.HasExited) { $p.Kill() }
}



