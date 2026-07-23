<#
.SYNOPSIS
  Dump da base de dados de desenvolvimento (formato custom, restauravel com pg_restore).
.DESCRIPTION
  Corre pg_dump dentro do contentor postgres (docker compose) e copia o ficheiro
  resultante para o anfitriao com "docker compose cp", que transfere os bytes via
  stream tar e nao passa pela pipeline de texto do PowerShell. Isto evita a
  corrupcao que ocorre ao encaminhar a saida binaria de pg_dump por um pipe do
  PowerShell 5.1 (que reencoda a saida de comandos nativos como texto).
.PARAMETER OutputDirectory
  Pasta local onde o dump fica gravado. Por omissao "backups" (ignorada pelo git).
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = "backups"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$dumpFileName = "sfc_events_$timestamp.dump"
$dumpPath = Join-Path $OutputDirectory $dumpFileName
$containerPath = "/tmp/$dumpFileName"

Write-Host "A criar dump em $containerPath (dentro do contentor) ..."

docker compose exec -T postgres pg_dump -U sfc -d sfc_events -Fc -f $containerPath
if ($LASTEXITCODE -ne 0) {
    throw "pg_dump falhou com o codigo $LASTEXITCODE."
}

Write-Host "A copiar dump para $dumpPath ..."
docker compose cp "postgres:${containerPath}" $dumpPath
if ($LASTEXITCODE -ne 0) {
    throw "docker compose cp falhou com o codigo $LASTEXITCODE."
}

# Limpar o ficheiro temporario dentro do contentor.
docker compose exec -T postgres rm -f $containerPath | Out-Null

if (-not (Test-Path $dumpPath)) {
    throw "Dump nao encontrado em $dumpPath apos a copia."
}

$sizeBytes = (Get-Item $dumpPath).Length
if ($sizeBytes -lt 1024) {
    throw "Dump em $dumpPath tem apenas $sizeBytes bytes - suspeito de estar corrompido ou vazio."
}

$sizeKb = [math]::Round($sizeBytes / 1KB, 1)
Write-Host "Dump concluido: $dumpPath ($sizeKb KB)"
$dumpPath
