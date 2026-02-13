param(
  [string]$ApiBase = "http://localhost:5005",
  [string]$DocxPath = "docs/example.docx",
  [int]$TableIndex = -1,
  [int]$ProjectColumnIndex = 0,
  [int]$SpecificationColumnIndex = 1,
  [int]$AcceptanceColumnIndex = 2,
  [int]$RemarkColumnIndex = 3
)

$ErrorActionPreference = "Stop"

function CurlJson {
  param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Args)
  $raw = & curl.exe -sS @Args
  if ($LASTEXITCODE -ne 0) { throw "curl.exe failed: $($Args -join ' ')" }
  return ($raw | ConvertFrom-Json)
}

Write-Host "API Base: $ApiBase"
Write-Host "DOCX: $DocxPath"

if (-not (Test-Path $DocxPath)) {
  throw "Docx file not found: $DocxPath"
}

# 1) Upload
$upload = CurlJson -F ("file=@{0}" -f $DocxPath) ("{0}/api/documents/upload" -f $ApiBase)
Write-Host "`n=== UPLOAD ==="
$upload | ConvertTo-Json -Depth 8
if ($upload.code -ne 0) { throw "Upload failed: $($upload.message)" }

$fileId = $upload.data.fileId
if (-not $fileId) { throw "No fileId returned from upload" }

# 2) List tables
$tables = CurlJson ("{0}/api/documents/{1}/tables" -f $ApiBase, $fileId)
Write-Host "`n=== TABLES ==="
$tables | ConvertTo-Json -Depth 8
if ($tables.code -ne 0) { throw "Get tables failed: $($tables.message)" }
if (-not $tables.data -or $tables.data.Count -eq 0) { throw "No tables returned" }

if ($TableIndex -lt 0) {
  $TableIndex = $tables.data[0].index
}

Write-Host "`nUsing tableIndex=$TableIndex"
Write-Host "Manual columns (0-based): project=$ProjectColumnIndex, spec=$SpecificationColumnIndex, acceptance=$AcceptanceColumnIndex, remark=$RemarkColumnIndex"

# 3) Seed one customer/process/spec so preview has at least one candidate
$suffix = [Guid]::NewGuid().ToString("N").Substring(0, 8)
$customerName = "测试客户_" + $suffix
$processName = "测试制程_" + $suffix

$customer = CurlJson -H "Content-Type: application/json" -d ("{""name"":""{0}""}" -f $customerName) ("{0}/api/customers" -f $ApiBase)
Write-Host "`n=== CREATE CUSTOMER ==="
$customer | ConvertTo-Json -Depth 8
if ($customer.code -ne 0) { throw "Create customer failed: $($customer.message)" }
$customerId = $customer.data.id

$process = CurlJson -H "Content-Type: application/json" -d ("{""customerId"":{0},""name"":""{1}""}" -f $customerId, $processName) ("{0}/api/processes" -f $ApiBase)
Write-Host "`n=== CREATE PROCESS ==="
$process | ConvertTo-Json -Depth 8
if ($process.code -ne 0) { throw "Create process failed: $($process.message)" }
$processId = $process.data.id

$spec = CurlJson -H "Content-Type: application/json" -d ("{""processId"":{0},""project"":""示例项目"",""specification"":""示例规格"",""acceptance"":""OK"",""remark"":""自动填充备注""}" -f $processId) ("{0}/api/specs" -f $ApiBase)
Write-Host "`n=== CREATE SPEC ==="
$spec | ConvertTo-Json -Depth 8
if ($spec.code -ne 0) { throw "Create spec failed: $($spec.message)" }
$specId = $spec.data.id

# 4) Preview (file mode + manual columns)
$previewBody = @{
  fileId = [int]$fileId
  tableIndex = [int]$TableIndex
  projectColumnIndex = [int]$ProjectColumnIndex
  specificationColumnIndex = [int]$SpecificationColumnIndex
  customerId = [int]$customerId
  processId = [int]$processId
  config = @{
    useLevenshtein = $true
    useJaccard = $true
    useCosine = $true
    minScoreThreshold = 0.0
  }
} | ConvertTo-Json -Depth 8

$preview = CurlJson -H "Content-Type: application/json" -d $previewBody ("{0}/api/matching/preview" -f $ApiBase)
Write-Host "`n=== PREVIEW ==="
$preview | ConvertTo-Json -Depth 10
if ($preview.code -ne 0) { throw "Preview failed: $($preview.message)" }
if (-not $preview.data.items -or $preview.data.items.Count -eq 0) { throw "No preview items" }

$first = $preview.data.items[0]
$rowIndex = $first.rowIndex
$chosenSpecId = if ($first.bestMatch -and $first.bestMatch.specId) { $first.bestMatch.specId } else { $specId }

Write-Host "`nUsing rowIndex=$rowIndex, specId=$chosenSpecId"

# 5) Execute fill (manual acceptance/remark column indices)
$execBody = @{
  fileId = [int]$fileId
  tableIndex = [int]$TableIndex
  acceptanceColumnIndex = [int]$AcceptanceColumnIndex
  remarkColumnIndex = [int]$RemarkColumnIndex
  mappings = @(@{ rowIndex = [int]$rowIndex; specId = [int]$chosenSpecId })
} | ConvertTo-Json -Depth 6

$exec = CurlJson -H "Content-Type: application/json" -d $execBody ("{0}/api/matching/execute" -f $ApiBase)
Write-Host "`n=== EXECUTE ==="
$exec | ConvertTo-Json -Depth 8
if ($exec.code -ne 0) { throw "Execute failed: $($exec.message)" }
$taskId = $exec.data.taskId
if (-not $taskId) { throw "No taskId returned" }

# 6) Download
$outFile = Join-Path (Get-Location) ("filled_{0}.docx" -f $taskId)
& curl.exe -sS -o $outFile ("{0}/api/matching/download/{1}" -f $ApiBase, $taskId)
if ($LASTEXITCODE -ne 0) { throw "Download failed" }

Write-Host "`n=== DOWNLOADED ==="
Write-Host $outFile

