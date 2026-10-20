param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\build.ps1" -GameRoot $GameRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo "/out:$PSScriptRoot\build\CraftingTests.exe" "$PSScriptRoot\src\ChurchCrafting\CraftPlan.cs" "$PSScriptRoot\src\ChurchCrafting\Tests.cs"
if($LASTEXITCODE -ne 0) { throw 'Crafting test compilation failed' }
& "$PSScriptRoot\build\CraftingTests.exe" $GameRoot
if($LASTEXITCODE -ne 0) { throw 'Crafting tests failed' }
& $compiler /nologo "/out:$PSScriptRoot\build\CompatibilityTests.exe" "$PSScriptRoot\src\SaveCompatibilityFix\Tests.cs"
if($LASTEXITCODE -ne 0) { throw 'Compatibility test compilation failed' }
& "$PSScriptRoot\build\CompatibilityTests.exe" $GameRoot "$PSScriptRoot\build\SaveCompatibilityFix.dll"
if($LASTEXITCODE -ne 0) { throw 'Compatibility tests failed' }
$project = "$PSScriptRoot\src\RandomWeaponProgression\tests\Validation.csproj"
dotnet run --project $project "-p:GameRoot=$GameRoot" -- "$PSScriptRoot\build\更rpg的火湖.dll" $GameRoot
if($LASTEXITCODE -ne 0) { throw 'Weapon progression tests failed' }
