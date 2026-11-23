$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'copy-package.ps1')

function Assert-PackageFiles([string]$TaskSource, [string]$TaskDestination, [string[]]$TaskNames, [string[]]$TaskPreserved = @()) {
    $taskActual = @(Get-ChildItem -LiteralPath $TaskDestination -File -Recurse | ForEach-Object {
        $_.FullName.Substring($TaskDestination.Length + 1)
    })
    if (@(Compare-Object @($TaskNames + $TaskPreserved) $taskActual).Count -ne 0) {
        throw "Repeated copy changed the package layout: $TaskDestination"
    }
    foreach ($taskName in $TaskNames) {
        if ((Get-FileHash -LiteralPath (Join-Path $TaskSource $taskName)).Hash -cne
            (Get-FileHash -LiteralPath (Join-Path $TaskDestination $taskName)).Hash) {
            throw "Copied file is stale or corrupt: $taskName"
        }
    }
}

$taskRoot = Join-Path ([IO.Path]::GetTempPath()) ('LogAlerts-package-tests-' + [Guid]::NewGuid().ToString('N'))
$taskSource = Join-Path $taskRoot 'source [mod]'
$taskPackage = Join-Path $taskRoot 'package [mod]'
$taskInstall = Join-Path $taskRoot 'installed [mod]'
$taskNames = @('BepInExLogAlerts.dll', '_info.ini', 'README.md',
    'language_en\bepinexlogalerts.json', 'language_ja\bepinexlogalerts.json')
try {
    foreach ($taskDirectory in @($taskSource, $taskPackage, $taskInstall)) {
        [IO.Directory]::CreateDirectory($taskDirectory) | Out-Null
    }
    foreach ($taskName in $taskNames) {
        $taskPath = Join-Path $taskSource $taskName
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($taskPath)) | Out-Null
        [IO.File]::WriteAllText($taskPath, ('first: ' + $taskName))
    }
    Copy-PackageFiles -SourceDirectory $taskSource -DestinationDirectory $taskPackage
    Copy-PackageFiles -SourceDirectory $taskPackage -DestinationDirectory $taskInstall
    Assert-PackageFiles $taskSource $taskPackage $taskNames
    Assert-PackageFiles $taskSource $taskInstall $taskNames
    Write-Output 'PASS First packaging and install preserve relative paths, including literal brackets and language folders'

    foreach ($taskName in $taskNames) {
        [IO.File]::WriteAllText((Join-Path $taskSource $taskName), ('updated: ' + $taskName))
    }
    [IO.File]::WriteAllBytes((Join-Path $taskSource 'BepInExLogAlerts.dll'), [byte[]]@(0, 255, 1, 128, 42))
    Copy-PackageFiles -SourceDirectory $taskSource -DestinationDirectory $taskPackage
    Copy-PackageFiles -SourceDirectory $taskPackage -DestinationDirectory $taskInstall
    Assert-PackageFiles $taskSource $taskPackage $taskNames
    Assert-PackageFiles $taskSource $taskInstall $taskNames
    Write-Output 'PASS Repeated packaging and install update the root translations and DLL without nesting or extra files'

    $taskLocal = Join-Path $taskInstall 'local-settings.cfg'
    [IO.File]::WriteAllText($taskLocal, 'preserve me')
    Copy-PackageFiles -SourceDirectory $taskPackage -DestinationDirectory $taskInstall
    Assert-PackageFiles $taskSource $taskPackage $taskNames
    Assert-PackageFiles $taskSource $taskInstall $taskNames @('local-settings.cfg')
    if ([IO.File]::ReadAllText($taskLocal) -cne 'preserve me') { throw 'Copy overwrote an unrelated local file' }
    Write-Output 'PASS Further updates preserve unrelated local files'
} finally {
    if (Test-Path -LiteralPath $taskRoot) {
        $taskResolved = (Resolve-Path -LiteralPath $taskRoot).ProviderPath
        $taskTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if (-not $taskResolved.StartsWith($taskTemp, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($taskResolved) -notlike 'LogAlerts-package-tests-*') {
            throw 'Unexpected temporary test cleanup path'
        }
        Remove-Item -LiteralPath $taskResolved -Recurse -Force
    }
}
