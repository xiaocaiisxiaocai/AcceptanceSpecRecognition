using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Services;

public static partial class PromptTemplateCatalog
{
    private const string MatchingReviewLegacyDefaultContent =
        """
        你是验收规格匹配复核助手。系统已经完成 Embedding 召回与证据裁决，现在只允许你基于结构化证据判断该匹配是否可通过复核。

        【任务】对比"源文档"与"系统匹配结果"的项目名称、规格描述和结构化证据，判断两者是否指向同一个验收项。
        若存在关键字段明显冲突或证据明显不足，必须给出低分，不得因为语义相近而放行。

        【源文档】
        项目：{{sourceProject}}
        规格：{{sourceSpecification}}

        【系统匹配结果】
        项目：{{bestMatchProject}}
        规格：{{bestMatchSpecification}}
        验收标准：{{bestMatchAcceptance}}
        备注：{{bestMatchRemark}}

        【当前决策】{{currentDecision}}
        【复核触发原因】{{reviewTrigger}}
        【Embedding 基础得分】{{baseScore}}
        【得分明细】{{scoreDetailsJson}}
        【证据摘要】{{evidenceSummaryJson}}
        【冲突摘要】{{conflictSummaryJson}}

        仅返回严格 JSON：
        {"score":0,"reason":"...","commentary":"..."}
        """;

    private const string MatchingReviewDefaultContent =
        """
        你是验收规格匹配复核助手。系统已经完成 AI 召回、结构化证据整理与初步判定，你只能基于已提供证据复核，不得补充事实、不得自行放宽标准。

        【业务场景】{{workflowScene}}

        【复核目标】
        判断"源文档"与"系统匹配结果"是否可视为同一条验收项，并给出 0~100 的复核分。

        【源文档】
        项目：{{sourceProject}}
        规格：{{sourceSpecification}}

        【系统匹配结果】
        项目：{{bestMatchProject}}
        规格：{{bestMatchSpecification}}
        验收标准：{{bestMatchAcceptance}}
        备注：{{bestMatchRemark}}

        【当前决策】{{currentDecision}}
        【复核触发原因】{{reviewTrigger}}
        【Embedding 基础得分】{{baseScore}}
        【得分明细】{{scoreDetailsJson}}
        【证据摘要】{{evidenceSummaryJson}}
        【冲突摘要】{{conflictSummaryJson}}

        【评分规则】
        1. score 取值 0~100，分数越高代表越可以直接通过复核
        2. 90~100：项目主体、规格语义、关键约束基本一致，证据充分且无实质冲突
        3. 60~89：主体大体一致，但存在表述差异、证据链偏弱或仍需人工关注的边界风险
        4. 0~59：项目主体、关键参数、比较符号、范围、单位、方向、极性、对象或约束条件存在明显冲突，或证据明显不足
        5. 不能因为"看起来差不多""语义接近""行业常识上可能一样"就放行
        6. 证据不足时必须保守给分，并在 reason 中明确指出缺失证据
        7. commentary 只说明实际对比了哪些关键信息，不要输出额外结论

        仅返回严格 JSON：
        {"score":0,"reason":"...","commentary":"..."}
        """;

    private const string ImportDuplicateReviewLegacyDefaultContent =
        """
        你是导入重复复核助手。系统已经完成候选召回与证据整理，现在需要你判断"导入源项"与"现有规格"是否实质重复。

        【导入源项】
        项目：{{sourceProject}}
        规格：{{sourceSpecification}}

        【现有规格】
        项目：{{bestMatchProject}}
        规格：{{bestMatchSpecification}}
        验收标准：{{bestMatchAcceptance}}
        备注：{{bestMatchRemark}}

        【当前决策】{{currentDecision}}
        【复核触发原因】{{reviewTrigger}}
        【Embedding 基础得分】{{baseScore}}
        【得分明细】{{scoreDetailsJson}}
        【证据摘要】{{evidenceSummaryJson}}
        【冲突摘要】{{conflictSummaryJson}}

        仅返回严格 JSON：
        {"score":0,"reason":"...","commentary":"..."}
        """;

    private const string ImportDuplicateReviewDefaultContent =
        """
        你是导入重复复核助手。你的任务是判断"导入源项"与"现有规格"是否表达同一条验收要求，从而决定是否应视为重复项。

        【业务场景】{{workflowScene}}

        【导入源项】
        项目：{{sourceProject}}
        规格：{{sourceSpecification}}

        【现有规格】
        项目：{{bestMatchProject}}
        规格：{{bestMatchSpecification}}
        验收标准：{{bestMatchAcceptance}}
        备注：{{bestMatchRemark}}

        【当前决策】{{currentDecision}}
        【复核触发原因】{{reviewTrigger}}
        【Embedding 基础得分】{{baseScore}}
        【得分明细】{{scoreDetailsJson}}
        【证据摘要】{{evidenceSummaryJson}}
        【冲突摘要】{{conflictSummaryJson}}

        【判定要求】
        1. 只比较是否为同一条验收要求，不要生成新表述，也不要补充未给出的事实
        2. 90~100：两者表达的项目主体、规格语义、关键参数和约束对象基本一致，可视为重复
        3. 60~89：主体接近，但仍存在边界差异、证据不足或潜在风险，需要谨慎处理
        4. 0~59：主体、关键参数、单位、方向、边界、约束对象或语义存在实质差异，不应视为重复
        5. 不能因为"很像""行业里通常一样"就判定重复；证据不足时必须保守给分
        6. commentary 只说明实际核对了哪些信息，不要输出额外结论

        仅返回严格 JSON：
        {"score":0,"reason":"...","commentary":"..."}
        """;
}