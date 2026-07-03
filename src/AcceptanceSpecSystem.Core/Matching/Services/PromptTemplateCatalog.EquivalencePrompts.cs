using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Services;

public static partial class PromptTemplateCatalog
{
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

        【整句同义复述判定】——长文本改写场景的核心准则：
        当源与候选是同一要求的不同写法时，应判 equivalent，重点比对"要求了什么事项/对象/数值边界"，而非"用什么措辞"。下列改写均属等价：
        - 术语同义替换：条码扫描装置=条码识别系统、减速马达=齿轮减速电机、摄像头=视频监控装置、感应器=传感器
        - 句式变换：主动↔被动、肯定↔双重否定、口语↔书面（"机台需配备X"="设备应装有X"）
        - 子句/并列项语序调换："A且B"="B，此外A"；品牌罗列"甲/乙/丙"与"丙、乙、甲"
        - 中英文混排或附带译文：同一要求的中英表述并存不构成差异
        ⚠️ 仅当未增删任何实质要求、未改变任何数值/方向/约束对象时才算等价；若一侧多出或缺少实质约束，仍判 different

        【判定示例】（仅供参照风格，按此输出 JSON，不要照抄示例内容、也不要把示例当作待判对象）
        1) 源：电机 | 功率 7.5kW
           候选：电机 | 功率 7500W
           {"verdict":"equivalent","reasonType":"equivalent_expression","reason":"7.5kW 换算为 7500W，数值与量纲完全一致","confidence":0.97}
        2) 源：伺服电机 | 品牌 Panasonic 型号 MSMF012L1U2M
           候选：伺服电机 | 品牌 松下 型号 MSMF012L1U2M
           {"verdict":"equivalent","reasonType":"equivalent_expression","reason":"Panasonic 与 松下 为同一品牌中英文名，型号一致","confidence":0.95}
        3) 源：输出电压：DC 24V（±5%）
           候选：输出电压: DC24V (±5%)
           {"verdict":"equivalent","reasonType":"format_only","reason":"仅全半角/空格/冒号格式差异，数值与容差一致","confidence":0.96}
        4) 源：噪音 ≤ 60dB
           候选：噪音 ≥ 60dB
           {"verdict":"different","reasonType":"symbol_conflict","reason":"比较符方向相反（≤ vs ≥），约束含义相反","confidence":0.98}
        5) 源：气缸动作 | 到位后气缸上升
           候选：气缸动作 | 到位后气缸下降
           {"verdict":"different","reasonType":"semantic_difference","reason":"动作方向相反（上升 vs 下降）","confidence":0.97}
        6) 源：循环时间 ≤ 1秒
           候选：循环时间 ≤ 2秒
           {"verdict":"different","reasonType":"semantic_difference","reason":"同单位下数值不同（1秒 vs 2秒），指标要求不同","confidence":0.97}
        7) 源：轴承 SKF-6204
           候选：轴承 SKF-6205
           {"verdict":"different","reasonType":"semantic_difference","reason":"型号尾数不同（6204 vs 6205），为不同物料","confidence":0.96}
        8) 源：轴承 SKF-6204-2Z
           候选：轴承 SKF 6204 2Z
           {"verdict":"equivalent","reasonType":"format_only","reason":"仅连字符/空格分隔差异，型号 6204-2Z 一致","confidence":0.95}
        9) 源：视觉检测 | 检测精度 高
           候选：视觉检测 | 检测精度 ±0.02mm
           {"verdict":"uncertain","reasonType":"uncertain","reason":"源为定性描述'高'，无法与定量 ±0.02mm 判定等价，需人工确认","confidence":0.3}
        10) 源：智能化 | 机台需配备条码扫描装置（含固定式和手持式两种类型），自动调用对应工艺参数
            候选：智能化 | 设备应装有两类条码识别系统（固定式扫码头与手持扫描器），自动加载对应制程参数
            {"verdict":"equivalent","reasonType":"equivalent_expression","reason":"整句同义复述：条码扫描装置=条码识别系统、固定式+手持两类、自动调用=自动加载，要求事项与对象一致，仅措辞不同","confidence":0.93}
        11) 源：标准件 | 减速马达：东方/利茗/精石
            候选：标准件 | 齿轮减速电机：东方、利茗、精石（顺序可不同）
            {"verdict":"equivalent","reasonType":"equivalent_expression","reason":"减速马达=齿轮减速电机为同义术语，三个品牌集合一致，仅罗列顺序与分隔符不同","confidence":0.94}
        12) 源：安全 | 移动式台车需有防撞设施，运动机构危险处需有防护盖板并开盖联动停止
            候选：安全 | 移动式物料承载平台应配备碰撞缓冲装置，运动部件等高风险区域需加装防护外罩并实现开盖切断动力的电气联锁
            {"verdict":"equivalent","reasonType":"equivalent_expression","reason":"句式与详略不同但要求事项完全一致：防撞、危险处防护盖板、开盖联动停机三点均覆盖，无增删实质约束","confidence":0.92}
        13) 源：维护 | 摄像头监控可保存30天以上（含中英对照）
            候选：维护 | 视频监控装置录像保存不少于一个月
            {"verdict":"equivalent","reasonType":"equivalent_expression","reason":"摄像头监控=视频监控装置，30天以上与不少于一个月数值边界一致；中英对照不构成差异","confidence":0.92}

        仅返回严格 JSON：
        {"verdict":"uncertain","reasonType":"uncertain","reason":"...","confidence":0.0}
        """;

    // 等价裁决第四版（含 9 条 few-shot，但缺"整句同义复述"长文本改写引导），
    // 作为 AdditionalLegacyContents 用于启动时自动升级到含整句同义复述的新默认。
    private const string MatchingEquivalenceAdjudicationV4Content =
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

        【判定示例】（仅供参照风格，按此输出 JSON，不要照抄示例内容、也不要把示例当作待判对象）
        1) 源：电机 | 功率 7.5kW
           候选：电机 | 功率 7500W
           {"verdict":"equivalent","reasonType":"equivalent_expression","reason":"7.5kW 换算为 7500W，数值与量纲完全一致","confidence":0.97}
        2) 源：伺服电机 | 品牌 Panasonic 型号 MSMF012L1U2M
           候选：伺服电机 | 品牌 松下 型号 MSMF012L1U2M
           {"verdict":"equivalent","reasonType":"equivalent_expression","reason":"Panasonic 与 松下 为同一品牌中英文名，型号一致","confidence":0.95}
        3) 源：输出电压：DC 24V（±5%）
           候选：输出电压: DC24V (±5%)
           {"verdict":"equivalent","reasonType":"format_only","reason":"仅全半角/空格/冒号格式差异，数值与容差一致","confidence":0.96}
        4) 源：噪音 ≤ 60dB
           候选：噪音 ≥ 60dB
           {"verdict":"different","reasonType":"symbol_conflict","reason":"比较符方向相反（≤ vs ≥），约束含义相反","confidence":0.98}
        5) 源：气缸动作 | 到位后气缸上升
           候选：气缸动作 | 到位后气缸下降
           {"verdict":"different","reasonType":"semantic_difference","reason":"动作方向相反（上升 vs 下降）","confidence":0.97}
        6) 源：循环时间 ≤ 1秒
           候选：循环时间 ≤ 2秒
           {"verdict":"different","reasonType":"semantic_difference","reason":"同单位下数值不同（1秒 vs 2秒），指标要求不同","confidence":0.97}
        7) 源：轴承 SKF-6204
           候选：轴承 SKF-6205
           {"verdict":"different","reasonType":"semantic_difference","reason":"型号尾数不同（6204 vs 6205），为不同物料","confidence":0.96}
        8) 源：轴承 SKF-6204-2Z
           候选：轴承 SKF 6204 2Z
           {"verdict":"equivalent","reasonType":"format_only","reason":"仅连字符/空格分隔差异，型号 6204-2Z 一致","confidence":0.95}
        9) 源：视觉检测 | 检测精度 高
           候选：视觉检测 | 检测精度 ±0.02mm
           {"verdict":"uncertain","reasonType":"uncertain","reason":"源为定性描述'高'，无法与定量 ±0.02mm 判定等价，需人工确认","confidence":0.3}

        仅返回严格 JSON：
        {"verdict":"uncertain","reasonType":"uncertain","reason":"...","confidence":0.0}
        """;

    // 本次之前的等价裁决默认内容（无 few-shot 示例），保留为升级源：库中若仍存此内容则自动升级到新默认。
    private const string MatchingEquivalenceAdjudicationV3Content =
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
}