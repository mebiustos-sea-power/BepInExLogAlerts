function Copy-PackageFiles {
    param(
        [Parameter(Mandatory=$true)][string]$SourceDirectory,
        [Parameter(Mandatory=$true)][string]$DestinationDirectory
    )
    $taskCopySource = (Resolve-Path -LiteralPath $SourceDirectory -ErrorAction Stop).ProviderPath.TrimEnd([IO.Path]::DirectorySeparatorChar)
    foreach ($taskCopyFile in (Get-ChildItem -LiteralPath $taskCopySource -File -Recurse -Force -ErrorAction Stop)) {
        $taskCopyRelative = $taskCopyFile.FullName.Substring($taskCopySource.Length + 1)
        $taskCopyTarget = Join-Path $DestinationDirectory $taskCopyRelative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($taskCopyTarget)) | Out-Null
        Copy-Item -LiteralPath $taskCopyFile.FullName -Destination $taskCopyTarget -Force -ErrorAction Stop
    }
}
