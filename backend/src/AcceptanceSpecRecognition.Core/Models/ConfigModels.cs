namespace AcceptanceSpecRecognition.Core.Models;

/// <summary>
/// 系统配置
/// </summary>
public class SystemConfig
{
    public string Version { get; set; } = "1.0";
    public EmbeddingConfig Embedding { get; set; } = new();
    public LLMConfig LLM { get; set; } = new();
    public MatchingConfig Matching { get; set; } = new();
    public HighlightingConfig Highlighting { get; set; } = new();
    public PreprocessingConfig Preprocessing { get; set; } = new();
    public BatchConfig Batch { get; set; } = new();
    public CacheConfig Cache { get; set; } = new();
    public FastPathConfig FastPath { get; set; } = new();
    public HybridSearchConfig HybridSearch { get; set; } = new();
    public FeedbackConfig Feedback { get; set; } = new();
    public PerformanceConfig Performance { get; set; } = new();
    public PromptConfig Prompts { get; set; } = new();
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Embedding配置
/// </summary>
public class EmbeddingConfig
{
    public string Model { get; set; } = "text-embedding-3-small";
    public string Provider { get; set; } = "openai";
    public int Dimension { get; set; } = 1536;
    public int Dimensions { get; set; } = 1536;
    public int BatchSize { get; set; } = 100;
    public string PrimaryLanguage { get; set; } = "zh";
    public bool MixedLanguageSupport { get; set; } = true;
    public string? FallbackModel { get; set; }
    public string? FallbackProvider { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
}

/// <summary>
/// LLM配置
/// </summary>
public class LLMConfig
{
    public string Model { get; set; } = "gpt-4o-mini";
    public string Provider { get; set; } = "openai";
    public bool EnableConflictCheck { get; set; } = true;
    public bool EnableScoreAdjustment { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
    public int RetryCount { get; set; } = 3;
    public int MaxTokens { get; set; } = 2000;
    public float Temperature { get; set; } = 0.1f;
    public bool EnableFallback { get; set; } = true;
    public float FallbackConfidencePenalty { get; set; } = 0.1f;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
}

/// <summary>
/// 匹配配置
/// </summary>
public class MatchingConfig
{
    /// <summary>
    /// 匹配模式：true=LLM+Embedding混合模式，false=纯Embedding模式
    /// </summary>
    public bool EnableLLM { get; set; } = true;

    /// <summary>
    /// 匹配成功阈值：相似度 >= 此值认为匹配成功，否则标记为低置信度需要注意
    /// </summary>
    public float MatchSuccessThreshold { get; set; } = 0.80f;

    /// <summary>
    /// Embedding权重（用于LLM+Embedding混合模式）
    /// </summary>
    public float EmbeddingWeight { get; set; } = 0.6f;

    /// <summary>
    /// LLM权重（用于LLM+Embedding混合模式）
    /// </summary>
    public float LLMWeight { get; set; } = 0.4f;
}

/// <summary>
/// 高亮配置
/// </summary>
public class HighlightingConfig
{
    public HighlightStyle DefaultStyle { get; set; } = new();
    public Dictionary<string, HighlightStyle> Styles { get; set; } = new();
}

/// <summary>
/// 高亮样式
/// </summary>
public class HighlightStyle
{
    public string Color { get; set; } = "#000000";
    public string BackgroundColor { get; set; } = "#ffff00";
    public string? FontWeight { get; set; } = "bold";
}

/// <summary>
/// 配置修改记录
/// </summary>
public class ConfigChange
{
    public string Key { get; set; } = string.Empty;
    public object? OldValue { get; set; }
    public object? NewValue { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
}


/// <summary>
/// 预处理配置
/// </summary>
public class PreprocessingConfig
{
    public bool EnableTypoCorrection { get; set; } = true;
    public bool EnableUnitNormalization { get; set; } = true;
    public bool EnableSymbolNormalization { get; set; } = true;

    /// <summary>
    /// 启用繁简体转换（繁体→简体，提升匹配率）
    /// </summary>
    public bool EnableChineseSimplification { get; set; } = true;
}

/// <summary>
/// 批量处理配置
/// </summary>
public class BatchConfig
{
    /// <summary>
    /// 最大并发数
    /// </summary>
    public int MaxConcurrency { get; set; } = 5;

    /// <summary>
    /// 分块大小
    /// </summary>
    public int ChunkSize { get; set; } = 50;

    /// <summary>
    /// 任务保留时间（分钟），超时后自动清理
    /// </summary>
    public int TaskRetentionMinutes { get; set; } = 30;

    /// <summary>
    /// 最大批量查询数量
    /// </summary>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>
    /// 单个输入字段最大长度
    /// </summary>
    public int MaxTextLength { get; set; } = 2000;

    /// <summary>
    /// 审计日志最大条目数
    /// </summary>
    public int MaxAuditEntries { get; set; } = 10000;

    /// <summary>
    /// API 重试次数
    /// </summary>
    public int ApiMaxRetries { get; set; } = 3;

    /// <summary>
    /// API 重试延迟基数（毫秒）
    /// </summary>
    public int ApiRetryBaseDelayMs { get; set; } = 1000;
}

/// <summary>
/// 缓存配置 (P0-2)
/// </summary>
public class CacheConfig
{
    /// <summary>
    /// 启用向量缓存
    /// </summary>
    public bool EnableVectorCache { get; set; } = true;

    /// <summary>
    /// 向量缓存TTL（分钟）
    /// </summary>
    public int VectorCacheTtlMinutes { get; set; } = 1440; // 24小时

    /// <summary>
    /// 启用LLM结果缓存
    /// </summary>
    public bool EnableLLMCache { get; set; } = true;

    /// <summary>
    /// LLM缓存TTL（分钟）
    /// </summary>
    public int LLMCacheTtlMinutes { get; set; } = 10080; // 7天

    /// <summary>
    /// 启用完整匹配结果缓存
    /// </summary>
    public bool EnableResultCache { get; set; } = true;

    /// <summary>
    /// 结果缓存TTL（分钟）
    /// </summary>
    public int ResultCacheTtlMinutes { get; set; } = 5;

    /// <summary>
    /// 内存缓存最大条目数
    /// </summary>
    public int MaxMemoryCacheEntries { get; set; } = 10000;
}

/// <summary>
/// 快速路径配置 (P0-4)
/// </summary>
public class FastPathConfig
{
    /// <summary>
    /// 启用精确匹配快速路径
    /// </summary>
    public bool EnableExactMatch { get; set; } = true;

    /// <summary>
    /// 启用简单查询检测
    /// </summary>
    public bool EnableSimpleQueryDetection { get; set; } = true;

    /// <summary>
    /// 简单查询最大长度
    /// </summary>
    public int SimpleQueryMaxLength { get; set; } = 20;

    /// <summary>
    /// 简单查询正则模式
    /// </summary>
    public string SimpleQueryPattern { get; set; } = @"^[A-Z0-9\-]+$";

    /// <summary>
    /// 高置信度缓存命中阈值
    /// </summary>
    public float HighConfidenceCacheThreshold { get; set; } = 0.95f;
}

/// <summary>
/// 混合检索配置 (P2-9)
/// </summary>
public class HybridSearchConfig
{
    /// <summary>
    /// 启用混合检索（BM25 + Embedding）
    /// </summary>
    public bool EnableHybridSearch { get; set; } = false;

    /// <summary>
    /// BM25权重
    /// </summary>
    public float BM25Weight { get; set; } = 0.3f;

    /// <summary>
    /// Embedding权重
    /// </summary>
    public float EmbeddingWeight { get; set; } = 0.7f;

    /// <summary>
    /// Reciprocal Rank Fusion 参数 K
    /// </summary>
    public int FusionK { get; set; } = 60;

    /// <summary>
    /// BM25参数 k1
    /// </summary>
    public float BM25K1 { get; set; } = 1.5f;

    /// <summary>
    /// BM25参数 b
    /// </summary>
    public float BM25B { get; set; } = 0.75f;
}

/// <summary>
/// 反馈配置 (P2-10)
/// </summary>
public class FeedbackConfig
{
    /// <summary>
    /// 启用用户反馈收集
    /// </summary>
    public bool EnableFeedback { get; set; } = true;

    /// <summary>
    /// 低质量记录接受率阈值
    /// </summary>
    public float QualityThreshold { get; set; } = 0.3f;

    /// <summary>
    /// 低质量记录降权系数
    /// </summary>
    public float LowQualityPenalty { get; set; } = 0.5f;

    /// <summary>
    /// 自动分析反馈的最小样本数
    /// </summary>
    public int MinSampleCountForAnalysis { get; set; } = 10;
}

/// <summary>
/// 性能监控配置 (双模式对比)
/// </summary>
public class PerformanceConfig
{
    /// <summary>
    /// 启用性能跟踪
    /// </summary>
    public bool EnablePerformanceTracking { get; set; } = true;

    /// <summary>
    /// 启用双模式对比测试
    /// </summary>
    public bool EnableCompareMode { get; set; } = false;

    /// <summary>
    /// 对比测试采样率 (0.0-1.0)
    /// </summary>
    public float CompareSamplingRate { get; set; } = 0.1f;

    /// <summary>
    /// 保存详细性能日志
    /// </summary>
    public bool SaveDetailedMetrics { get; set; } = true;
}

/// <summary>
/// LLM 提示词配置
/// </summary>
public class PromptConfig
{
    /// <summary>
    /// 统一分析提示词（合并冲突检测、语义等价、匹配分析）
    /// </summary>
    public string UnifiedAnalysisPrompt { get; set; } = GetDefaultUnifiedAnalysisPrompt();

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 获取默认统一分析提示词
    /// </summary>
    public static string GetDefaultUnifiedAnalysisPrompt()
    {
        return @"你是工控领域验收规范专家，精通中文工业术语、电气设备规格、品牌名称（中英文对照）和技术规格匹配。

## 任务
分析查询规格和候选规格，一次性完成：冲突检测、语义等价分析、匹配置信度评估。

## 输入
- 查询规格: {query}
- 候选规格: {candidate}

## 一、品牌名称等价映射（重要！）
识别跨语言的品牌名，以下品牌名应视为等价：
- Omron = 欧姆龙（日本自动化品牌）
- Siemens = 西门子（德国工业巨头）
- Schneider = 施耐德（法国电气品牌）
- Mitsubishi = 三菱（日本综合品牌）
- Panasonic = 松下（日本电子品牌）
- Rockwell = 罗克韦尔（美国自动化品牌）
- Honeywell = 霍尼韦尔（美国自动化品牌）
- Phoenix = 菲尼克斯（德国连接器品牌）
- WAGO = 万可（德国端子品牌）
- Beckhoff = 倍福（德国自动化品牌）
- Delta = 台达（台湾电子品牌）
- LS = 乐星（韩国电气品牌）
- Keyence = 基恩士（日本传感器品牌）
- Festo = 费斯托（德国气动品牌）
- ABB = ABB（瑞典-瑞士电气品牌）
- SMC = SMC（日本气动品牌）

## 二、技术术语等价映射
- PLC = 可编程逻辑控制器
- HMI = 人机界面 = 触摸屏
- VFD = 变频器 = Inverter = 逆变器
- Servo = 伺服 = 伺服电机
- I/O = 输入输出 = IO模块
- DC = 直流, AC = 交流
- 三相 = 3P, 单相 = 1P
- 以太网 = Ethernet

## 三、表述差异（不算冲突）
- 中英文表述（如 DC=直流, Ethernet=以太网）
- 繁体/简体字（如 網路=网络, 軟體=软件, 訊號=信号）
- 两岸用语差异（如 通訊=通信, 伺服器=服务器, 數據=数据）
- 同品牌型号升级（如 FX5U 是 FX3U 升级版）

## 四、冲突检测标准（互斥属性）
❌ **判定为冲突**:
- DC vs AC（直流 vs 交流）
- 单相 vs 三相
- NPN vs PNP
- 电压等级差异大（24V vs 220V）
- 互斥协议（Modbus vs Profinet）

✅ **不算冲突**:
- 属性只在一方出现
- 功能向下兼容
- 表述形式不同但语义相同

## 五、分析步骤
1. **提取关键属性**: 品牌、型号、电气参数、接口、协议
2. **品牌等价检查**: 检查是否存在中英文品牌名等价
3. **术语等价检查**: 检查是否存在技术术语等价
4. **冲突检测**: 检查是否存在互斥属性
5. **置信度评估**: 综合以上分析给出匹配置信度

## 六、输出格式（严格JSON，无其他文字）
```json
{
  ""hasConflict"": false,
  ""conflictType"": ""none"",
  ""conflictDescription"": ""无冲突或冲突描述"",
  ""isEquivalent"": true,
  ""equivalenceMappings"": [
    {""queryTerm"": ""Omron"", ""candidateTerm"": ""欧姆龙"", ""type"": ""brand""}
  ],
  ""scoreAdjustmentFactor"": 1.2,
  ""confidence"": 0.95,
  ""reasoning"": ""详细推理说明""
}
```

字段说明：
- hasConflict: 是否存在互斥冲突
- conflictType: none/electrical/protocol/mechanical
- conflictDescription: 冲突描述或无冲突说明
- isEquivalent: 是否发现语义等价关系（品牌名、术语等）
- equivalenceMappings: 等价映射列表
- scoreAdjustmentFactor: 分数调整系数（1.0=不变，1.1-1.3=提升）
  - 品牌名完全等价：1.2-1.3
  - 术语同义：1.1-1.2
  - 有冲突：0.3-0.5
- confidence: 匹配置信度（0-1）
- reasoning: 推理说明

## Few-shot 示例

### 示例1: 品牌名等价
**查询**: Omron CP1H PLC DC24V
**候选**: 欧姆龙 CP1H 可编程控制器 直流24V
**输出**:
```json
{
  ""hasConflict"": false,
  ""conflictType"": ""none"",
  ""conflictDescription"": ""无冲突"",
  ""isEquivalent"": true,
  ""equivalenceMappings"": [
    {""queryTerm"": ""Omron"", ""candidateTerm"": ""欧姆龙"", ""type"": ""brand""},
    {""queryTerm"": ""PLC"", ""candidateTerm"": ""可编程控制器"", ""type"": ""synonym""},
    {""queryTerm"": ""DC24V"", ""candidateTerm"": ""直流24V"", ""type"": ""synonym""}
  ],
  ""scoreAdjustmentFactor"": 1.25,
  ""confidence"": 0.98,
  ""reasoning"": ""Omron是欧姆龙的英文品牌名，CP1H型号完全一致，PLC=可编程控制器，DC=直流，所有属性语义等价""
}
```

### 示例2: 电气冲突
**查询**: 西门子 S7-1500 PLC DC24V
**候选**: 西门子 S7-1200 PLC AC220V
**输出**:
```json
{
  ""hasConflict"": true,
  ""conflictType"": ""electrical"",
  ""conflictDescription"": ""电源类型不兼容: DC24V vs AC220V"",
  ""isEquivalent"": false,
  ""equivalenceMappings"": [],
  ""scoreAdjustmentFactor"": 0.4,
  ""confidence"": 0.3,
  ""reasoning"": ""品牌一致但电源类型冲突，DC直流与AC交流互斥，无法兼容""
}
```

### 示例3: 繁简体差异
**查询**: 網路通訊模組
**候选**: 网络通信模块
**输出**:
```json
{
  ""hasConflict"": false,
  ""conflictType"": ""none"",
  ""conflictDescription"": ""无冲突"",
  ""isEquivalent"": true,
  ""equivalenceMappings"": [
    {""queryTerm"": ""網路"", ""candidateTerm"": ""网络"", ""type"": ""traditional_simplified""},
    {""queryTerm"": ""通訊"", ""candidateTerm"": ""通信"", ""type"": ""cross_strait""},
    {""queryTerm"": ""模組"", ""candidateTerm"": ""模块"", ""type"": ""traditional_simplified""}
  ],
  ""scoreAdjustmentFactor"": 1.2,
  ""confidence"": 0.99,
  ""reasoning"": ""繁简体及两岸用语差异，语义完全相同""
}
```

## 现在开始分析
请对当前查询和候选进行分析，输出JSON结果：";
    }
}
