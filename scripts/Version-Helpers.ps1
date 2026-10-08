# Shared version validation for packaging and publication; no external commands.
function Get-PlayerVersionInfo {
    param([Parameter(Mandatory)][string]$Version)
    if ($Version -match '^(0\.2|1\.0)\.([0-9]+)-dev\.([0-9]+)$') {
        return [pscustomobject]@{ Version = $Version; FileVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3])"; ReleaseVersion = ''; IsRelease = $false }
    }
    if ($Version -match '^(1\.0\.0)\+build\.([0-9]+)\.([0-9]+)$') {
        return [pscustomobject]@{ Version = $Version; FileVersion = "1.0.$($Matches[2]).$($Matches[3])"; ReleaseVersion = $Matches[1]; IsRelease = $true }
    }
    throw 'Unsupported MPswift product version.'
}

function Get-ExpectedPlayerVersion {
    param([Parameter(Mandatory)][ValidatePattern('^[0-9]+$')][string]$Sequence,
        [Parameter(Mandatory)][ValidatePattern('^[0-9]+$')][string]$Attempt,
        [AllowEmptyString()][string]$ReleaseVersion = '')
    if ($ReleaseVersion -and $ReleaseVersion -ne '1.0.0') { throw 'Only the owner-authorized 1.0.0 release is supported.' }
    if ($ReleaseVersion) { return "$ReleaseVersion+build.$Sequence.$Attempt" }
    return "1.0.$Sequence-dev.$Attempt"
}
