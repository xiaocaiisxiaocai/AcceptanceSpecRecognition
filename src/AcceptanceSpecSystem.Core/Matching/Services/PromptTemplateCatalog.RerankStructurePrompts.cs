using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Services;

public static partial class PromptTemplateCatalog
{
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

    private const string SmartConfigStructureRecognitionDefaultContent =
        """
        你是验收规格文档结构识别助手。系统已先做表头扫描、客户模板匹配和列名规则匹配，你只允许基于给定表格摘要裁决结构，不得补充未出现的列或行。

        【业务场景】{{workflowScene}}

        【文档表格摘要 JSON】
        {{documentTablesJson}}

        【规则识别结果 JSON】
        {{ruleCandidatesJson}}

        【裁决要求】
        1. tables 只能引用输入中存在的 tableIndex 和列索引
        2. 规格列 specificationColumnIndex 是必需列；无法确定时 decision 必须为 needConfirm
        3. 项目列、验收标准列、备注列可以为 null，但不得臆造
        4. headerRowIndex、headerRowCount、dataStartRowIndex、dataEndRowIndex 必须与表格实际行范围一致
        5. confidence 取值 0~1；只有结构完整且冲突很少时才可高于 0.85
        6. decision 只允许 autoApply、needConfirm、reject
        7. reason 用一句话说明关键依据或需要人工确认的原因

        仅返回严格 JSON：
        {"tables":[{"tableIndex":0,"tableName":"表1","headerRowIndex":0,"headerRowCount":1,"dataStartRowIndex":1,"dataEndRowIndex":10,"projectColumnIndex":0,"specificationColumnIndex":1,"acceptanceColumnIndex":2,"remarkColumnIndex":3,"isSpecificationOnly":false,"confidence":0.0,"decision":"needConfirm","reason":"..."}],"confidence":0.0,"decision":"needConfirm","reason":"..."}
        """;
}