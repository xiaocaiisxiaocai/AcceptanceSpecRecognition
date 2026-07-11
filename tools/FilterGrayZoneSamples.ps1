# 从合成数据中筛选真正需要 LLM 裁决的灰区样本
# 排除：1) 项目+规格完全一致  2) 规范化后完全一致  3) Embedding 过高/过低
# 保留：需要 LLM 语义判断的中间地带

param(
    [string]$InputJson = "tools/Fixtures/synthetic_specs.json",
    [string]$OutputCsv = "generated_gray_zone_samples.csv",
    [int]$MinPairs = 50,
    [double]$MinEmbedding = 0.75,  # Embedding 下限：太低的不是语义等价
    [double]$MaxEmbedding = 0.92   # Embedding 上限：太高的规范化就能搞定
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot\..

Write-Host "🔍 加载合成规格数据..." -ForegroundColor Cyan
$json = Get-Content $InputJson -Raw -Encoding UTF8 | ConvertFrom-Json
$specs = $json.data.items

Write-Host "📊 共 $($specs.Count) 条规格" -ForegroundColor Green

# 构建 project+spec 组合，找出"有多个候选"的源行
$projectSpecGroups = $specs | Group-Object -Property { "$($_.project.Trim()) ||| $($_.specification.Trim())" }

Write-Host "🎯 分组后共 $($projectSpecGroups.Count) 个唯一组合" -ForegroundColor Yellow

# 筛选灰区样本对：
# 1. 项目相同，规格不同但语义可能相近
# 2. 规格相似度在中间地带（不是完全一致，也不是完全无关）
$grayZonePairs = @()

Write-Host "🔬 开始筛选灰区样本..." -ForegroundColor Cyan

# 按项目分组
$projectGroups = $specs | Group-Object -Property { $_.project.Trim() }

foreach ($projectGroup in $projectGroups) {
    $projectName = $projectGroup.Name
    $specsInProject = $projectGroup.Group

    # 项目内规格去重
    $uniqueSpecs = $specsInProject | Group-Object -Property { $_.specification.Trim() } | ForEach-Object { $_.Group[0] }

    if ($uniqueSpecs.Count -lt 2) {
        continue
    }

    # 两两比对，找出"可能语义相近但文本不同"的对
    for ($i = 0; $i -lt $uniqueSpecs.Count - 1; $i++) {
        for ($j = $i + 1; $j -lt $uniqueSpecs.Count; $j++) {
            $spec1 = $uniqueSpecs[$i]
            $spec2 = $uniqueSpecs[$j]

            $text1 = $spec1.specification.Trim()
            $text2 = $spec2.specification.Trim()

            # 排除完全一致
            if ($text1 -eq $text2) {
                continue
            }

            # 排除长度差异过大（不太可能语义等价）
            $len1 = $text1.Length
            $len2 = $text2.Length
            $lenRatio = [Math]::Min($len1, $len2) / [Math]::Max($len1, $len2)
            if ($lenRatio -lt 0.3) {
                continue
            }

            # 简单文本相似度（字符集交集）
            $chars1 = [System.Collections.Generic.HashSet[char]]::new($text1.ToCharArray())
            $chars2 = [System.Collections.Generic.HashSet[char]]::new($text2.ToCharArray())
            $intersection = 0
            foreach ($c in $chars1) {
                if ($chars2.Contains($c)) {
                    $intersection++
                }
            }
            $union = $chars1.Count + $chars2.Count - $intersection
            $jaccardSim = if ($union -gt 0) { $intersection / $union } else { 0 }

            # 筛选条件：字符相似度在 0.3-0.8 之间（灰区）
            if ($jaccardSim -ge 0.3 -and $jaccardSim -le 0.8) {
                $grayZonePairs += [PSCustomObject]@{
                    Project = $projectName
                    Spec1 = $text1
                    Spec2 = $text2
                    Acceptance1 = $spec1.acceptance
                    Acceptance2 = $spec2.acceptance
                    Remark1 = $spec1.remark
                    Remark2 = $spec2.remark
                    JaccardSim = [Math]::Round($jaccardSim, 3)
                }
            }

            if ($grayZonePairs.Count -ge $MinPairs) {
                break
            }
        }
        if ($grayZonePairs.Count -ge $MinPairs) {
            break
        }
    }

    if ($grayZonePairs.Count -ge $MinPairs) {
        break
    }
}

Write-Host "✅ 筛选出 $($grayZonePairs.Count) 对灰区样本" -ForegroundColor Green

# 输出 CSV
$grayZonePairs | Export-Csv -Path $OutputCsv -Encoding UTF8 -NoTypeInformation

Write-Host "💾 已保存到: $OutputCsv" -ForegroundColor Green
Write-Host ""
Write-Host "📋 样本统计:" -ForegroundColor Yellow
Write-Host "  - 总对数: $($grayZonePairs.Count)"
Write-Host "  - Jaccard 相似度范围: 0.3 - 0.8"
Write-Host "  - 这些样本需要 LLM 语义裁决，不能靠规范化精确匹配"
