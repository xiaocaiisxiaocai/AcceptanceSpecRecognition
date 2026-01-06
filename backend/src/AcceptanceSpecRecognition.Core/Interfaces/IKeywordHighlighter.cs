using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Core.Interfaces;

/// <summary>
/// 关键字高亮器接口
/// </summary>
public interface IKeywordHighlighter
{
    /// <summary>
    /// 高亮文本中的关键字
    /// </summary>
    HighlightedText Highlight(string text);
    
    /// <summary>
    /// 添加关键字
    /// </summary>
    void AddKeyword(string keyword, HighlightStyle style);
    
    /// <summary>
    /// 加载关键字库
    /// </summary>
    void LoadKeywordLibrary(string path);
    
    /// <summary>
    /// 获取关键字库
    /// </summary>
    KeywordLibrary GetLibrary();
}
