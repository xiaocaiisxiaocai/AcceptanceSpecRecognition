# 从淮安数据中提取"需要 LLM 语义裁决"的源行
# 排除：项目+规格在库中有完全一致的候选
# 保留：需要语义匹配才能找到候选的源行

param(
    [string]$InputJson = "huaian_specs.json",
    [string]$OutputCsv = "huaian_llm_semantic_sources.csv",
    [int]$SampleCount = 50
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot\..

Write-Host "🔍 加载淮安规格数据..." -ForegroundColor Cyan
$json = Get-Content $InputJson -Raw -Encoding UTF8 | ConvertFrom-Json
$specs = $json.data.items

Write-Host "📊 共 $($specs.Count) 条规格" -ForegroundColor Green

# 构建精确匹配索引（项目+规格）
$exactIndex = @{}
foreach ($spec in $specs) {
    $key = "$($spec.project.Trim())|$($spec.specification.Trim())"
    if (-not $exactIndex.ContainsKey($key)) {
        $exactIndex[$key] = @()
    }
    $exactIndex[$key] += $spec
}

Write-Host "🎯 精确索引中有 $($exactIndex.Keys.Count) 个唯一项" -ForegroundColor Yellow

# 筛选灰区源行：同项目下有其他规格，但规格文本不完全一致
$grayZoneSources = @()

# 按项目分组
$projectGroups = $specs | Group-Object -Property { $_.project.Trim() }

foreach ($projectGroup in $projectGroups) {
    $projectName = $projectGroup.Name
    $specsInProject = $projectGroup.Group

    # 项目内规格去重
    $uniqueSpecs = $specsInProject |
        Group-Object -Property { $_.specification.Trim() } |
        ForEach-Object { $_.Group[0] }

    # 至少要有2个不同规格，才有语义匹配的可能
    if ($uniqueSpecs.Count -lt 2) {
        continue
    }

    # 选择规格文本有差异的行作为灰区源
    foreach ($spec in $uniqueSpecs) {
        $specText = $spec.specification.Trim()

        # 检查是否有"相似但不完全一致"的其他规格
        $hasSimilarSpec = $false
        foreach ($otherSpec in $uniqueSpecs) {
            if ($otherSpec.id -eq $spec.id) {
                continue
            }

            $otherText = $otherSpec.specification.Trim()

            # 不完全一致
            if ($specText -eq $otherText) {
                continue
            }

            # 长度相近（比例 > 0.3）
            $len1 = $specText.Length
            $len2 = $otherText.Length
            $lenRatio = [Math]::Min($len1, $len2) / [Math]::Max($len1, $len2)
            if ($lenRatio -lt 0.3) {
                continue
            }

            # 有一定字符重叠
            $chars1 = [System.Collections.Generic.HashSet[char]]::new($specText.ToCharArray())
            $chars2 = [System.Collections.Generic.HashSet[char]]::new($otherText.ToCharArray())
            $intersection = 0
            foreach ($c in $chars1) {
                if ($chars2.Contains($c)) {
                    $intersection++
                }
            }
            $union = $chars1.Count + $chars2.Count - $intersection
            $jaccardSim = if ($union -gt 0) { $intersection / $union } else { 0 }

            # Jaccard 相似度在 0.3-0.8 之间，认为是可能的灰区
            if ($jaccardSim -ge 0.3 -and $jaccardSim -le 0.8) {
                $hasSimilarSpec = $true
                break
            }
        }

        if ($hasSimilarSpec) {
            $grayZoneSources += [PSCustomObject]@{
                项目 = $spec.project
                规格 = $spec.specification
                验收 = $spec.acceptance
                备注 = $spec.remark
            }

            if ($grayZoneSources.Count -ge $SampleCount) {
                break
            }
        }
    }

    if ($grayZoneSources.Count -ge $SampleCount) {
        break
    }
}

Write-Host "✅ 筛选出 $($grayZoneSources.Count) 个灰区源行" -ForegroundColor Green

# 输出 CSV
$grayZoneSources | Export-Csv -Path $OutputCsv -Encoding UTF8 -NoTypeInformation

Write-Host "💾 已保存到: $OutputCsv" -ForegroundColor Green
Write-Host ""
Write-Host "📋 样本说明:" -ForegroundColor Yellow
Write-Host "  - 这些源行的项目下有其他规格，但规格文本不完全一致"
Write-Host "  - 需要 LLM 语义判断才能找到正确的匹配"
Write-Host "  - 不能通过规范化精确匹配自动完成"
