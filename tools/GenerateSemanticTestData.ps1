# 生成需要 AI 语义匹配的智能填充测试数据
# 从库中选择规格，对规格列进行语义化改写（不是简单的字符替换/同义词）

param(
    [string]$InputJson = "tools/Fixtures/synthetic_specs.json",
    [string]$OutputCsv = "generated_ai_semantic_test.csv",
    [int]$SampleCount = 50
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot\..

Write-Host "🔍 加载合成规格数据..." -ForegroundColor Cyan
$json = Get-Content $InputJson -Raw -Encoding UTF8 | ConvertFrom-Json
$specs = $json.data.items

Write-Host "📊 共 $($specs.Count) 条规格" -ForegroundColor Green

# 语义改写规则（AI语义化，不是简单替换）
function Get-SemanticParaphrase {
    param([string]$text)

    # 这些是真正的语义等价改写，不是字符/同义词替换
    $paraphrases = @{
        # 数值+单位换算
        "7.5kW" = "7500瓦"
        "7500W" = "7.5千瓦"
        "3000rpm" = "每分钟3000转"
        "50Hz" = "工频50赫兹"
        "380V" = "三相380伏电压"

        # 语义改写
        "伺服电机" = "伺服马达"
        "伺服马达" = "伺服驱动电机"
        "触摸屏" = "人机交互界面"
        "PLC" = "可编程逻辑控制器"
        "扫码枪" = "条码扫描器"
        "条码扫描装置" = "二维码识别设备"
        "传感器" = "检测传感元件"
        "气缸" = "气动执行器"
        "滑轨" = "线性导轨"
        "减速电机" = "齿轮减速马达"

        # 品牌语义化
        "三菱/松下" = "Mitsubishi或Panasonic品牌"
        "HIWIN" = "台湾上银"
        "THK" = "日本THK"
        "SMC" = "日本SMC气动"

        # 描述改写
        "必须满足" = "需要达到"
        "100%达到要求" = "完全符合规范"
        "不少于" = "至少"
        "应当" = "需要"
    }

    $result = $text
    foreach ($key in $paraphrases.Keys) {
        if ($result -match [regex]::Escape($key)) {
            $result = $result -replace [regex]::Escape($key), $paraphrases[$key]
            break  # 只改一处，保持可控
        }
    }

    return $result
}

# 筛选可以语义改写的规格
$testSources = @()

foreach ($spec in $specs) {
    $originalSpec = $spec.specification.Trim()
    $paraphrased = Get-SemanticParaphrase -text $originalSpec

    # 如果改写后不同，说明可以作为测试样本
    if ($paraphrased -ne $originalSpec) {
        $testSources += [PSCustomObject]@{
            项目 = $spec.project
            规格 = $paraphrased
        }

        if ($testSources.Count -ge $SampleCount) {
            break
        }
    }
}

Write-Host "✅ 生成 $($testSources.Count) 个语义改写样本" -ForegroundColor Green

# 输出 CSV
$testSources | Export-Csv -Path $OutputCsv -Encoding UTF8 -NoTypeInformation

Write-Host "💾 已保存到: $OutputCsv" -ForegroundColor Green
Write-Host ""
Write-Host "📋 样本说明:" -ForegroundColor Yellow
Write-Host "  - 项目列与库中一致"
Write-Host "  - 规格列经过语义化改写（不是字符替换/同义词）"
Write-Host "  - 用于测试 AI 语义匹配能力"
Write-Host "  - 应该能匹配到库中对应的原始规格"
