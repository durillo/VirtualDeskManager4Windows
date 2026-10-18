<#
.SYNOPSIS
    Sube la version de la aplicacion en Directory.Build.props.

.DESCRIPTION
    Directory.Build.props es la unica fuente de la version: AssemblyVersion y FileVersion salen
    de ella, y build.ps1 la lee para nombrar el MSI y para pasarsela a WiX. Cambiarla aqui la
    cambia en todas partes.

    La version solo puede subir. Windows Installer decide si un paquete actualiza al anterior
    comparando los tres primeros numeros, asi que publicar una version igual o menor deja al
    usuario sin poder actualizar: le toca desinstalar a mano.

.EXAMPLE
    installer\bump-version.ps1 -Parte Patch     # 0.2.0 -> 0.2.1
    installer\bump-version.ps1 -Parte Minor     # 0.2.1 -> 0.3.0
    installer\bump-version.ps1 -A 1.0.0
    installer\bump-version.ps1 -Etapa beta.2  # la misma version, otra beta
    installer\bump-version.ps1 -Final         # quita la etapa: lista para publicar
#>
[CmdletBinding(DefaultParameterSetName = 'Parte')]
param(
    [Parameter(ParameterSetName = 'Parte')]
    [ValidateSet('Major', 'Minor', 'Patch')]
    [string] $Parte = 'Patch',

    [Parameter(ParameterSetName = 'Exacta', Mandatory = $true)]
    [string] $A,

    # La etapa no cambia los numeros: es la misma version, en beta o ya definitiva.
    [Parameter(ParameterSetName = 'Etapa', Mandatory = $true)]
    [ValidatePattern('^[a-z]+\.\d+$')]
    [string] $Etapa,

    [Parameter(ParameterSetName = 'Final', Mandatory = $true)]
    [switch] $Final
)

$ErrorActionPreference = 'Stop'

$props = Join-Path (Split-Path -Parent $PSScriptRoot) 'Directory.Build.props'

# Con .NET y no con Get-Content: PowerShell 5.1 lee en ANSI y destrozaria cualquier acento.
$texto = [System.IO.File]::ReadAllText($props)

if ($texto -notmatch '<Version>([^<]+)</Version>') { throw "no encuentro <Version> en $props" }

$actual = [version] $Matches[1]

if ($PSCmdlet.ParameterSetName -in 'Etapa', 'Final') {
    if ($texto -notmatch '<Etapa>[^<]*</Etapa>') { throw "no encuentro <Etapa> en $props" }

    $nuevaEtapa = if ($Final) { '' } else { $Etapa }
    $texto = $texto -replace '<Etapa>[^<]*</Etapa>', "<Etapa>$nuevaEtapa</Etapa>"
    [System.IO.File]::WriteAllText($props, $texto, (New-Object System.Text.UTF8Encoding $false))

    Write-Host ("version: {0}" -f $(if ($nuevaEtapa) { "$actual-$nuevaEtapa" } else { "$actual (definitiva)" }))
    return
}

if ($PSCmdlet.ParameterSetName -eq 'Exacta') {
    $nueva = [version] $A
}
else {
    $nueva = switch ($Parte) {
        'Major' { [version]::new($actual.Major + 1, 0, 0) }
        'Minor' { [version]::new($actual.Major, $actual.Minor + 1, 0) }
        'Patch' { [version]::new($actual.Major, $actual.Minor, $actual.Build + 1) }
    }
}

if ($nueva -le $actual) {
    throw "la version solo puede subir: $actual -> $nueva. Un MSI con version igual o menor no actualiza al instalado."
}

$texto = $texto -replace '<Version>[^<]+</Version>', "<Version>$nueva</Version>"
[System.IO.File]::WriteAllText($props, $texto, (New-Object System.Text.UTF8Encoding $false))

Write-Host "version: $actual -> $nueva"
Write-Host "recuerda las notas: docs/releases/v$nueva.md"
