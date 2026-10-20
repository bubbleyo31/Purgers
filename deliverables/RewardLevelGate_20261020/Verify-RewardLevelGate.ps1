param(
    [string]$ProjectRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)
)
$ErrorActionPreference = 'Stop'
# Only read production sources/assets; keep output outside Assets and do not call Unity.
$rulesPath = Join-Path $ProjectRoot 'Assets/Scripts/Progression/RewardDraftRules.cs'
$catalogPath = Join-Path $ProjectRoot 'Assets/Resources/Progression/PlayerRewardCatalog.asset'
$gameLogicPath = Join-Path $ProjectRoot 'Assets/Scripts/GameLogic.cs'
Add-Type -TypeDefinition (Get-Content -LiteralPath $rulesPath -Raw)
$repeat = [Purgers.Progression.RewardRepeatPolicy]::Repeatable
$once = [Purgers.Progression.RewardRepeatPolicy]::OncePerRun
$checks = 0
foreach ($category in 1,2,3,5) {
    foreach ($minimum in 1,5) {
        foreach ($level in ($minimum - 1),$minimum,($minimum + 1)) {
            foreach ($chance in 0.0,1.0) {
                foreach ($seed in 1..30) {
                    $pool = [Purgers.Progression.RewardDraftEntry[]]@(
                        [Purgers.Progression.RewardDraftEntry]::new('target', $category, $minimum, 1, $repeat)
                    )
                    $result = [Purgers.Progression.RewardDraftRules]::Draw($pool, $level, $seed, $null, $null, $chance)
                    $expectedCount = if ($level -ge $minimum) { 1 } else { 0 }
                    if ($result.Length -ne $expectedCount -or ($expectedCount -eq 1 -and $result[0] -ne 'target')) {
                        throw "Boundary failed: category=$category minimum=$minimum level=$level"
                    }
                    $checks++
                }
            }
        }
    }
    foreach ($seed in 1..30) {
        # Fallback must not bypass the level gate, weight, exclusions or once-per-run policy.
        $pool = [Purgers.Progression.RewardDraftEntry[]]@(
            [Purgers.Progression.RewardDraftEntry]::new('locked', $category, 5, 1000, $repeat),
            [Purgers.Progression.RewardDraftEntry]::new('available', 4, 1, 1, $repeat),
            [Purgers.Progression.RewardDraftEntry]::new('disabled', $category, 1, 0, $repeat),
            [Purgers.Progression.RewardDraftEntry]::new('excluded', $category, 1, 1, $repeat),
            [Purgers.Progression.RewardDraftEntry]::new('owned', $category, 1, 1, $once)
        )
        $result = [Purgers.Progression.RewardDraftRules]::Draw(
            $pool, 4, $seed, [string[]]@('owned'), [string[]]@('excluded'), 1.0)
        if ($result.Length -ne 1 -or $result[0] -ne 'available') { throw "Fallback failed: $category" }
        $checks++
    }
}
$catalogText = Get-Content -LiteralPath $catalogPath -Raw
$rewardGuids = @([regex]::Matches($catalogText, 'fileID: 11400000, guid: ([0-9a-f]{32})') |
    ForEach-Object { $_.Groups[1].Value })
$guidPaths = @{}
foreach ($metaPath in (rg --files (Join-Path $ProjectRoot 'Assets') -g '*.asset.meta')) {
    $metaText = Get-Content -LiteralPath $metaPath -Raw
    if ($metaText -match '(?m)^guid: ([0-9a-f]{32})' -and $rewardGuids -contains $Matches[1]) {
        $guidPaths[$Matches[1]] = $metaPath.Substring(0, $metaPath.Length - 5)
    }
}
$assets = @(foreach ($rewardGuid in $rewardGuids) {
    if (-not $guidPaths.ContainsKey($rewardGuid)) { throw "Missing asset: $rewardGuid" }
    $assetPath = $guidPaths[$rewardGuid]
    $assetText = Get-Content -LiteralPath $assetPath -Raw
    $category = [regex]::Match($assetText, '(?m)^  category: (\d+)')
    $minimum = [regex]::Match($assetText, '(?m)^  minimumPlayerLevel: (\d+)')
    if (-not $category.Success -or -not $minimum.Success) { throw "Missing fields: $assetPath" }
    if ([int]$minimum.Groups[1].Value -ne 1) { throw "Expected level 1: $assetPath" }
    [pscustomobject]@{
        Path = $assetPath
        Category = [int]$category.Groups[1].Value
        MinimumPlayerLevel = [int]$minimum.Groups[1].Value
        SHA256 = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash
    }
})
$gameLogic = Get-Content -LiteralPath $gameLogicPath -Raw
if (-not $gameLogic.Contains('int targetLevel = Math.Max(1, xp.Level - xp.PendingRewards + 1);') -or
    -not $gameLogic.Contains('RewardDraftRules.Draw(pool, targetLevel, seed,')) {
    throw 'Reward-level routing changed; inspect the implementation.'
}
$report = [ordered]@{
    VerifiedAtUtc = [DateTime]::UtcNow.ToString('o')
    Scope = 'Offline compiled production RewardDraftRules; disk asset audit; static GameLogic routing check'
    RuleChecksPassed = $checks
    CatalogAssetCount = $assets.Count
    TargetCategoryAssetCount = @($assets | Where-Object { $_.Category -in 1,2,3,5 }).Count
    RulesSHA256 = (Get-FileHash -LiteralPath $rulesPath -Algorithm SHA256).Hash
    GameLogicSHA256 = (Get-FileHash -LiteralPath $gameLogicPath -Algorithm SHA256).Hash
    CatalogSHA256 = (Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash
    RewardLevelRouting = 'Max(1, Level - PendingRewards + 1); static inspection only'
    NotRun = @('Unity compilation', 'Unity EditMode tests', 'PurgersRegression', 'Host/Client', 'Build', 'Natural play')
    Assets = $assets
}
$reportPath = Join-Path $PSScriptRoot 'offline-verification.json'
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Output "PASS: $checks rule checks; $($assets.Count) catalog assets at level 1."
Write-Output "Report: $reportPath"
