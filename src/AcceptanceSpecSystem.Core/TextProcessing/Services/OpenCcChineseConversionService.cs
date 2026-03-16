using System.Reflection;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using OpenCCNET;

namespace AcceptanceSpecSystem.Core.TextProcessing.Services;

public class OpenCcChineseConversionService : IChineseConversionService
{
    private static int _initialized;

    public OpenCcChineseConversionService()
    {
        // ZhConverter 需要初始化字典（静态）；避免重复初始化
        // 基于程序集位置定位资源目录，防止工作目录不在 bin 时找不到资源
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            var dictDir = Path.Combine(assemblyDir, "Dictionary");
            var jiebaDir = Path.Combine(assemblyDir, "JiebaResource");
            ZhConverter.Initialize(dictDir, jiebaDir);
        }
    }

    public string Convert(string text, ChineseConversionMode mode)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return mode switch
        {
            ChineseConversionMode.None => text,
            ChineseConversionMode.HansToTW => ZhConverter.HansToTW(text),
            ChineseConversionMode.TWToHans => ZhConverter.TWToHans(text),
            _ => text
        };
    }
}

