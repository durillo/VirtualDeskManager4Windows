<#
.SYNOPSIS
    Construye el instalador MSI de VirtualDeskManager, o con -Portable el ejecutable suelto.

.DESCRIPTION
    Publica la aplicación y empaqueta el resultado en un MSI que se instala para el usuario
    actual, sin pedir elevación, y que muestra la carpeta de destino durante la instalación.

    Todo lo que sale de aquí es autocontenido: lleva dentro el runtime de .NET y funciona en
    cualquier Windows 11 x64 sin instalar nada antes (pedido por el usuario el 2026-10-09; hasta
    la 0.25 había además un MSI y un .exe que pedían el .NET 10 Desktop Runtime). Con -Portable no
    hay MSI: sale un único .exe, con el runtime dentro, que se ejecuta y ya.

    Con -Firmar se firman los binarios propios y despues el MSI, en ese orden: firmar el
    contenido despues de empaquetarlo dejaria la firma del paquete sin cuadrar con lo que lleva
    dentro.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Portable
    .\build.ps1 -Firmar                            # busca un certificado de firma de codigo
    .\build.ps1 -Firmar -Huella A1B2C3...          # uno concreto
#>
[CmdletBinding()]
param(
    [switch] $Portable,
    [string] $Version,
    [string] $Configuration = 'Release',

    [switch] $Firmar,

    # Huella del certificado. Sin ella se coge el unico de firma de codigo que haya.
    [string] $Huella,

    [string] $SignTool,

    # Sin sello de tiempo la firma muere con el certificado, y Windows la trata como invalida
    # desde ese momento aunque el binario no haya cambiado.
    [string] $SelloDeTiempo = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'

$raiz = Split-Path -Parent $PSScriptRoot
$proyecto = Join-Path $raiz 'src\VirtualDeskManager.App\VirtualDeskManager.App.csproj'
$publicacion = Join-Path $raiz 'artifacts\installer-publish'
$salida = Join-Path $raiz 'dist'

# Herramientas de usuario: ni el SDK ni WiX necesitan estar instalados a nivel de máquina.
$env:PATH = "$env:USERPROFILE\.dotnet\tools;$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

if (-not $Version) {
    # La versión sale de donde ya vive la del producto, para que no se dupliquen.
    $props = Get-Content (Join-Path $raiz 'Directory.Build.props') -Raw
    if ($props -match '<Version>([^<]+)</Version>') { $Version = $Matches[1] }
    else { throw "No se pudo leer <Version> de Directory.Build.props" }

    # La etapa ("beta.1") va en el nombre de los ficheros, no en la version del MSI, que solo
    # admite numeros.
    if ($props -match '<Etapa>([^<]+)</Etapa>') { $Etapa = $Matches[1].Trim() }
}

$nombre = if ($Etapa) { "$Version-$Etapa" } else { $Version }

# ---------------------------------------------------------------------------- firma

# signtool y no Set-AuthenticodeSignature: el cmdlet firma sin sello de tiempo, y una firma sin
# sello caduca con el certificado. Medido el 2026-09-02 en un equipo con Smart App Control
# activo: firmado con el cmdlet la aplicación arrancaba y moría; con signtool, SHA-256 y sello,
# arranca y se mantiene.
function Buscar-SignTool {
    if ($SignTool) { return $SignTool }
    if ($env:SIGNTOOL) { return $env:SIGNTOOL }

    $enPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($enPath) { return $enPath.Source }

    # El SDK de Windows completo no hace falta: signtool viaja en un paquete NuGet.
    $candidatos = @(
        "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64\signtool.exe",
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe"
    )

    foreach ($patron in $candidatos) {
        $encontrado = Get-ChildItem $patron -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1

        if ($encontrado) { return $encontrado.FullName }
    }

    throw @"
no encuentro signtool.exe. Pasalo con -SignTool, ponlo en SIGNTOOL, o traelo sin instalar el SDK:
    dotnet new classlib -o tools\signtool
    dotnet add tools\signtool package Microsoft.Windows.SDK.BuildTools
"@
}

function Buscar-Certificado {
    $todos = @(Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert)

    if ($Huella) {
        $elegido = $todos | Where-Object { $_.Thumbprint -eq $Huella.Replace(' ', '') }
        if (-not $elegido) { throw "no hay ningun certificado de firma de codigo con la huella $Huella" }
        return $elegido
    }

    if ($todos.Count -eq 0) { throw "no hay ningun certificado de firma de codigo en Cert:\CurrentUser\My" }

    # Elegir por su cuenta entre varios seria firmar con uno que el usuario no espera.
    if ($todos.Count -gt 1) {
        throw "hay $($todos.Count) certificados de firma de codigo; di cual con -Huella:`n" +
              (($todos | ForEach-Object { "  $($_.Thumbprint)  $($_.Subject)" }) -join "`n")
    }

    return $todos[0]
}

function Firmar-Ficheros([string[]] $rutas) {
    if (-not $Firmar -or $rutas.Count -eq 0) { return }

    & $script:herramientaFirma sign `
        /sha1 $script:certificado.Thumbprint `
        /fd SHA256 `
        /tr $SelloDeTiempo `
        /td SHA256 `
        $rutas | Out-Null

    if ($LASTEXITCODE -ne 0) { throw "signtool fallo con codigo $LASTEXITCODE" }

    Write-Host ("firmados: {0} ficheros" -f $rutas.Count)
}

if ($Firmar) {
    $script:herramientaFirma = Buscar-SignTool
    $script:certificado = Buscar-Certificado
    Write-Host "firma: $($script:certificado.Subject)"
}

# ----------------------------------------------------------------------------

Write-Host "VirtualDeskManager $nombre"
Write-Host ("modo: {0}" -f $(if ($Portable) { 'ejecutable suelto, con el runtime dentro' } else { 'instalador, con el runtime dentro' }))

if (Test-Path $publicacion) { Remove-Item $publicacion -Recurse -Force }
New-Item -ItemType Directory -Path $publicacion -Force | Out-Null
New-Item -ItemType Directory -Path $salida -Force | Out-Null

$argumentos = @(
    'publish', $proyecto,
    '-c', $Configuration,
    '-r', 'win-x64',
    '-o', $publicacion,
    '-v', 'minimal'
)

if ($Portable) {
    # Un solo fichero con todo dentro. Las bibliotecas nativas de WPF (wpfgfx, PresentationNative…)
    # no se pueden cargar desde el paquete: con IncludeNativeLibrariesForSelfExtract se sacan a
    # una carpeta temporal al arrancar, y sin ello el .exe necesitaría esos .dll al lado.
    # Sin EnableCompressionInSingleFile a propósito. Medido el 2026-10-09: comprimido pesa 73 MB,
    # pero descomprime todo en memoria y gasta 277 MB en marcha; sin comprimir son 167 MB de
    # fichero y 129 MB en marcha, lo mismo que la versión instalada. Vdesk está encendido todo el
    # día, así que se paga la descarga una vez y no la memoria siempre.
    $argumentos += @('--self-contained', 'true', '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=embedded')
}
else {
    # Autocontenido y sin fichero único: dentro de un MSI no aporta nada empaquetar dos veces,
    # y así el desinstalador retira los ficheros uno a uno como es debido.
    $argumentos += @('--self-contained', 'true', '-p:DebugType=embedded')
}

Write-Host "`n--- publicando ---"
& dotnet @argumentos | Select-Object -Last 2
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fallo" }

$megas = [math]::Round((Get-ChildItem $publicacion -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
$ficheros = (Get-ChildItem $publicacion -Recurse -File).Count
Write-Host ("publicado: {0} ficheros, {1} MB" -f $ficheros, $megas)

if ($Firmar) {
    Write-Host "`n--- firmando los binarios ---"

    # Solo lo propio: lo que viene del runtime de .NET ya lo firma Microsoft, y volver a firmarlo
    # no anade nada.
    Firmar-Ficheros (Get-ChildItem $publicacion -Recurse -Include 'VirtualDeskManager*.dll', 'VirtualDeskManager*.exe' |
        ForEach-Object { $_.FullName })
}

if ($Portable) {
    # La version va en el nombre: un VirtualDeskManager.exe a secas corriendo desde dist\ queda
    # bloqueado, y la compilacion siguiente se llevaria el de la version anterior sin avisar.
    $exe = Join-Path $salida "VirtualDeskManager-$nombre.exe"
    Copy-Item (Join-Path $publicacion 'VirtualDeskManager.exe') $exe -Force

    $tamano = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "`nlisto: $exe  ($tamano MB)"
    return
}

$msi = Join-Path $salida "VirtualDeskManager-$nombre.msi"

Write-Host "`n--- empaquetando ---"
& wix build (Join-Path $PSScriptRoot 'Package.wxs') `
    -arch x64 `
    -d "Version=$Version" `
    -d "PublishDir=$publicacion" `
    -ext WixToolset.UI.wixext `
    -ext WixToolset.Util.wixext `
    -o $msi

if ($LASTEXITCODE -ne 0) { throw "wix build fallo" }

# Una acción de un botón que no existe no la detecta WiX: compila, y el botón no hace nada. Así
# salieron la 0.21.0 y la 0.22.0-beta.1, cuyo "Siguiente" de la página de opciones no avanzaba;
# las pruebas instalaban en silencio y no pasaban por los diálogos. Se mira en las tablas del MSI.
function Consultar-Msi([string] $ruta, [string] $sql) {
    $instalador = New-Object -ComObject WindowsInstaller.Installer
    $base = $instalador.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $instalador, @($ruta, 0))
    $vista = $base.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $base, @($sql))
    $vista.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $vista, $null) | Out-Null

    while ($fila = $vista.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $vista, $null)) {
        $fila.GetType().InvokeMember('StringData', 'GetProperty', $null, $fila, 1) + '/' +
        $fila.GetType().InvokeMember('StringData', 'GetProperty', $null, $fila, 2)
    }

    $vista.GetType().InvokeMember('Close', 'InvokeMethod', $null, $vista, $null) | Out-Null

    # Sin soltarlos, el MSI se queda abierto y signtool no puede firmarlo despues.
    foreach ($objeto in $fila, $vista, $base, $instalador) {
        if ($objeto) { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($objeto) }
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}

$controles = @(Consultar-Msi $msi 'SELECT Dialog_, Control FROM Control')
$huerfanas = @(Consultar-Msi $msi 'SELECT Dialog_, Control_ FROM ControlEvent' | Sort-Object -Unique |
    Where-Object { $controles -notcontains $_ })

if ($huerfanas.Count -gt 0) {
    throw "el MSI tiene acciones de botones que no existen (dialogo/control): $($huerfanas -join ', ')"
}

if ($Firmar) {
    Write-Host "`n--- firmando el MSI ---"
    Firmar-Ficheros @($msi)
}

$tamano = [math]::Round((Get-Item $msi).Length / 1MB, 1)
Write-Host "`nlisto: $msi  ($tamano MB)"
Write-Host "instalar en silencio:  msiexec /i `"$msi`" /qn"
Write-Host "desinstalar:           msiexec /x `"$msi`" /qn"
