$ErrorActionPreference = 'Stop'

$packageName = 'inferstat'
$toolsDir    = Split-Path -Parent $MyInvocation.MyCommand.Definition

# inferstat ships as a single self-contained executable inside the
# inferstat-win-x64.zip release asset. Install-ChocolateyZipPackage extracts the
# zip into the package's tools directory; Chocolatey then auto-shims the
# inferstat.exe it finds there onto the PATH (no installer, no registry entries).
$packageArgs = @{
  packageName    = $packageName
  unzipLocation  = $toolsDir
  url64bit       = '__URL64__'
  checksum64     = '__CHECKSUM64__'
  checksumType64 = 'sha256'
}

Install-ChocolateyZipPackage @packageArgs
