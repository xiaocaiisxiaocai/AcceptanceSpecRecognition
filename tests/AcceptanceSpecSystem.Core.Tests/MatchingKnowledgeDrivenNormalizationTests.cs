using System.Diagnostics;
using AcceptanceSpecSystem.Core.Matching.Services;
using AcceptanceSpecSystem.Core.TextProcessing.Services;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests;

public class MatchingKnowledgeDrivenNormalizationTests
{
    [Fact]
    public async Task MinimalTextPreprocessingPipeline_ShouldOnlyNormalizeWhitespace()
    {
        var pipeline = new MinimalTextPreprocessingPipeline();

        var session = await pipeline.CreateSessionAsync();

        session.Process("  PASS \r\n NG\t ").Should().Be("PASS NG");
        session.Process("宽尺寸   <  0.5cm").Should().Be("宽尺寸 < 0.5cm");

        // 繁体不被转换
        session.Process("寬度").Should().Be("寬度");
        // 同义词不被替换
        session.Process("松下").Should().Be("松下");
        // 单位不被展开
        session.Process("厘米").Should().Be("厘米");
    }

    [Fact]
    public void SpecCanonicalizer_ShouldNormalizeBrandAdjacentDeviceWordsQuickly()
    {
        var canonicalizer = new SpecCanonicalizer();
        var texts = new[]
        {
            "断路器品牌要求 用例002 品牌要求 Schneider断路器",
            "工业相机品牌要求 用例026 品牌要求 Basler工业相机",
            "机器人品牌要求 用例007 品牌要求 Yaskawa机器人",
            "读码器品牌要求 用例025 品牌要求 Cognex读码器",
            "相机品牌要求 用例027 品牌要求 Hikrobot相机",
            "光电开关品牌要求 用例006 品牌要求 Keyence光电开关",
            "接近开关品牌要求 用例003 品牌要求 Omron接近开关",
            "PLC品牌要求 用例001 品牌要求 SiemensPLC",
            "传感器品牌要求 用例020 品牌要求 Balluff传感器",
            "伺服电机品牌要求 用例005 品牌要求 Panasonic伺服电机"
        };

        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 9; i++)
        {
            foreach (var text in texts)
            {
                canonicalizer.Canonicalize(text).Should().NotBeNullOrWhiteSpace();
            }
        }

        watch.Stop();
        watch.ElapsedMilliseconds.Should().BeLessThan(2000);
    }

    [Theory]
    [InlineData("品牌要求 SiemensPLC", "品牌要求 西门子PLC")]
    [InlineData("品牌要求 Schneider断路器", "品牌要求 施耐德断路器")]
    [InlineData("品牌要求 Panasonic伺服电机", "品牌要求 松下伺服电机")]
    [InlineData("品牌要求 Rexroth液压阀", "品牌要求 力士乐液压阀")]
    [InlineData("品牌要求 Honeywell安全模块", "品牌要求 霍尼韦尔安全模块")]
    [InlineData("品牌要求 Advantech工控机", "品牌要求 研华工控机")]
    [InlineData("品牌要求 Pilz安全继电器", "品牌要求 皮尔磁安全继电器")]
    [InlineData("品牌要求 Airtac电磁阀", "品牌要求 亚德客电磁阀")]
    [InlineData("品牌要求 Inovance PLC", "品牌要求 汇川PLC")]
    [InlineData("品牌要求 Leadshine步进驱动", "品牌要求 雷赛步进驱动")]
    [InlineData("品牌要求 STEP伺服", "品牌要求 新时达伺服")]
    [InlineData("品牌要求 HCFA伺服", "品牌要求 禾川伺服")]
    [InlineData("品牌要求 Adtech控制器", "品牌要求 众为兴控制器")]
    [InlineData("品牌要求 MindVision相机", "品牌要求 迈德威视相机")]
    [InlineData("品牌要求 Han协作臂", "品牌要求 大族机器人协作臂")]
    [InlineData("品牌要求 Banner光电传感器", "品牌要求 邦纳光电传感器")]
    [InlineData("品牌要求 Sick安全光栅", "品牌要求 西克安全光栅")]
    [InlineData("品牌要求 Pepperl-Fuchs光电", "品牌要求 倍加福光电")]
    [InlineData("品牌要求 Mech-Mind 3D相机", "品牌要求 梅卡曼德3D相机")]
    public void SpecCanonicalizer_ShouldKeepBrandAliasAdjacentDeviceEquivalence(string left, string right)
    {
        var canonicalizer = new SpecCanonicalizer();

        canonicalizer.Canonicalize(left).Should().Be(canonicalizer.Canonicalize(right));
    }

    [Theory]
    [InlineData("品牌要求 Mean Well开关电源", "品牌要求 明纬开关电源")]
    [InlineData("品牌要求 Moxa交换机", "品牌要求 摩莎交换机")]
    [InlineData("品牌要求 Autonics传感器", "品牌要求 奥托尼克斯传感器")]
    [InlineData("品牌要求 Oriental Motor步进电机", "品牌要求 东方马达步进电机")]
    [InlineData("品牌要求 Rittal电柜", "品牌要求 威图电柜")]
    public void SpecCanonicalizer_ShouldLoadDefaultExternalBrandRules(string left, string right)
    {
        var canonicalizer = new SpecCanonicalizer();

        canonicalizer.Canonicalize(left).Should().Be(canonicalizer.Canonicalize(right));
    }

    [Theory]
    [InlineData("保持时间为30min", "保持时间为0.5h")]
    [InlineData("定位误差为8mm到12mm", "定位误差为10±2mm")]
    [InlineData("输送线速度400mm/s到600mm/s", "输送线速度0.4-0.6m/s")]
    [InlineData("整线产能为60upm", "整线产能为3600uph")]
    [InlineData("AI算力为5000GOPS", "AI算力为5TOPS")]
    [InlineData("相机分辨率为500万像素", "相机分辨率为5MP")]
    [InlineData("计数能力为1ct/s", "计数能力为60ct/min")]
    [InlineData("托盘处理能力为20tray/min", "托盘处理能力为1200tray/h")]
    [InlineData("贴标速度为0.03kppm", "贴标速度为30ppm")]
    [InlineData("包装产能为20pcs/min", "包装产能为1200pcs/h")]
    [InlineData("贴标能力为2pcs/s", "贴标能力为120pcs/min")]
    [InlineData("循环能力为3cycle/s", "循环能力为180cycle/min")]
    [InlineData("工站产能为12站/min", "工站产能为720站/h")]
    [InlineData("处理能力为0.3kqps", "处理能力为300qps")]
    [InlineData("制动电阻为2kΩ", "制动电阻为2000Ω")]
    [InlineData("滤波电容为1mF", "滤波电容为1000μF")]
    public void SpecCanonicalizer_ShouldNormalizeAutomationUnitsAndIntervals(string left, string right)
    {
        var canonicalizer = new SpecCanonicalizer();

        canonicalizer.Canonicalize(left).Should().Be(canonicalizer.Canonicalize(right));
    }

    [Theory]
    [InlineData("真空流量为30NL/min", "真空流量为30SLM")]
    [InlineData("冷却水流量为0.06m³/h", "冷却水流量为1L/min")]
    [InlineData("点胶量为2cc/min", "点胶量为2mL/min")]
    [InlineData("气耗为100L/s", "气耗为6000L/min")]
    [InlineData("主轴转速为50r/s", "主轴转速为3000rpm")]
    public void SpecCanonicalizer_ShouldLoadDefaultExternalUnitRules(string left, string right)
    {
        var canonicalizer = new SpecCanonicalizer();

        canonicalizer.Canonicalize(left).Should().Be(canonicalizer.Canonicalize(right));
    }

    [Fact]
    public void SpecCanonicalizer_ShouldNormalizeKgfToForceWithinEngineeringTolerance()
    {
        var canonicalizer = new SpecCanonicalizer();

        canonicalizer.TryNormalizeToBaseUnit(12.236, "kgf", out var kgfValue, out var kgfDimension).Should().BeTrue();
        canonicalizer.TryNormalizeToBaseUnit(120, "N", out var nValue, out var nDimension).Should().BeTrue();

        kgfDimension.Should().Be(nDimension);
        (Math.Abs(kgfValue - nValue) / nValue).Should().BeLessThan(1e-3);
    }

    [Fact]
    public void SpecCanonicalizer_ShouldLoadBrandAndUnitRulesFromCustomExternalFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"smart-fill-rules-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
        {
          "brands": [
            {
              "canonical": "ExternalServo",
              "aliases": [ "外置伺服", "ExternalServo" ]
            }
          ],
          "brandDeviceWords": [ "测试驱动" ],
          "units": [
            {
              "dimension": "external_rate",
              "factor": 2,
              "tokens": [ "xpm", "外置单位" ]
            }
          ]
        }
        """);

        try
        {
            var canonicalizer = new SpecCanonicalizer(path);

            canonicalizer.Canonicalize("品牌要求 外置伺服测试驱动")
                .Should()
                .Be(canonicalizer.Canonicalize("品牌要求 ExternalServo测试驱动"));

            canonicalizer.TryNormalizeToBaseUnit(3, "xpm", out var baseValue, out var dimension)
                .Should()
                .BeTrue();
            baseValue.Should().Be(6);
            dimension.Should().Be("external_rate");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
