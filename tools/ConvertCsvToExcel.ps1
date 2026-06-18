# 将 CSV 转换为 Excel 格式
param(
    [string]$InputCsv = "huaian_gray_zone_samples.csv",
    [string]$OutputExcel = "huaian_gray_zone_samples.xlsx"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot\..

Write-Host "📄 读取 CSV: $InputCsv" -ForegroundColor Cyan
$data = Import-Csv -Path $InputCsv -Encoding UTF8

Write-Host "📊 共 $($data.Count) 行数据" -ForegroundColor Green

# 创建 Excel
$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false

$workbook = $excel.Workbooks.Add()
$worksheet = $workbook.Worksheets.Item(1)
$worksheet.Name = "灰区样本"

Write-Host "✍️ 写入 Excel..." -ForegroundColor Cyan

# 写入表头
$headers = $data[0].PSObject.Properties.Name
for ($col = 0; $col -lt $headers.Count; $col++) {
    $worksheet.Cells.Item(1, $col + 1) = $headers[$col]
}

# 写入数据
$row = 2
foreach ($item in $data) {
    $col = 1
    foreach ($header in $headers) {
        $worksheet.Cells.Item($row, $col) = $item.$header
        $col++
    }
    $row++
}

# 自动调整列宽
$usedRange = $worksheet.UsedRange
$usedRange.EntireColumn.AutoFit() | Out-Null

# 保存
$fullPath = Join-Path (Get-Location) $OutputExcel
$workbook.SaveAs($fullPath)
$workbook.Close()
$excel.Quit()

# 释放 COM 对象
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($worksheet) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($workbook) | Out-Null
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
[System.GC]::Collect()
[System.GC]::WaitForPendingFinalizers()

Write-Host "✅ 已保存到: $OutputExcel" -ForegroundColor Green
