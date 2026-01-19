namespace AcceptanceSpecSystem.Core.TextProcessing.Interfaces;

public interface IOkNgConversionService
{
    string NormalizeOkNg(string text, string okStandard, string ngStandard);
}

