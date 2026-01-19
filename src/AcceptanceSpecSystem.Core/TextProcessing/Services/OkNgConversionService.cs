using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;

namespace AcceptanceSpecSystem.Core.TextProcessing.Services;

public class OkNgConversionService : IOkNgConversionService
{
    // 仅做“格式统一”，不引入额外语义词表，避免误替换
    private static readonly Regex OkRegex = new(@"\bO\s*K\b|\bOK\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NgRegex = new(@"\bN\s*G\b|\bNG\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string NormalizeOkNg(string text, string okStandard, string ngStandard)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        okStandard = string.IsNullOrWhiteSpace(okStandard) ? "OK" : okStandard.Trim();
        ngStandard = string.IsNullOrWhiteSpace(ngStandard) ? "NG" : ngStandard.Trim();

        var output = OkRegex.Replace(text, okStandard);
        output = NgRegex.Replace(output, ngStandard);
        return output;
    }
}

