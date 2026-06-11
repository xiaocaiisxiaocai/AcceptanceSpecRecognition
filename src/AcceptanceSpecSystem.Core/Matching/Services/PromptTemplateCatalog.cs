using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Services;

public sealed record SystemPromptTemplateDefinition(
    PromptTemplateScene Scene,
    string Name,
    string DisplayName,
    string UsageDescription,
    string DefaultContent,
    string? LegacyDefaultContent,
    IReadOnlyList<string> RequiredVariables,
    IReadOnlyList<string> AvailableVariables,
    IReadOnlyList<string> RequiredJsonKeys,
    IReadOnlyList<string>? AdditionalLegacyContents = null);

public static class PromptTemplateCatalog
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

    private const string MatchingEquivalenceAdjudicationLegacyDefaultContent =
        """
        你是验收规格等价裁决助手。你的任务是判断源项与候选项在当前上下文下是否表达同一验收语义，不是润色文本，也不是补充事实。

        【源项】
        项目：{{sourceProject}}
        规格：{{sourceSpecification}}

        【候选项】
        项目：{{candidateProject}}
        规格：{{candidateSpecification}}

        【当前决策】{{currentDecision}}
        【得分明细】{{scoreDetailsJson}}
        【证据摘要】{{evidenceSummaryJson}}
        【冲突摘要】{{conflictSummaryJson}}

        【裁决规则】
        1. 只允许输出 equivalent、different、uncertain 三种 verdict
        2. reasonType 只允许 format_only、punctuation_only、equivalent_expression、symbol_equivalent、semantic_difference、symbol_conflict、uncertain
        3. verdict 为 equivalent 时，reasonType 只能是 format_only、punctuation_only、equivalent_expression、symbol_equivalent
        4. verdict 为 different 时，reasonType 只能是 semantic_difference、symbol_conflict
        5. verdict 为 uncertain 时，reasonType 必须是 uncertain
        6. 只有在差异仅限于换行、空格、普通标点、稳定同义表达或等价符号替换时，才可判定 equivalent
        7. 只要区间上下限、比较符、方向、极性、单位、容差、是否包含边界、约束对象任一不同，就优先判定 different
        8. 约等于/≈/不大于/≤ 这类表达，只有在语义和边界完全一致时才算等价；不能只看字面接近
        9. 无法确认符号差异是否影响语义时，必须返回 uncertain，禁止猜测
        10. confidence 取值 0~1

        仅返回严格 JSON：
        {"verdict":"uncertain","reasonType":"uncertain","reason":"...","confidence":0.0}
        """;

    // 等价裁决第一版（9条规则），作为 AdditionalLegacyContents 用于启动时自动升级
    private const string MatchingEquivalenceAdjudicationV1Content =
        "你是验收规格等价裁决助手。你的任务不是判断是否要编造内容，而是判断源项与候选项在当前上下文下是否表达同一含义。\n\n" +
        "【源项】\n项目：{{sourceProject}}\n规格：{{sourceSpecification}}\n\n" +
        "【候选项】\n项目：{{candidateProject}}\n规格：{{candidateSpecification}}\n\n" +
        "【当前决策】{{currentDecision}}\n【得分明细】{{scoreDetailsJson}}\n【证据摘要】{{evidenceSummaryJson}}\n【冲突摘要】{{conflictSummaryJson}}\n\n" +
        "【裁决规则】\n1. 只允许输出 equivalent、different、uncertain 三种 verdict\n" +
        "2. reasonType 只允许 format_only、punctuation_only、equivalent_expression、symbol_equivalent、semantic_difference、symbol_conflict、uncertain\n" +
        "3. verdict 为 equivalent 时，reasonType 只能是 format_only、punctuation_only、equivalent_expression、symbol_equivalent\n" +
        "4. verdict 为 different 时，reasonType 只能是 semantic_difference、symbol_conflict\n" +
        "5. verdict 为 uncertain 时，reasonType 必须是 uncertain\n" +
        "6. 当差异仅为换行、空格、普通标点、等价符号或等价表达时，应返回 equivalent\n" +
        "7. 当确有语义差异或关键符号导致含义不同时，应返回 different\n" +
        "8. 证据不足时必须返回 uncertain，禁止猜测\n" +
        "9. confidence 取值 0~1\n\n" +
        "仅返回严格 JSON：\n{\"verdict\":\"uncertain\",\"reasonType\":\"uncertain\",\"reason\":\"...\",\"confidence\":0.0}";

    // 等价裁决第二版（含单位/品牌/反义词知识，未含候选验收标准/备注），作为 AdditionalLegacyContents 用于启动时自动升级
    private const string MatchingEquivalenceAdjudicationV2Content =
        """
        你是验收规格等价裁决助手。你的任务是判断源项与候选项在当前上下文下是否表达同一验收语义，不是润色文本，也不是补充事实。

        【源项】
        项目：{{sourceProject}}
        规格：{{sourceSpecification}}

        【候选项】
        项目：{{candidateProject}}
        规格：{{candidateSpecification}}

        【当前决策】{{currentDecision}}
        【得分明细】{{scoreDetailsJson}}
        【证据摘要】{{evidenceSummaryJson}}
        【冲突摘要】{{conflictSummaryJson}}

        【裁决规则】
        1. 只允许输出 equivalent、different、uncertain 三种 verdict
        2. reasonType 只允许 format_only、punctuation_only、equivalent_expression、symbol_equivalent、semantic_difference、symbol_conflict、uncertain
        3. verdict 为 equivalent 时，reasonType 只能是 format_only、punctuation_only、equivalent_expression、symbol_equivalent
        4. verdict 为 different 时，reasonType 只能是 semantic_difference、symbol_conflict
        5. verdict 为 uncertain 时，reasonType 必须是 uncertain

        【等价判定条件】——满足以下任一情形可判 equivalent：
        - 差异仅为换行、空格、全半角、普通标点符号
        - 差异仅为同一语义的等价表达（如"不超过"与"≤"、"需要"与"应"）
        - 差异仅为等价符号替换（如 ≤ 与 <=、℃ 与 °C）
        - 差异仅为品牌中英文名称（如 Panasonic 与 松下、Omron 与 欧姆龙），且型号/参数完全一致
        - 差异仅为单位换算，且将两边数值换算到同一单位后完全相等（如"1秒"与"1000ms"，"5mm"与"0.5cm"，"7.5kW"与"7500W"）
          ⚠️ 单位换算必须先做数值计算验证，数值换算后不相等则判 different，不得仅凭单位相关就判 equivalent

        【必须判 different 的情形】——出现以下任一情形直接判 different，不得因"看起来相近"而放行：
        - 语义方向/极性相反：允许 ↔ 禁止，开启 ↔ 关闭，正转 ↔ 反转，上升 ↔ 下降，夹紧 ↔ 松开，升温 ↔ 降温，输入 ↔ 输出，上料 ↔ 下料，投板 ↔ 收板，左进右出 ↔ 右进左出，启动 ↔ 停止，继续 ↔ 停止，以及一切其他语义方向明确相反的表达
        - 数值不同（单位相同时）：如"不超过1秒"与"不超过2秒"
        - 单位换算后数值不等：如"1秒"与"500ms"（1秒=1000ms，500ms≠1000ms）
        - 区间上下限、容差或是否包含边界不同：如"8~12mm"与"10~12mm"、"±0.1"与"±0.2"
        - 比较符/边界方向不同：如"≥"与"≤"、"不超过"与"不低于"
        - 约束对象不同：如"气缸上升"与"气缸下降"、"左进"与"右进"
        - 极性词不同：如"应报警"与"不应报警"、"才允许"与"禁止"

        【uncertain 使用条件】
        - 仅当无法从上述规则判断时使用，必须在 reason 中说明具体疑点
        - 禁止用 uncertain 规避明确的语义差异

        【品牌等价补充】
        同一品牌的中英文对照（如 Panasonic=松下、Omron=欧姆龙、Mitsubishi=三菱、Keyence=基恩士、Yaskawa=安川、Schneider=施耐德、Siemens=西门子）不视为语义冲突，型号一致时可判 equivalent

        9. 约等于/≈/不大于/≤ 这类表达，只有在语义和边界完全一致时才算等价；不能只看字面接近
        10. confidence 取值 0~1

        仅返回严格 JSON：
        {"verdict":"uncertain","reasonType":"uncertain","reason":"...","confidence":0.0}
        """;

    private const string MatchingEquivalenceAdjudicationDefaultContent =
        """
        你是验收规格等价裁决助手。你的任务是判断源项与候选项在当前上下文下是否表达同一验收语义，不是润色文本，也不是补充事实。

        【源项】
        项目：{{sourceProject}}
        规格：{{sourceSpecification}}

        【候选项】
        项目：{{candidateProject}}
        规格：{{candidateSpecification}}
        验收标准：{{candidateAcceptance}}
        备注：{{candidateRemark}}

        【当前决策】{{currentDecision}}
        【得分明细】{{scoreDetailsJson}}
        【证据摘要】{{evidenceSummaryJson}}
        【冲突摘要】{{conflictSummaryJson}}

        【裁决规则】
        1. 只允许输出 equivalent、different、uncertain 三种 verdict
        2. reasonType 只允许 format_only、punctuation_only、equivalent_expression、symbol_equivalent、semantic_difference、symbol_conflict、uncertain
        3. verdict 为 equivalent 时，reasonType 只能是 format_only、punctuation_only、equivalent_expression、symbol_equivalent
        4. verdict 为 different 时，reasonType 只能是 semantic_difference、symbol_conflict
        5. verdict 为 uncertain 时，reasonType 必须是 uncertain
        6. 候选的"验收标准/备注"仅作为辅助上下文（如确认电压档位、确认是否同一验收对象），裁决对象始终是双方的项目+规格本身
        7. 约等于/≈/不大于/≤ 这类表达，只有在语义和边界完全一致时才算等价；不能只看字面接近
        8. confidence 取值 0~1

        【等价判定条件】——满足以下任一情形可判 equivalent：
        - 差异仅为换行、空格、全半角、普通标点符号
        - 差异仅为同一语义的等价表达（如"不超过"与"≤"、"需要"与"应"）
        - 差异仅为等价符号替换（如 ≤ 与 <=、℃ 与 °C）
        - 差异仅为品牌中英文名称（如 Panasonic 与 松下、Omron 与 欧姆龙），且型号/参数完全一致
        - 差异仅为单位换算，且将两边数值换算到同一单位后完全相等（如"1秒"与"1000ms"，"5mm"与"0.5cm"，"7.5kW"与"7500W"）
          ⚠️ 单位换算必须先做数值计算验证，数值换算后不相等则判 different，不得仅凭单位相关就判 equivalent

        【必须判 different 的情形】——出现以下任一情形直接判 different，不得因"看起来相近"而放行：
        - 语义方向/极性相反：允许 ↔ 禁止，开启 ↔ 关闭，正转 ↔ 反转，上升 ↔ 下降，夹紧 ↔ 松开，升温 ↔ 降温，输入 ↔ 输出，上料 ↔ 下料，投板 ↔ 收板，左进右出 ↔ 右进左出，启动 ↔ 停止，继续 ↔ 停止，以及一切其他语义方向明确相反的表达
        - 数值不同（单位相同时）：如"不超过1秒"与"不超过2秒"
        - 单位换算后数值不等：如"1秒"与"500ms"（1秒=1000ms，500ms≠1000ms）
        - 区间上下限、容差或是否包含边界不同：如"8~12mm"与"10~12mm"、"±0.1"与"±0.2"
        - 比较符/边界方向不同：如"≥"与"≤"、"不超过"与"不低于"
        - 约束对象不同：如"气缸上升"与"气缸下降"、"左进"与"右进"
        - 极性词不同：如"应报警"与"不应报警"、"才允许"与"禁止"
        - 型号/料号不同且不属于纯格式差异：如"SKF-6204"与"SKF-6205"；仅连字符/空格差异（如"SKF-6204-2Z"与"SKF 6204 2Z"）可判 equivalent

        【uncertain 使用条件】
        - 仅当无法从上述规则判断时使用，必须在 reason 中说明具体疑点
        - 禁止用 uncertain 规避明确的语义差异

        【品牌等价补充】
        同一品牌的中英文对照（如 Panasonic=松下、Omron=欧姆龙、Mitsubishi=三菱、Keyence=基恩士、Yaskawa=安川、Schneider=施耐德、Siemens=西门子）不视为语义冲突，型号一致时可判 equivalent

        仅返回严格 JSON：
        {"verdict":"uncertain","reasonType":"uncertain","reason":"...","confidence":0.0}
        """;

    private const string MatchingCandidateRerankDefaultContent =
        """
        你是验收规格候选重排助手。你的任务是在系统已经召回的候选集中选出"当前最应作为最佳候选的一条"，不是生成新候选，也不是补充未提供的事实。

        【源项】
        项目：{{sourceProject}}
        规格：{{sourceSpecification}}

        【当前本地 Top1 SpecId】{{currentTopCandidateSpecId}}

        【候选列表 JSON】
        {{candidatesJson}}

        【选择规则】
        1. 只能从候选列表中选择一个 selectedSpecId
        2. 若你认为当前本地 Top1 仍是最佳，可以直接返回它的 SpecId
        3. 重点比较项目主体、规格语义、边界条件、比较符、范围、单位、方向、对象和约束是否更一致
        4. 不得因为 Embedding 更高就机械保留某候选，也不得因为表述相似就忽略关键语义冲突
        5. 无法确认时，保守返回当前本地 Top1
        6. confidence 取值 0~1

        仅返回严格 JSON：
        {"selectedSpecId":1,"reason":"候选 1 更符合源项语义","confidence":0.0}
        """;

    private static readonly SystemPromptTemplateDefinition[] Definitions =
    [
        new(
            PromptTemplateScene.MatchingReview,
            "matching-review",
            "智能填充复核",
            "用于智能填充流程中的 AI 复核。",
            MatchingReviewDefaultContent,
            MatchingReviewLegacyDefaultContent,
            ["sourceProject", "sourceSpecification", "bestMatchProject", "bestMatchSpecification", "baseScore", "scoreDetailsJson", "workflowScene"],
            [
                "sourceProject",
                "sourceSpecification",
                "bestMatchProject",
                "bestMatchSpecification",
                "bestMatchAcceptance",
                "bestMatchRemark",
                "baseScore",
                "scoreDetailsJson",
                "currentDecision",
                "reviewTrigger",
                "evidenceSummaryJson",
                "conflictSummaryJson",
                "workflowScene"
            ],
            ["score", "reason", "commentary"]),
        new(
            PromptTemplateScene.ImportDuplicateReview,
            "import-duplicate-review",
            "导入重复复核",
            "用于导入重复检查流程中的 LLM 复核。",
            ImportDuplicateReviewDefaultContent,
            ImportDuplicateReviewLegacyDefaultContent,
            ["sourceProject", "sourceSpecification", "bestMatchProject", "bestMatchSpecification", "baseScore", "scoreDetailsJson", "workflowScene"],
            [
                "sourceProject",
                "sourceSpecification",
                "bestMatchProject",
                "bestMatchSpecification",
                "bestMatchAcceptance",
                "bestMatchRemark",
                "baseScore",
                "scoreDetailsJson",
                "currentDecision",
                "reviewTrigger",
                "evidenceSummaryJson",
                "conflictSummaryJson",
                "workflowScene"
            ],
            ["score", "reason", "commentary"]),
        new(
            PromptTemplateScene.MatchingEquivalenceAdjudication,
            "matching-equivalence-adjudication",
            "智能填充等价裁决",
            "用于智能填充当前最佳候选的等价表达裁决门禁。",
            MatchingEquivalenceAdjudicationDefaultContent,
            MatchingEquivalenceAdjudicationLegacyDefaultContent,
            ["sourceProject", "sourceSpecification", "candidateProject", "candidateSpecification", "currentDecision", "scoreDetailsJson", "evidenceSummaryJson", "conflictSummaryJson"],
            ["sourceProject", "sourceSpecification", "candidateProject", "candidateSpecification", "candidateAcceptance", "candidateRemark", "currentDecision", "scoreDetailsJson", "evidenceSummaryJson", "conflictSummaryJson"],
            ["verdict", "reasonType", "reason", "confidence"],
            AdditionalLegacyContents:
            [
                MatchingEquivalenceAdjudicationV1Content,
                MatchingEquivalenceAdjudicationV2Content
            ]),
        new(
            PromptTemplateScene.MatchingCandidateRerank,
            "matching-candidate-rerank",
            "智能填充候选重排",
            "用于智能填充 TopK 候选中的 AI 改选。",
            MatchingCandidateRerankDefaultContent,
            null,
            ["sourceProject", "sourceSpecification", "currentTopCandidateSpecId", "candidatesJson"],
            ["sourceProject", "sourceSpecification", "currentTopCandidateSpecId", "candidatesJson"],
            ["selectedSpecId", "reason", "confidence"])
    ];

    public static IReadOnlyList<SystemPromptTemplateDefinition> GetSystemTemplates() => Definitions;

    public static SystemPromptTemplateDefinition GetByScene(PromptTemplateScene scene)
    {
        return Definitions.First(definition => definition.Scene == scene);
    }

    public static bool TryGetByName(string? name, out SystemPromptTemplateDefinition definition)
    {
        definition = Definitions.FirstOrDefault(item =>
            string.Equals(item.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase))!;
        return definition != null;
    }
}
