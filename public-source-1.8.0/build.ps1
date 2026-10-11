param([string]$GameManaged='C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed')
$ErrorActionPreference='Stop'
$version='1.8.0'
dotnet run --project "$PSScriptRoot\Checks\Checks.csproj" -c Release
if($LASTEXITCODE -ne 0){throw 'Checks failed'}
 dotnet build "$PSScriptRoot\RandomMapStudio.csproj" -c Release "-p:GameManaged=$GameManaged"
 if($LASTEXITCODE -ne 0){throw 'Build failed'}
 $package=Join-Path $PSScriptRoot "dist\unified-$version\RandomMapStudio"
 New-Item -ItemType Directory -Force $package | Out-Null
 Copy-Item -LiteralPath "$PSScriptRoot\bin\Release\RandomMapStudio.dll","$PSScriptRoot\Libs\UnifiedUILib.dll","$PSScriptRoot\README.ja.md","$PSScriptRoot\README.md","$PSScriptRoot\LICENSE.UnifiedUI.txt" -Destination $package
 Compress-Archive -Path $package -DestinationPath "$PSScriptRoot\dist\RandomMapStudio-$version.zip" -Force
$source=Join-Path $PSScriptRoot "dist\public-source-$version"
New-Item -ItemType Directory -Force "$source\Source","$source\Libs","$source\Checks","$source\Workshop" | Out-Null
Copy-Item -Path "$PSScriptRoot\Source\*.cs" -Destination "$source\Source"
Copy-Item -LiteralPath "$PSScriptRoot\Libs\UnifiedUILib.dll" -Destination "$source\Libs"
Copy-Item -LiteralPath "$PSScriptRoot\Checks\Program.cs","$PSScriptRoot\Checks\Checks.csproj" -Destination "$source\Checks"
Copy-Item -LiteralPath "$PSScriptRoot\RandomMapStudio.csproj","$PSScriptRoot\build.ps1","$PSScriptRoot\README.ja.md","$PSScriptRoot\README.md","$PSScriptRoot\LICENSE.UnifiedUI.txt","$PSScriptRoot\.gitignore","$PSScriptRoot\Localization-translations.json" -Destination $source
Copy-Item -Path "$PSScriptRoot\Workshop\*" -Destination "$source\Workshop"
Compress-Archive -Path $source -DestinationPath "$PSScriptRoot\dist\RandomMapStudio-$version-Source.zip" -Force
