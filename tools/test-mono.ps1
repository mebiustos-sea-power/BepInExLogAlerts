param([Parameter(Mandatory=$true)][string]$GameDirectory, [Parameter(Mandatory=$true)][string]$AnchorChainDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskManaged = Join-Path $GameDirectory 'Sea Power_Data\Managed'
$taskCore = Join-Path $GameDirectory 'BepInEx\core'
$taskRuntime = Join-Path $GameDirectory 'MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll'
$taskTests = Join-Path $taskRoot 'tests\MonoSmoke\bin\Release\netstandard2.1\BepInExLogAlerts.MonoSmoke.dll'
$taskPlugin = Join-Path $taskRoot 'src\bin\Release\netstandard2.1'
foreach ($taskPath in @($taskRuntime,$taskTests)) { if (-not (Test-Path $taskPath)) { throw "Missing Mono test dependency: $taskPath" } }
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$taskNativeSource = @"
using System;
using System.Runtime.InteropServices;
public static class BepInExLogAlertsNativeMono {
    [DllImport(@"$taskRuntime", CallingConvention=CallingConvention.Cdecl)]
    public static extern void mono_set_assemblies_path(string path);
    [DllImport(@"$taskRuntime", CallingConvention=CallingConvention.Cdecl)]
    public static extern void mono_set_dirs(string assemblyPath, string configPath);
    [DllImport(@"$taskRuntime", CallingConvention=CallingConvention.Cdecl)]
    public static extern int mono_main(int argc, IntPtr argv);
}
"@
Add-Type -TypeDefinition $taskNativeSource
$taskSearch = @($taskManaged,$taskCore,$taskPlugin,(Split-Path $taskTests -Parent),$AnchorChainDirectory) -join ';'
[Environment]::SetEnvironmentVariable('MONO_PATH',$taskSearch)
[BepInExLogAlertsNativeMono]::mono_set_dirs($taskManaged,(Join-Path $GameDirectory 'MonoBleedingEdge\etc'))
[BepInExLogAlertsNativeMono]::mono_set_assemblies_path($taskSearch)
$taskArguments = @('BepInExLogAlertsMono','--runtime=v4.0.30319',$taskTests)
$taskArguments += $GameDirectory
$taskStrings = @($taskArguments | ForEach-Object { [Runtime.InteropServices.Marshal]::StringToHGlobalAnsi($_) })
$taskVector = [Runtime.InteropServices.Marshal]::AllocHGlobal([IntPtr]::Size * ($taskStrings.Count + 1))
try {
    for ($taskIndex=0; $taskIndex -lt $taskStrings.Count; $taskIndex++) {
        [Runtime.InteropServices.Marshal]::WriteIntPtr($taskVector,[IntPtr]::Size*$taskIndex,$taskStrings[$taskIndex])
    }
    [Runtime.InteropServices.Marshal]::WriteIntPtr($taskVector,[IntPtr]::Size*$taskStrings.Count,[IntPtr]::Zero)
    Write-Output "Testing with Sea Power's own Mono runtime: $taskRuntime"
    $taskResult = [BepInExLogAlertsNativeMono]::mono_main($taskStrings.Count,$taskVector)
    if ($taskResult -ne 0) { throw "Mono hook tests failed with exit code $taskResult." }
} finally {
    foreach ($taskPointer in $taskStrings) { [Runtime.InteropServices.Marshal]::FreeHGlobal($taskPointer) }
    [Runtime.InteropServices.Marshal]::FreeHGlobal($taskVector)
}
