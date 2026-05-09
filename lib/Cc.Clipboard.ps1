# CC-DESC: Shared cross-platform clipboard helpers for Context Control scripts.

function Invoke-CcClipboardProgram {
    param(
        [string]$Program,
        [string[]]$Arguments,
        [AllowNull()][string]$Text
    )

    if ([string]::IsNullOrWhiteSpace($Program)) {
        throw "Clipboard backend program was empty."
    }

    if ($null -eq $Arguments) {
        $Arguments = @()
    }

    if ($null -eq $Text) {
        $Text = ""
    }

    $cmd = Get-Command $Program -ErrorAction SilentlyContinue
    if ($null -eq $cmd) {
        throw "$Program was not found."
    }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $cmd.Source
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    foreach ($arg in $Arguments) {
        [void]$psi.ArgumentList.Add($arg)
    }

    try {
        $psi.StandardInputEncoding = New-Object System.Text.UTF8Encoding($false)
    }
    catch {
        # Older runtimes may not expose StandardInputEncoding. The default still
        # works for normal ASCII/UTF-8 project paths; this is only best-effort.
    }

    $process = [System.Diagnostics.Process]::Start($psi)
    $process.StandardInput.Write($Text)
    $process.StandardInput.Close()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    if ($process.ExitCode -ne 0) {
        if ([string]::IsNullOrWhiteSpace($stderr)) {
            $stderr = "exit code $($process.ExitCode)"
        }
        throw "$Program failed: $stderr"
    }
}

function Copy-CcTextToClipboard {
    [CmdletBinding()]
    param([AllowNull()][string]$Text)

    if ($null -eq $Text) {
        $Text = ""
    }

    $errors = New-Object System.Collections.Generic.List[string]

    try {
        Microsoft.PowerShell.Management\Set-Clipboard -Value $Text -ErrorAction Stop
        return "Set-Clipboard"
    }
    catch {
        [void]$errors.Add("Set-Clipboard: $($_.Exception.Message)")
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
        try {
            Invoke-CcClipboardProgram "pbcopy" @() $Text
            return "pbcopy"
        }
        catch {
            [void]$errors.Add("pbcopy: $($_.Exception.Message)")
        }
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        try {
            $clipExe = Join-Path $env:SystemRoot "System32\clip.exe"
            Invoke-CcClipboardProgram $clipExe @() $Text
            return "clip.exe"
        }
        catch {
            [void]$errors.Add("clip.exe: $($_.Exception.Message)")
        }
    }

    foreach ($candidate in @("wl-copy", "xclip", "xsel")) {
        try {
            if ($candidate -eq "xclip") {
                Invoke-CcClipboardProgram $candidate @("-selection", "clipboard") $Text
            }
            elseif ($candidate -eq "xsel") {
                Invoke-CcClipboardProgram $candidate @("--clipboard", "--input") $Text
            }
            else {
                Invoke-CcClipboardProgram $candidate @() $Text
            }
            return $candidate
        }
        catch {
            [void]$errors.Add("${candidate}: $($_.Exception.Message)")
        }
    }

    throw "No clipboard backend worked. Tried: $([string]::Join('; ', [string[]]$errors.ToArray()))"
}

function Set-Clipboard {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        [AllowNull()]
        [string]$Value
    )

    begin {
        $parts = New-Object System.Collections.Generic.List[string]
    }

    process {
        if ($null -ne $Value) {
            [void]$parts.Add([string]$Value)
        }
    }

    end {
        $text = [string]::Join([Environment]::NewLine, [string[]]$parts.ToArray())
        [void](Copy-CcTextToClipboard $text)
    }
}
