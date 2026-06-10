using AcceptanceSpecSystem.Core.TextProcessing.Models;

namespace AcceptanceSpecSystem.Core.TextProcessing.Interfaces;

public interface IChineseConversionService
{
    string Convert(string text, ChineseConversionMode mode);
}

