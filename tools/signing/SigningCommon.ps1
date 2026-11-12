# Fungsi bersama skrip signing (di-dot-source). Kompatibel Windows PowerShell 5.1.

function Get-RepoRoot {
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
}

# Private key tidak boleh berada di dalam repo (spec 9): tolak path di bawah root repo.
function Assert-OutsideRepo {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$What)
    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\') + '\'
    $repo = (Get-RepoRoot).TrimEnd('\') + '\'
    if ($full.StartsWith($repo, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$What must be outside the repository ($repo). Private keys must never be stored in the repo."
    }
}

function ConvertFrom-SecureStringPlain {
    param([Parameter(Mandatory = $true)][System.Security.SecureString]$Secure)
    $ptr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { return [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}
