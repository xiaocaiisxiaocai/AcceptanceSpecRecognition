# 调用系统配置的 LLM 服务对淮安规格进行语义改写
param(
    [string]$ApiBaseUrl = "http://localhost:5291",
    [string]$InputJson = "tools/Fixtures/synthetic_specs.json",
    [string]$OutputExcel = "huaian_ai_paraphrased_50.xlsx",
    [int]$MaxCount = 50
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot\..

Write-Host "🔍 加载淮安规格数据..." -ForegroundColor Cyan
$json = Get-Content $InputJson -Raw -Encoding UTF8 | ConvertFrom-Json
$specs = $json.data.items | Select-Object -First $MaxCount

Write-Host "📊 共 $($specs.Count) 条规格待改写" -ForegroundColor Green

# 改写提示词模板
$systemPrompt = @"
你是一个专业的技术文档改写专家。你的任务是对验收规格文本进行同义改写（paraphrase）。

**要求**：
1. **语义完全等价**：改写后的文本必须表达与原文相同的技术要求
2. **表达方式不同**：改变句式、用词、语序，像"你吃饭了吗"改成"你用餐了吗"这样
3. **保留关键信息**：数值、单位、品牌、型号必须保留
4. **自然流畅**：符合中文表达习惯
5. **只输出改写后的文本**：不要任何解释或额外内容
"@

# 调用系统 API 进行改写
function Invoke-SystemParaphrase {
    param([string]$text)

    $requestBody = @{
        systemPrompt = $systemPrompt
        userPrompt = "请改写以下文本：`n`n$text"
        temperature = 0.7
        maxTokens = 500
    } | ConvertTo-Json -Depth 10

    try {
        $response = Invoke-RestMethod -Uri "$ApiBaseUrl/api/ai-service/test-llm" `
            -Method Post `
            -Body $requestBody `
            -ContentType "application/json; charset=utf-8" `
            -TimeoutSec 120

        if ($response.success) {
            return $response.data.response.Trim()
        }
        else {
            Write-Host "  ❌ API 返回失败: $($response.message)" -ForegroundColor Red
            return $null
        }
    }
    catch {
        Write-Host "  ❌ 改写失败: $_" -ForegroundColor Red
        return $null
    }
}

# 批量改写
$results = @()
$successCount = 0

foreach ($spec in $specs) {
    $index = $specs.IndexOf($spec) + 1
    $projectPreview = $spec.project.Substring(0, [Math]::Min(30, $spec.project.Length))
    Write-Host "🔄 [$index/$($specs.Count)] 改写: $projectPreview..." -ForegroundColor Cyan

    $paraphrased = Invoke-SystemParaphrase -text $spec.specification

    if ($paraphrased -and $paraphrased.Trim() -ne "" -and $paraphrased -ne $spec.specification.Trim()) {
        $results += [PSCustomObject]@{
            项目 = $spec.project
            规格 = $paraphrased
        }
        $successCount++
        Write-Host "  ✅ 成功" -ForegroundColor Green
    }
    else {
        Write-Host "  ⚠️  跳过（未改变或失败）" -ForegroundColor Yellow
    }

    # 避免频繁调用
    Start-Sleep -Milliseconds 1000
}

Write-Host ""
Write-Host "✅ 成功改写 $successCount 条" -ForegroundColor Green

if ($results.Count -eq 0) {
    Write-Host "❌ 没有成功改写的数据，无法生成 Excel" -ForegroundColor Red
    exit 1
}

# 生成 Excel
Write-Host "📝 生成 Excel..." -ForegroundColor Cyan

$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false

$workbook = $excel.Workbooks.Add()
$worksheet = $workbook.Worksheets.Item(1)
$worksheet.Name = "AI语义改写"

# 表头
$worksheet.Cells.Item(1, 1) = "项目"
$worksheet.Cells.Item(1, 2) = "规格"

# 数据
$row = 2
foreach ($item in $results) {
    $worksheet.Cells.Item($row, 1) = $item.项目
    $worksheet.Cells.Item($row, 2) = $item.规格
    $row++
}

# 自动调整列宽
$worksheet.UsedRange.EntireColumn.AutoFit() | Out-Null

# 保存
$fullPath = Join-Path (Get-Location) $OutputExcel
$workbook.SaveAs($fullPath)
$workbook.Close()
$excel.Quit()

# 释放 COM
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($worksheet) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($workbook) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
[System.GC]::Collect()

Write-Host "💾 已保存到: $OutputExcel" -ForegroundColor Green
Write-Host ""
Write-Host "📋 使用说明:" -ForegroundColor Yellow
Write-Host "  1. 这个 Excel 只有【项目】和【规格】两列"
Write-Host "  2. 规格列是 LLM 深度改写的，表达方式完全不同但语义等价"
Write-Host "  3. 导入系统做智能填充，应该能匹配到库里的原始规格"
