<#
.SYNOPSIS
  Restaura um dump por cima da base de desenvolvimento. DESTRUTIVO.
.DESCRIPTION
  Faz DROP DATABASE / CREATE DATABASE e carrega o dump. Nao apaga o volume Docker.
  O ficheiro de dump e copiado para dentro do contentor com "docker compose cp"
  (stream tar, seguro para binarios) e o pg_restore corre la dentro - isto evita
  a corrupcao que ocorre ao encaminhar bytes binarios por um pipe do PowerShell 5.1.
.PARAMETER DumpFile
  Caminho local do ficheiro .dump a restaurar (produzido por backup.ps1).
.PARAMETER Force
  Confirmacao explicita - sem esta flag o script nao apaga nada.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DumpFile,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $DumpFile)) {
    throw "Ficheiro de dump nao encontrado: $DumpFile"
}

if (-not $Force) {
    Write-Host "Isto APAGA a base 'sfc_events' e restaura a partir de $DumpFile." -ForegroundColor Yellow
    Write-Host "Voltar a correr com -Force para confirmar." -ForegroundColor Yellow
    exit 1
}

$dumpItem = Get-Item $DumpFile
$containerPath = "/tmp/$($dumpItem.Name)"

Write-Host "A terminar ligacoes abertas a sfc_events ..."
$terminateSql = "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = 'sfc_events' AND pid <> pg_backend_pid();"
docker compose exec -T postgres psql -U sfc -d postgres -c $terminateSql | Out-Null

Write-Host "A recriar a base ..."
docker compose exec -T postgres psql -U sfc -d postgres -c "DROP DATABASE IF EXISTS sfc_events;"
if ($LASTEXITCODE -ne 0) { throw "DROP DATABASE falhou." }

docker compose exec -T postgres psql -U sfc -d postgres -c "CREATE DATABASE sfc_events OWNER sfc;"
if ($LASTEXITCODE -ne 0) { throw "CREATE DATABASE falhou." }

Write-Host "A copiar $DumpFile para o contentor ..."
docker compose cp $DumpFile "postgres:${containerPath}"
if ($LASTEXITCODE -ne 0) {
    throw "docker compose cp falhou com o codigo $LASTEXITCODE."
}

Write-Host "A restaurar $DumpFile ..."
docker compose exec -T postgres pg_restore -U sfc -d sfc_events --no-owner $containerPath
if ($LASTEXITCODE -ne 0) {
    throw "pg_restore falhou com o codigo $LASTEXITCODE."
}

# Limpar o ficheiro temporario dentro do contentor.
docker compose exec -T postgres rm -f $containerPath | Out-Null

Write-Host "Restauro concluido."
