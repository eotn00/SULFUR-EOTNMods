param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference = 'Stop'
$managed = Join-Path $GameRoot 'Sulfur_Data\Managed'
$core = Join-Path $GameRoot 'BepInEx\core'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$common = @('mscorlib','System','System.Core','netstandard','PerfectRandom.Sulfur.Core',
    'UnityEngine','UnityEngine.CoreModule','UnityEngine.UI','UnityEngine.UIModule') |
    ForEach-Object { '/reference:' + (Join-Path $managed ($_ + '.dll')) }
$common += @("/reference:$core\BepInEx.dll", "/reference:$core\0Harmony.dll")
$weapon = @('Progression.cs','WeaponState.cs','Migration.cs','RandomWeapons.cs','WeaponDescription.cs','WeaponText.cs','WeaponLanguage.cs') |
    ForEach-Object { Join-Path $PSScriptRoot "src\RandomWeaponProgression\$_" }
$weapon += Join-Path $PSScriptRoot 'src\OptionsUi.cs'
$weapon += @('QualityRecord.cs','EquipmentQualityTest.cs','EquipmentMigration.cs','Affixes.cs','QualitySettings.cs') |
    ForEach-Object { Join-Path $PSScriptRoot "src\EquipmentQualityTest\$_" }
$weaponRefs = @('Newtonsoft.Json','UnityEngine.JSONSerializeModule','Unity.TextMeshPro','I2AsmDef','UnityEngine.TextRenderingModule') |
    ForEach-Object { '/reference:' + (Join-Path $managed ($_ + '.dll')) }
& $compiler /nologo /noconfig /nostdlib+ /target:library /optimize+ "/out:$output\sulfur-RPG.dll" $common $weaponRefs $weapon
if($LASTEXITCODE -ne 0) { throw 'Weapon progression compilation failed' }
& $compiler /nologo /noconfig /nostdlib+ /target:library /optimize+ "/out:$output\SaveCompatibilityFix.dll" $common "$PSScriptRoot\src\SaveCompatibilityFix\SaveCompatibilityFix.cs"
if($LASTEXITCODE -ne 0) { throw 'Save compatibility compilation failed' }
& $compiler /nologo /noconfig /nostdlib+ /target:library /optimize+ "/out:$output\ChurchStashCrafting.dll" $common "/reference:$managed\PerfectRandom.Sulfur.Gameplay.dll" "$PSScriptRoot\src\ChurchCrafting\CraftPlan.cs" "$PSScriptRoot\src\ChurchCrafting\ChurchCrafting.cs"
if($LASTEXITCODE -ne 0) { throw 'Church crafting compilation failed' }
$inventoryRefs = @('Newtonsoft.Json','UnityEngine.JSONSerializeModule','UnityEngine.UIElementsModule','Unity.InputSystem','UnityEngine.IMGUIModule','UnityEngine.TextRenderingModule') | ForEach-Object { '/reference:'+(Join-Path $managed ($_+'.dll')) }
$inventorySources = Get-ChildItem -LiteralPath "$PSScriptRoot\src\InventoryRedux" -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compiler /nologo /noconfig /nostdlib+ /target:library /optimize+ "/out:$output\InventoryRedux.dll" $common $inventoryRefs $inventorySources
if($LASTEXITCODE -ne 0) { throw 'Inventory Redux compilation failed' }
$recipeRefs = @('UnityEngine.TextRenderingModule','UnityEngine.InputLegacyModule','UnityEngine.InputModule','UnityEngine.IMGUIModule','Unity.InputSystem','I2AsmDef','Unity.TextMeshPro') | ForEach-Object { '/reference:'+(Join-Path $managed ($_+'.dll')) }
$recipeSources = Get-ChildItem -LiteralPath "$PSScriptRoot\src\RecipeBrowser" -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compiler /nologo /noconfig /nostdlib+ /target:library /optimize+ "/out:$output\whats_this.dll" $common $recipeRefs $recipeSources
if($LASTEXITCODE -ne 0) { throw 'Recipe browser compilation failed' }
$traderSources=Get-ChildItem -LiteralPath "$PSScriptRoot\src\TraderFramework" -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compiler /nologo /noconfig /nostdlib+ /target:library /optimize+ "/out:$output\TraderFramework.dll" $common $inventoryRefs "/reference:$output\InventoryRedux.dll" $traderSources
if($LASTEXITCODE -ne 0) { throw 'Trader Framework compilation failed' }
$rummageSources=Get-ChildItem -LiteralPath "$PSScriptRoot\src\RummageInventoryReduxCompat" -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compiler /nologo /noconfig /nostdlib+ /target:library /optimize+ "/out:$output\InventoryRedux.RummageCompat.dll" $common $inventoryRefs $rummageSources
if($LASTEXITCODE -ne 0) { throw 'Rummage compatibility compilation failed' }
Write-Output "Built all seven plugins in $output"
