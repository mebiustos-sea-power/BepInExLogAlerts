param(
    [string]$GameDirectory = '',
    [string]$AnchorChainDirectory = '',
    [switch]$Test,
    [switch]$InstallLocal
)
$ErrorActionPreference = 'Stop'
$taskDotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path $taskDotnet)) { throw 'A .NET SDK is required.' }
$taskProperties = @('-p:UseSharedCompilation=false')
if ($GameDirectory) { $taskProperties += "-p:SeaPowerDir=$GameDirectory" }
if ($AnchorChainDirectory) { $taskProperties += "-p:AnchorChainDir=$AnchorChainDirectory" }
$taskHome = Join-Path $PSScriptRoot 'artifacts\dotnet-home'
New-Item -ItemType Directory -Force $taskHome | Out-Null
function Invoke-TaskProcess([string]$TaskExecutable, [string[]]$TaskArguments) {
    $taskStartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $taskStartInfo.FileName = $TaskExecutable
    $taskStartInfo.Arguments = ($TaskArguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $taskStartInfo.UseShellExecute = $false
    $taskStartInfo.CreateNoWindow = $true
    $taskStartInfo.RedirectStandardOutput = $true
    $taskStartInfo.RedirectStandardError = $true
    $taskStartInfo.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $taskStartInfo.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)
    $taskStartInfo.EnvironmentVariables['DOTNET_CLI_HOME'] = $taskHome
    $taskStartInfo.EnvironmentVariables['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    $taskStartInfo.EnvironmentVariables['DOTNET_CLI_UI_LANGUAGE'] = 'en'
    $taskStartInfo.EnvironmentVariables['DOTNET_GENERATE_ASPNET_CERTIFICATE'] = 'false'
    $taskStartInfo.EnvironmentVariables['DOTNET_SKIP_FIRST_TIME_EXPERIENCE'] = '1'
    $taskStartInfo.EnvironmentVariables['DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE'] = 'true'
    $taskProcess = [System.Diagnostics.Process]::Start($taskStartInfo)
    $taskOutput = $taskProcess.StandardOutput.ReadToEndAsync()
    $taskErrors = $taskProcess.StandardError.ReadToEndAsync()
    $taskProcess.WaitForExit()
    if ($taskOutput.Result) { Write-Output $taskOutput.Result.TrimEnd() }
    if ($taskErrors.Result) { Write-Output $taskErrors.Result.TrimEnd() }
    if ($taskProcess.ExitCode -ne 0) { throw "$TaskExecutable exited with $($taskProcess.ExitCode)." }
}
function Invoke-TaskDotnet([string[]]$TaskArguments) { Invoke-TaskProcess $taskDotnet $TaskArguments }
Invoke-TaskDotnet (@('build', (Join-Path $PSScriptRoot 'src\BepInExLogAlerts.csproj'), '-c', 'Release', '--nologo', '--verbosity', 'minimal') + $taskProperties)
if ($Test) {
    Invoke-TaskDotnet (@('build', (Join-Path $PSScriptRoot 'tests\BepInExLogAlerts.Tests.csproj'), '-c', 'Release', '--nologo', '--verbosity', 'minimal') + $taskProperties)
    Invoke-TaskDotnet @((Join-Path $PSScriptRoot 'tests\bin\Release\net10.0\BepInExLogAlerts.Tests.dll'))
    Invoke-TaskDotnet (@('build', (Join-Path $PSScriptRoot 'tests\MonoSmoke\BepInExLogAlerts.MonoSmoke.csproj'), '-c', 'Release', '--nologo', '--verbosity', 'minimal') + $taskProperties)
}
if (-not $GameDirectory -or -not $AnchorChainDirectory) {
    $taskLocalPath = Join-Path $PSScriptRoot 'Directory.Build.local.props'
    if (-not (Test-Path $taskLocalPath)) { throw 'Set local game paths or pass both directory arguments.' }
    [xml]$taskLocal = Get-Content $taskLocalPath
    if (-not $GameDirectory) { $GameDirectory = [string]$taskLocal.Project.PropertyGroup.SeaPowerDir }
    if (-not $AnchorChainDirectory) { $AnchorChainDirectory = [string]$taskLocal.Project.PropertyGroup.AnchorChainDir }
}
if ($Test) {
    $taskMonoOptions = @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'tools\test-mono.ps1'),'-GameDirectory',$GameDirectory,'-AnchorChainDirectory',$AnchorChainDirectory)
    Invoke-TaskProcess (Join-Path $PSHOME 'powershell.exe') $taskMonoOptions
}
$taskPackage = Join-Path $PSScriptRoot 'dist\BepInExLogAlerts'
New-Item -ItemType Directory -Force $taskPackage | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'src\bin\Release\netstandard2.1\BepInExLogAlerts.dll') $taskPackage -Force
Copy-Item (Join-Path $PSScriptRoot 'package\_info.ini') $taskPackage -Force
Copy-Item (Join-Path $PSScriptRoot 'README.md') $taskPackage -Force
Write-Output "Package: $taskPackage"
[xml]$taskProject = Get-Content (Join-Path $PSScriptRoot 'src\BepInExLogAlerts.csproj')
$taskVersion = [string]$taskProject.Project.PropertyGroup.Version
$taskZip = Join-Path $PSScriptRoot ("dist\BepInExLogAlerts-v" + $taskVersion + '.zip')
Add-Type -AssemblyName System.IO.Compression
$taskZipStream = [IO.File]::Open($taskZip, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite)
$taskArchive = [IO.Compression.ZipArchive]::new($taskZipStream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskName in @('BepInExLogAlerts.dll', '_info.ini', 'README.md')) {
        $taskEntry = $taskArchive.CreateEntry(('BepInExLogAlerts/' + $taskName), [IO.Compression.CompressionLevel]::Optimal)
        $taskInput = [IO.File]::OpenRead((Join-Path $taskPackage $taskName))
        $taskOutput = $taskEntry.Open()
        try { $taskInput.CopyTo($taskOutput) }
        finally { $taskOutput.Dispose(); $taskInput.Dispose() }
    }
} finally { $taskArchive.Dispose(); $taskZipStream.Dispose() }
$taskChecksum = Join-Path $PSScriptRoot 'dist\SHA256SUMS.txt'
$taskHash = (Get-FileHash -Algorithm SHA256 $taskZip).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($taskChecksum, ($taskHash + '  ' + [IO.Path]::GetFileName($taskZip) + "`n"), [Text.UTF8Encoding]::new($false))
Write-Output "Archive: $taskZip"
Write-Output "Checksums: $taskChecksum"
if ($InstallLocal) {
    if (-not (Test-Path (Join-Path $GameDirectory 'Sea Power.exe'))) { throw 'Sea Power.exe was not found.' }
    $taskGameProcesses = Get-Process -Name 'Sea Power' -ErrorAction SilentlyContinue
    if ($taskGameProcesses) { throw 'Close Sea Power before installing the mod.' }
    $taskDestination = Join-Path $GameDirectory 'Sea Power_Data\StreamingAssets\BepInExLogAlerts'
    New-Item -ItemType Directory -Force $taskDestination | Out-Null
    Get-ChildItem $taskPackage -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $taskDestination $_.Name) -Force }
    Write-Output "Installed: $taskDestination"
    Write-Output 'Enable BepInEx Log Alerts in the mod manager, then restart Sea Power.'
}
