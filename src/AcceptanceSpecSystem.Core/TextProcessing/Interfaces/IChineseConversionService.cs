using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Core.TextProcessing.Interfaces;

public interface IChineseConversionService
{
    string Convert(string text, ChineseConversionMode mode);
}

