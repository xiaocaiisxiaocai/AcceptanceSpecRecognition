using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var baseUrl = GetArg(args, "--base", "http://localhost:5006").TrimEnd('/');
        var docxPath = GetArg(args, "--docx", Path.Combine("docs", "example.docx"));

        var tableIndex = int.Parse(GetArg(args, "--table", "0"));
        var projectCol = int.Parse(GetArg(args, "--project-col", "0"));
        var specCol = int.Parse(GetArg(args, "--spec-col", "1"));
        var acceptanceCol = int.Parse(GetArg(args, "--acceptance-col", "2"));
        var remarkCol = int.Parse(GetArg(args, "--remark-col", "3"));

        Console.WriteLine($"Base: {baseUrl}");
        Console.WriteLine($"DOCX: {docxPath}");
        Console.WriteLine($"TableIndex={tableIndex}, projectCol={projectCol}, specCol={specCol}, acceptanceCol={acceptanceCol}, remarkCol={remarkCol}");

        if (!File.Exists(docxPath))
        {
            Console.Error.WriteLine($"File not found: {docxPath}");
            return 2;
        }

        var json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        using var http = new HttpClient { BaseAddress = new Uri(baseUrl + "/") };

        // 1) Upload
        var uploadResp = await UploadAsync(http, docxPath, json);
        Console.WriteLine("\n=== UPLOAD ===");
        Dump(uploadResp);
        EnsureOk(uploadResp);
        var fileId = uploadResp.Data!.FileId;

        // 2) Tables
        var tables = await GetAsync<ApiResponse<List<TableInfoDto>>>(http, $"api/documents/{fileId}/tables", json);
        Console.WriteLine("\n=== TABLES ===");
        Dump(tables);
        EnsureOk(tables);
        if (tables.Data == null || tables.Data.Count == 0)
            throw new Exception("No tables returned");

        // 3) Seed customer/process/spec
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var customer = await PostJsonAsync<ApiResponse<CustomerDto>>(http, "api/customers",
            new { name = $"测试客户_{suffix}" }, json);
        Console.WriteLine("\n=== CREATE CUSTOMER ===");
        Dump(customer);
        EnsureOk(customer);

        var process = await PostJsonAsync<ApiResponse<ProcessDto>>(http, "api/processes",
            new { customerId = customer.Data!.Id, name = $"测试制程_{suffix}" }, json);
        Console.WriteLine("\n=== CREATE PROCESS ===");
        Dump(process);
        EnsureOk(process);

        var spec = await PostJsonAsync<ApiResponse<AcceptanceSpecDto>>(http, "api/specs",
            new
            {
                processId = process.Data!.Id,
                project = "示例项目",
                specification = "示例规格",
                acceptance = "OK",
                remark = "自动填充备注"
            }, json);
        Console.WriteLine("\n=== CREATE SPEC ===");
        Dump(spec);
        EnsureOk(spec);

        // 4) Preview (file mode + manual cols)
        var preview = await PostJsonAsync<ApiResponse<MatchPreviewResponseDto>>(http, "api/matching/preview",
            new
            {
                fileId,
                tableIndex,
                projectColumnIndex = projectCol,
                specificationColumnIndex = specCol,
                customerId = customer.Data!.Id,
                processId = process.Data!.Id,
                config = new
                {
                    useLevenshtein = true,
                    useJaccard = true,
                    useCosine = true,
                    minScoreThreshold = 0.0
                }
            }, json);
        Console.WriteLine("\n=== PREVIEW ===");
        Dump(preview);
        EnsureOk(preview);

        var first = preview.Data!.Items.FirstOrDefault();
        if (first == null)
            throw new Exception("No preview items");

        var rowIndex = first.RowIndex;
        var chosenSpecId = first.BestMatch?.SpecId ?? spec.Data!.Id;

        Console.WriteLine($"\nUsing rowIndex={rowIndex}, specId={chosenSpecId}");

        // 5) Execute
        var exec = await PostJsonAsync<ApiResponse<ExecuteFillResponseDto>>(http, "api/matching/execute",
            new
            {
                fileId,
                tableIndex,
                acceptanceColumnIndex = acceptanceCol,
                remarkColumnIndex = remarkCol,
                mappings = new[] { new { rowIndex, specId = chosenSpecId } }
            }, json);
        Console.WriteLine("\n=== EXECUTE ===");
        Dump(exec);
        EnsureOk(exec);

        // 6) Download
        var taskId = exec.Data!.TaskId;
        var bytes = await http.GetByteArrayAsync($"api/matching/download/{taskId}");
        var outPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(docxPath) ?? ".", $"filled_{taskId}.docx"));
        await File.WriteAllBytesAsync(outPath, bytes);
        Console.WriteLine("\n=== DOWNLOADED ===");
        Console.WriteLine(outPath);

        return 0;
    }

    private static async Task<ApiResponse<FileUploadResponseDto>> UploadAsync(HttpClient http, string path, JsonSerializerOptions json)
    {
        await using var fs = File.OpenRead(path);
        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(fs);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        content.Add(fileContent, "file", Path.GetFileName(path));
        using var resp = await http.PostAsync("api/documents/upload", content);
        var text = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ApiResponse<FileUploadResponseDto>>(text, json)
               ?? throw new Exception("Failed to parse upload response");
    }

    private static async Task<T> GetAsync<T>(HttpClient http, string url, JsonSerializerOptions json)
    {
        var text = await http.GetStringAsync(url);
        return JsonSerializer.Deserialize<T>(text, json)
               ?? throw new Exception($"Failed to parse GET {url}");
    }

    private static async Task<T> PostJsonAsync<T>(HttpClient http, string url, object body, JsonSerializerOptions json)
    {
        var payload = JsonSerializer.Serialize(body, json);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await http.PostAsync(url, content);
        var text = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(text, json)
               ?? throw new Exception($"Failed to parse POST {url}");
    }

    private static void EnsureOk(ApiResponse resp)
    {
        if (resp.Code != 0)
            throw new Exception($"API error {resp.Code}: {resp.Message}");
    }

    private static void Dump<T>(T obj)
    {
        var pretty = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(pretty);
    }

    private static string GetArg(string[] args, string name, string fallback)
    {
        var idx = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0 && idx + 1 < args.Length)
            return args[idx + 1];
        return fallback;
    }
}

public class ApiResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

public class ApiResponse<T> : ApiResponse
{
    [JsonPropertyName("data")] public T? Data { get; set; }
}

public class FileUploadResponseDto
{
    [JsonPropertyName("fileId")] public int FileId { get; set; }
    [JsonPropertyName("fileName")] public string FileName { get; set; } = "";
    [JsonPropertyName("fileHash")] public string FileHash { get; set; } = "";
    [JsonPropertyName("isDuplicate")] public bool IsDuplicate { get; set; }
    [JsonPropertyName("tableCount")] public int TableCount { get; set; }
}

public class TableInfoDto
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("rowCount")] public int RowCount { get; set; }
    [JsonPropertyName("columnCount")] public int ColumnCount { get; set; }
    [JsonPropertyName("isNested")] public bool IsNested { get; set; }
    [JsonPropertyName("previewText")] public string? PreviewText { get; set; }
    [JsonPropertyName("headers")] public List<string> Headers { get; set; } = new();
    [JsonPropertyName("hasMergedCells")] public bool HasMergedCells { get; set; }
}

public class CustomerDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

public class ProcessDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

public class AcceptanceSpecDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("project")] public string Project { get; set; } = "";
    [JsonPropertyName("specification")] public string Specification { get; set; } = "";
    [JsonPropertyName("acceptance")] public string? Acceptance { get; set; }
    [JsonPropertyName("remark")] public string? Remark { get; set; }
}

public class MatchPreviewResponseDto
{
    [JsonPropertyName("items")] public List<MatchPreviewItemDto> Items { get; set; } = new();
}

public class MatchPreviewItemDto
{
    [JsonPropertyName("rowIndex")] public int RowIndex { get; set; }
    [JsonPropertyName("bestMatch")] public MatchResultDto? BestMatch { get; set; }
}

public class MatchResultDto
{
    [JsonPropertyName("specId")] public int SpecId { get; set; }
}

public class ExecuteFillResponseDto
{
    [JsonPropertyName("taskId")] public string TaskId { get; set; } = "";
}

