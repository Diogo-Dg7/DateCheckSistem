<#
.SYNOPSIS
  Cria (ou recria) um banco de DEMONSTRAÇÃO do DateCheck com compromissos de exemplo, para as capturas do README.

.DESCRIPTION
  Aplica as migrations no banco informado e o popula com compromissos relativos à data/hora atuais
  (assim "Hoje" sempre tem um próximo, atrasados e concluídos). Nunca aponte para o banco real.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/capturas/criar-banco-demo.ps1
  # ...depois rode tools/capturas/capturar.ps1 (instruções no cabeçalho dele)

  # Para apagar o banco de demonstração:
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/capturas/criar-banco-demo.ps1 -Apagar

.NOTES
  Salve em UTF-8 com BOM (Windows PowerShell 5.1), por causa dos acentos.
  Rode a partir da raiz do repositório (usa `dotnet ef`, restaurado por `dotnet tool restore`).
#>
param(
  [string]$Banco = "DateCheckDb_Capturas",
  [string]$Servidor = ".\SQLEXPRESS",
  [switch]$Apagar
)

$ErrorActionPreference = "Stop"
if ($Banco -eq "DateCheckDb") { throw "Este script não deve tocar no banco real (DateCheckDb)." }

Add-Type -AssemblyName System.Data
$mestre = "Server=$Servidor;Database=master;Trusted_Connection=True;TrustServerCertificate=True;"
$demo = "Server=$Servidor;Database=$Banco;Trusted_Connection=True;TrustServerCertificate=True;"

function Executar($cs, $sql, $parametros = @{}) {
  $con = New-Object System.Data.SqlClient.SqlConnection $cs
  $con.Open()
  try {
    $cmd = $con.CreateCommand(); $cmd.CommandText = $sql
    foreach ($k in $parametros.Keys) { $cmd.Parameters.AddWithValue("@$k", $parametros[$k]) | Out-Null }
    $cmd.ExecuteNonQuery() | Out-Null
  } finally { $con.Dispose() }
}

$existe = "IF DB_ID(N'$Banco') IS NOT NULL BEGIN ALTER DATABASE [$Banco] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Banco]; END"
Executar $mestre $existe
if ($Apagar) { Write-Output "Banco '$Banco' apagado."; return }

dotnet ef database update --project src/DateCheck.Infrastructure --connection $demo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Falha ao aplicar as migrations." }

# Status: 0 = Pendente, 1 = Concluído, 2 = Cancelado
function Novo($titulo, $descricao, [datetime]$inicio, [int]$minutos, [int]$status, $serie = [DBNull]::Value) {
  $sql = "INSERT INTO Agendamentos (Titulo, Descricao, Inicio, Fim, Status, SerieId) VALUES (@t, @d, @i, @f, @s, @g)"
  $desc = if ($descricao) { $descricao } else { [DBNull]::Value }
  Executar $demo $sql @{ t = $titulo; d = $desc; i = $inicio; f = $inicio.AddMinutes($minutos); s = $status; g = $serie }
}

$agora = Get-Date
$hoje = $agora.Date
$h = { param($hora, $min = 0) $hoje.AddHours($hora).AddMinutes($min) }

# Hoje: concluídos, um atrasado, o próximo e os seguintes (relativos ao horário atual, arredondado a 5 min).
$base = $agora.AddMinutes(-($agora.Minute % 5)).AddSeconds(-$agora.Second)
Novo "Café com o time de produto" "Alinhar prioridades da semana" $base.AddHours(-5) 45 1
Novo "Revisar pull requests" $null $base.AddHours(-3.5) 60 1
Novo "Enviar relatório mensal" "Mandar para a Marina antes das 16h" $base.AddMinutes(-90) 30 0
Novo "Reunião de planejamento" "Sala 3 - levar o roteiro impresso" $base.AddMinutes(45) 60 0
Novo "Consulta no dentista" "Rua das Acácias, 120" $base.AddHours(3) 45 0
Novo "Academia" $null $base.AddHours(5) 60 0

# Restante do mês: variedade de status e uma série semanal.
$serie = [guid]::NewGuid()
$primeiroDia = Get-Date -Year $hoje.Year -Month $hoje.Month -Day 1 -Hour 0 -Minute 0 -Second 0
$dias = [DateTime]::DaysInMonth($hoje.Year, $hoje.Month)
for ($d = 1; $d -le $dias; $d++) {
  $dia = $primeiroDia.AddDays($d - 1)
  if ($dia -lt $hoje) { continue }
  if ($dia -eq $hoje) { continue }
  $st = 0
  if ($dia.DayOfWeek -in 'Monday', 'Wednesday') { Novo "Academia" $null $dia.AddHours(18) 60 0 $serie }
  switch ($d % 7) {
    1 { Novo "Reunião com cliente" "Apresentar a proposta revisada" $dia.AddHours(10) 60 0 }
    3 { Novo "Almoço de aniversário" "Restaurante Sabor da Terra" $dia.AddHours(12).AddMinutes(30) 90 0 }
    5 { Novo "Entrega do projeto" $null $dia.AddHours(15) 30 0 }
    6 { Novo "Dentista (remarcado)" $null $dia.AddHours(9) 45 2 }
  }
}
# Alguns dias passados do mês (se houver) já resolvidos.
for ($d = 1; $d -lt $hoje.Day; $d++) {
  $dia = $primeiroDia.AddDays($d - 1)
  Novo "Planejamento semanal" $null $dia.AddHours(9) 45 1
  if ($d % 2 -eq 0) { Novo "Mercado" $null $dia.AddHours(18) 40 1 }
}
Write-Output "Banco '$Banco' criado com os dados de demonstração."
