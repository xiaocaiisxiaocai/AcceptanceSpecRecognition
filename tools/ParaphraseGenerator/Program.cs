using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ParaphraseGenerator;

// 配置
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("../../../src/AcceptanceSpecSystem.Api/appsettings.json", optional: false)
    .AddJsonFile("../../../src/AcceptanceSpecSystem.Api/appsettings.Development.json", optional: true)
    .Build();

// DI
var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

services.AddSingleton<IConfiguration>(configuration);
services.AddAcceptanceAiServices(configuration);
services.AddSingleton<ParaphraseGenerator.ParaphraseGenerator>();

var serviceProvider = services.BuildServiceProvider();
var generator = serviceProvider.GetRequiredService<ParaphraseGenerator.ParaphraseGenerator>();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

// 读取合成测试数据
var inputFile = args.Length > 0 ? args[0] : "tools/Fixtures/synthetic_specs.json";
var outputFile = args.Length > 1 ? args[1] : "generated_ai_paraphrased_50.csv";
var maxCount = args.Length > 2 ? int.Parse(args[2]) : 50;

logger.LogInformation("读取输入文件: {File}", inputFile);
var json = await File.ReadAllTextAsync(inputFile);
var doc = JsonDocument.Parse(json);
var items = doc.RootElement.GetProperty("data").GetProperty("items");

var specs = new List<SpecItem>();
foreach (var item in items.EnumerateArray())
{
    specs.Add(new SpecItem
    {
        Project = item.GetProperty("project").GetString() ?? string.Empty,
        Specification = item.GetProperty("specification").GetString() ?? string.Empty,
        Acceptance = item.TryGetProperty("acceptance", out var acc) ? acc.GetString() : null,
        Remark = item.TryGetProperty("remark", out var rem) ? rem.GetString() : null
    });
}

logger.LogInformation("共 {Count} 条规格，将改写 {MaxCount} 条", specs.Count, maxCount);

// 批量改写
var results = await generator.BatchParaphraseAsync(specs, maxCount, CancellationToken.None);

logger.LogInformation("成功改写 {Count} 条", results.Count);

// 输出 CSV（只保留项目和改写后的规格）
var csv = new StringBuilder();
csv.AppendLine("项目,规格");

foreach (var result in results)
{
    var project = EscapeCsvField(result.Project);
    var spec = EscapeCsvField(result.ParaphrasedSpecification);
    csv.AppendLine($"{project},{spec}");
}

await File.WriteAllTextAsync(outputFile, csv.ToString(), new UTF8Encoding(true));

logger.LogInformation("已保存到: {File}", outputFile);

// 同时保存对照版本（用于验证）
var compareFile = outputFile.Replace(".csv", "_compare.json");
var compareJson = JsonSerializer.Serialize(results, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
});
await File.WriteAllTextAsync(compareFile, compareJson, Encoding.UTF8);
logger.LogInformation("对照版本已保存到: {File}", compareFile);

static string EscapeCsvField(string field)
{
    if (string.IsNullOrEmpty(field))
        return string.Empty;

    // 如果包含逗号、换行或引号，需要用引号包裹并转义内部引号
    if (field.Contains(',') || field.Contains('\n') || field.Contains('"'))
    {
        return $"\"{field.Replace("\"", "\"\"")}\"";
    }

    return field;
}
