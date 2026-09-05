# Generate RSA key pair (run once by admin)
$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider 2048

$privXmlPath = Join-Path $toolsDir 'private_key.xml'
$pubXmlPath  = Join-Path $toolsDir 'public_key.xml'

[System.IO.File]::WriteAllText($privXmlPath, $rsa.ToXmlString($true))
[System.IO.File]::WriteAllText($pubXmlPath, $rsa.ToXmlString($false))

Write-Host "Done:"
Write-Host "  private_key.xml - admin only, do not distribute"
Write-Host "  public_key.xml  - embed in LicenseManager.cs"
