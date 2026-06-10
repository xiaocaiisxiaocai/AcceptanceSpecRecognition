using AcceptanceSpecSystem.Api;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class SmartFillKnowledgeConfigurationTests
{
    [Fact]
    public void AddApiLayerServices_ShouldLoadSmartFillKnowledgeFromConfiguredPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"smart-fill-api-rules-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
        {
          "brands": [
            {
              "canonical": "ApiExternalBrand",
              "aliases": [ "接口外置品牌", "ApiExternalBrand" ]
            }
          ],
          "brandDeviceWords": [ "接口测试设备" ],
          "units": [
            {
              "dimension": "api_external_unit",
              "factor": 10,
              "tokens": [ "apiUnit" ]
            }
          ]
        }
        """);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SmartFillKnowledge:RulesPath"] = path
                })
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddApiLayerServices(configuration);

            using var provider = services.BuildServiceProvider();
            var canonicalizer = provider.GetRequiredService<ISpecCanonicalizer>();

            canonicalizer.Canonicalize("品牌要求 接口外置品牌接口测试设备")
                .Should()
                .Be(canonicalizer.Canonicalize("品牌要求 ApiExternalBrand接口测试设备"));

            canonicalizer.TryNormalizeToBaseUnit(2, "apiUnit", out var value, out var dimension)
                .Should()
                .BeTrue();
            value.Should().Be(20);
            dimension.Should().Be("api_external_unit");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
