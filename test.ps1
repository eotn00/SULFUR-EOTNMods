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
dotnet run --project $project "-p:GameRoot=$GameRoot" -- "$PSScriptRoot\build\sulfur-RPG.dll" $GameRoot
if($LASTEXITCODE -ne 0) { throw 'Weapon progression tests failed' }
$env:SULFUR_TEST_GAME_ROOT = $GameRoot
dotnet run --project "$PSScriptRoot\src\EquipmentQualityTest\tests\Validation.csproj" "-p:GameRoot=$GameRoot" -- "$PSScriptRoot\build\sulfur-RPG.dll"
if($LASTEXITCODE -ne 0) { throw 'Equipment quality tests failed' }
dotnet run --project "$PSScriptRoot\src\RecipeBrowser\tests\Validation.csproj" "-p:GameRoot=$GameRoot" -- "$PSScriptRoot\build\whats_this.dll" "$GameRoot\Sulfur_Data\Managed\PerfectRandom.Sulfur.Core.dll"
if($LASTEXITCODE -ne 0) { throw 'Recipe browser tests failed' }
$env:SULFUR_TEST_GAME_ROOT=$GameRoot
dotnet run --project "$PSScriptRoot\src\InventoryRedux\tests\Validation.csproj" "-p:GameRoot=$GameRoot" -- "$PSScriptRoot\build\InventoryRedux.dll"
if($LASTEXITCODE -ne 0) { throw 'Inventory Redux tests failed' }
dotnet run --project "$PSScriptRoot\src\TraderFramework\tests\Validation.csproj" "-p:GameRoot=$GameRoot" -- "$PSScriptRoot\build" $GameRoot
if($LASTEXITCODE -ne 0) { throw 'Trader Framework tests failed' }
if(Test-Path -LiteralPath "$GameRoot\BepInEx\plugins\Rummage.dll") {
    dotnet run --project "$PSScriptRoot\src\RummageInventoryReduxCompat\tests\Validation.csproj" "-p:GameRoot=$GameRoot" -- "$PSScriptRoot\build\InventoryRedux.RummageCompat.dll"
    if($LASTEXITCODE -ne 0) { throw 'Rummage compatibility tests failed' }
}
