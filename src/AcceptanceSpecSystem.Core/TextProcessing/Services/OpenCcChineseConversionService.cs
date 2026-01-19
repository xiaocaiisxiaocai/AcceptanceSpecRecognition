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
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            ZhConverter.Initialize();
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

