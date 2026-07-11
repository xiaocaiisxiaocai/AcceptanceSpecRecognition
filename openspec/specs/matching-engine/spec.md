# Matching Engine Capability

## Purpose
定义当前已实现的 Embedding 召回、候选重排、阈值过滤与 AI-only 判定行为，作为智能匹配与预览结果输出的基础能力说明。
## Requirements
### Requirement: Embedding 向量匹配
系统 SHALL 使用 Embedding 向量相似度作为第一阶段召回能力，而不是最终裁决依据。系统 SHALL 优先复用当前模型、当前用途与当前文本指纹匹配的有效缓存；缓存缺失时可按需生成并写回缓存。

#### Scenario: Embedding 用于召回候选
- **GIVEN** 输入查询文本"不锈钢管材"
- **WHEN** 系统执行匹配
- **THEN** 系统计算查询文本与候选【项目+规格】组合文本的 Embedding 相似度
- **AND** 使用该相似度召回 TopK 候选
- **AND** 不直接以 Embedding 分数作为最终高置信判定结果
- **AND** 候选向量优先从匹配用途的有效缓存读取

### Requirement: Embedding 服务不可用时返回失败
系统必须（SHALL）在 Embedding 服务不可用时返回失败，而不是降级到其他算法。

#### Scenario: Embedding 服务不可用
- **GIVEN** Embedding 服务不可用
- **WHEN** 系统执行匹配
- **THEN** 系统返回“Embedding 服务不可用”的错误

---

### Requirement: 项目与规格组合文本匹配
系统必须（SHALL）基于【项目】与【规格】拼接后的组合文本进行匹配计算。

#### Scenario: 组合文本匹配
- **GIVEN** 用户输入项目="不锈钢管"，规格="Φ50×3mm"
- **WHEN** 系统执行匹配
- **THEN** 系统将项目与规格拼接为"不锈钢管 Φ50×3mm"
- **AND** 使用拼接文本参与相似度计算

---

### Requirement: 候选结果排序与Top-N
系统 SHALL 使用统一的多阶段证据驱动流程对召回候选进行排序与决策。

#### Scenario: 先召回再基于证据决策
- **GIVEN** 候选库中存在多个语义接近的候选
- **WHEN** 系统执行匹配
- **THEN** 系统先按 Embedding 得分召回 TopK 候选
- **AND** 对这些候选生成结构化证据
- **AND** 基于结构化证据、AI 实体复判与 AI 等价裁决决定最终最佳候选
- **AND** 即使 Embedding 第一名已命中，也不能跳过服务端重排与当前最佳候选 AI 等价裁决门禁

### Requirement: 阈值过滤
系统 SHALL 支持可配置的候选过滤阈值，并将该阈值用于第一阶段召回准入。

#### Scenario: 仅召回达到阈值的候选
- **GIVEN** 用户设置候选过滤阈值为 0.3
- **AND** 候选的 Embedding 得分分别为 0.61、0.46、0.29、0.22
- **WHEN** 系统执行第一阶段召回
- **THEN** 系统仅允许得分 0.61 与 0.46 的候选进入 TopK 集合
- **AND** 得分 0.29 与 0.22 的候选不会进入证据生成阶段

#### Scenario: 无候选达到召回阈值
- **GIVEN** 用户设置候选过滤阈值为 0.9
- **AND** 所有候选的 Embedding 得分均低于 0.9
- **WHEN** 系统执行第一阶段召回
- **THEN** 系统返回无匹配结果
- **AND** 不进入后续证据判定流程

### Requirement: 匹配结果包含算法得分明细
系统 SHALL 在匹配结果中返回召回、证据、冲突、歧义与复核相关明细。

#### Scenario: 返回证据与决策摘要
- **WHEN** 系统返回最佳匹配结果
- **THEN** 结果包含 `Embedding` 召回得分
- **AND** 结果包含关键证据摘要、歧义状态与最终决策原因

### Requirement: 默认选择最高得分
系统 SHALL 选择通过证据裁决后的最佳候选作为默认最佳匹配。

#### Scenario: 最佳候选不是最高 Embedding 候选
- **GIVEN** Embedding 召回得分最高的候选存在型号冲突
- **AND** 另一候选与源数据在关键证据上更符合
- **WHEN** 系统完成证据裁决
- **THEN** 系统将无关键冲突且证据更充分的候选标记为默认最佳匹配
- **AND** 不因最高 Embedding 分而覆盖该结果

### Requirement: 运行时实体候选提取
系统 SHALL 在匹配运行时对源项与候选项文本执行实体候选提取和轻量归一化，即使未命中本地最小解析规则，也应尽量提取品牌或组织实体候选。

#### Scenario: 无配置场景提取英文品牌
- **GIVEN** 源文本包含 `Panasonic 设备`
- **AND** 当前本地最小解析规则中未直接定义 `Panasonic -> 松下`
- **WHEN** 系统执行多阶段匹配
- **THEN** 系统仍然提取出源项实体候选 `Panasonic`
- **AND** 该候选可用于后续实体关系判别

### Requirement: LLM 实体关系判别
系统 SHALL 在运行时可选地使用 LLM 对实体候选关系进行判别，但该判别仅用于实体证据，不得直接替代整体匹配排序。

#### Scenario: 中英文品牌被判为别名同一
- **GIVEN** 源项实体候选为 `Panasonic`
- **AND** 候选项实体候选为 `松下`
- **AND** 用户已开启 LLM 实体判别
- **WHEN** 系统对 Top 候选执行实体关系判别
- **THEN** 系统输出 `alias_same` 或 `same`
- **AND** 将其记为正向实体证据

#### Scenario: 品牌冲突被判为不同实体
- **GIVEN** 源项实体候选为 `Panasonic`
- **AND** 候选项实体候选为 `Mitsubishi`
- **AND** 用户已开启 LLM 实体判别
- **WHEN** 系统执行实体关系判别
- **THEN** 系统输出 `conflict`
- **AND** 系统为该候选生成实体冲突问题说明

### Requirement: 未知实体保守降级
系统 SHALL 对无法确认关系的实体候选输出 `unknown`，并保守降级为人工确认，而不是直接拒绝或自动采用。

#### Scenario: 未知品牌无法确认关系
- **GIVEN** 源项实体候选为 `XJTech`
- **AND** 候选项实体候选为 `新境科技`
- **AND** LLM 无法确认两者是否为同一实体
- **WHEN** 系统完成实体关系判别
- **THEN** 系统输出 `unknown`
- **AND** 结果至少降级为 `manualReview`
- **AND** 系统输出 `entity_unknown` 问题说明

### Requirement: 关键结构化证据优先于实体判别
系统 SHALL 保持数值、型号等关键结构化证据的优先级，不允许实体同一证据推翻已存在的关键冲突证据。

#### Scenario: 数值冲突不得被实体同一覆盖
- **GIVEN** 源项与候选项都指向同一品牌
- **AND** 源项包含 `电压等于24V`
- **AND** 候选项包含 `电压等于2.4V`
- **WHEN** 系统完成实体判别和多阶段重排
- **THEN** 系统仍将该候选视为数值冲突候选
- **AND** 最终结果不得因为实体同一而自动采用

### Requirement: 验收规格语义检索
系统 SHALL 支持针对验收规格数据执行语义检索，并返回 TopN 相似结果。系统 SHALL 使用语义搜索用途的 Embedding 缓存，避免复用智能匹配用途的候选向量。

#### Scenario: 组合文本语义检索
- **GIVEN** 一条查询文本
- **WHEN** 系统执行验收规格语义检索
- **THEN** 系统将查询文本与候选规格的组合文本进行 Embedding 相似度计算
- **AND** 组合文本至少包含项目、规格、验收标准、备注信息
- **AND** 候选向量优先从语义搜索用途的有效缓存读取

#### Scenario: 批量查询复用候选集合
- **GIVEN** 一次请求中包含多条查询文本
- **WHEN** 系统执行语义检索
- **THEN** 系统复用同一批候选规格集合完成多条查询计算
- **AND** 分别返回每条查询的 TopN 结果

#### Scenario: 结果按相似度排序
- **GIVEN** 某条查询命中了多条候选规格
- **WHEN** 系统返回结果
- **THEN** 系统按相似度得分降序排列候选结果

#### Scenario: 结果应用最小分数过滤
- **GIVEN** 用户设置最小分数阈值
- **WHEN** 系统返回语义检索结果
- **THEN** 系统过滤掉低于阈值的候选
- **AND** 保留高于等于阈值的结果

### Requirement: LLM思考内容抑制
系统 SHALL 在启用关闭思考模式时，尽量避免向前端和解析逻辑暴露模型思考内容。

#### Scenario: Ollama请求关闭思考模式
- **GIVEN** LLM 服务类型为 Ollama
- **AND** AI 服务配置已开启关闭思考模式
- **WHEN** 系统调用 LLM 复核或生成能力
- **THEN** 系统向底层模型请求传递关闭思考模式参数

#### Scenario: 模型仍返回思考内容
- **GIVEN** 模型响应中包含 `<think>` 思考片段
- **WHEN** 系统处理 LLM 非流式或流式输出
- **THEN** 系统清理思考片段后再用于前端展示或 JSON 解析

### Requirement: 导入阶段疑似重复识别
系统 SHALL 在导入阶段支持基于规则和 AI 的疑似重复识别。对于仅规格导入且项目由规格补齐的行，系统 SHALL 使用补齐后的 `Project + Specification` 参与规则判重和 AI 疑似重复识别。

#### Scenario: 规则命中优先
- **GIVEN** 导入行与数据库已有规格完全一致，或者项目与规格完全一致但验收/备注不同
- **WHEN** 系统执行导入前检查
- **THEN** 系统优先返回规则命中结果
- **AND** 不再对该导入行继续执行 AI 疑似重复识别

#### Scenario: 仅规格导入使用补齐后的组合键
- **GIVEN** 导入行为仅规格导入行
- **AND** 系统已将该行项目补齐为规格文本
- **WHEN** 系统执行导入前检查
- **THEN** 系统使用补齐后的 `项目=规格文本` 与 `规格=规格文本` 执行完全重复和差异重复检测
- **AND** 不使用空项目或占位项目参与判重

#### Scenario: Embedding 召回语义候选
- **GIVEN** 导入行未命中规则层
- **AND** 用户启用了 AI 疑似重复识别
- **WHEN** 系统执行导入前检查
- **THEN** 系统基于“项目 + 规格”组合文本计算 Embedding 相似度
- **AND** 返回得分达到阈值的 Top-K 既有规格候选

#### Scenario: LLM 复核语义候选
- **GIVEN** 用户启用了 LLM 复核
- **AND** Embedding 已召回至少一条候选
- **WHEN** 系统执行语义复核
- **THEN** 系统对候选的项目与规格语义是否代表同一条验收规格进行复核
- **AND** 仅将达到 LLM 通过阈值的候选作为语义命中结果返回

#### Scenario: 覆盖已有记录
- **GIVEN** 用户在导入确认中选择覆盖已有
- **WHEN** 系统执行最终导入
- **THEN** 系统更新命中的已有规格记录
- **AND** 不新增一条重复的验收规格记录

### Requirement: 结构化证据生成
系统 SHALL 为每个召回候选生成统一的结构化匹配证据。

#### Scenario: 生成关键字段证据
- **GIVEN** 源文本与候选文本均包含数值、型号和品牌描述
- **WHEN** 系统处理召回候选
- **THEN** 系统提取数值约束、型号/料号、品牌/单位实体、方向词与布尔条件等证据
- **AND** 为每项证据标记标准化结果与关系类型

### Requirement: 关键冲突证据保守降级
系统 SHALL 在最终排序前优先检查关键字段冲突证据，并对存在冲突证据的候选保守降级为人工确认，而不是直接自动采用。当且仅当 `EnableLlmSemanticPriority` 开启时，系统 SHALL 允许 LLM 等价裁决（判定 `equivalent` 且自评置信度不低于 `LlmEquivalenceMinConfidence`）覆盖该保守降级；该开关关闭时保守降级为绝对门禁，LLM 裁决不可覆盖。

#### Scenario: 数值约束明确冲突
- **GIVEN** 源文本包含"宽度小于0.5cm"
- **AND** 候选文本包含"宽度等于0.7cm"
- **WHEN** 系统完成数值约束比较
- **THEN** 系统为该候选生成冲突证据与问题说明
- **AND** 该候选不得自动采用

#### Scenario: 型号或品牌明确冲突
- **GIVEN** 源文本与候选文本在同一槽位上提取到不同的型号或不同的标准化品牌实体
- **WHEN** 系统完成证据判定
- **THEN** 系统为该候选生成冲突证据与问题说明
- **AND** 不得因为文本语义接近而提升为自动采用结果

#### Scenario: 语义优先模式下 LLM 等价覆盖冲突降级
- **GIVEN** `EnableLlmSemanticPriority` 已开启
- **AND** 当前最佳候选存在关键字段冲突证据
- **AND** LLM 等价裁决返回 `equivalent` 且自评置信度不低于 `LlmEquivalenceMinConfidence`
- **WHEN** 系统生成最终决策
- **THEN** 系统 SHALL 允许自动采用该候选，覆盖保守降级
- **AND** 系统 SHALL 在结果说明中标注该结果由语义优先模式下的 LLM 裁决放行

#### Scenario: 标准模式下冲突降级不可被覆盖
- **GIVEN** `EnableLlmSemanticPriority` 关闭
- **AND** 当前最佳候选存在关键字段冲突证据
- **WHEN** 系统生成最终决策
- **THEN** 系统 SHALL 将该候选保守降级为人工确认
- **AND** 即使 LLM 判定等价也不得自动采用

### Requirement: 关键字段相容判定
系统 SHALL 对关键字段给出 `Exact`、`Compatible`、`Overlap` 或 `Conflict` 等关系判定。

#### Scenario: 点值满足区间约束
- **GIVEN** 源文本包含"宽度小于0.5cm"
- **AND** 候选文本包含"宽度等于0.2cm"
- **WHEN** 系统完成数值约束比较
- **THEN** 系统将该关系判定为 `Compatible`
- **AND** 允许该候选继续参与后续排序

### Requirement: 实体候选提取与数值单位识别由运行时最小规则维护
系统 SHALL 在运行时保留实体候选提取、轻量归一化与数值单位识别所需的最小内部规则，而不是依赖对外可编辑的品牌别名、单位换算或冲突词配置契约。

#### Scenario: 运行时提取实体候选并交给 AI 判别
- **GIVEN** 源文本包含 `Panasonic 设备`
- **AND** 候选文本包含 `松下设备`
- **WHEN** 系统执行运行时实体候选提取并进入 AI 实体判别链路
- **THEN** 系统能够提取出 `Panasonic` 与 `松下` 作为实体候选
- **AND** 该运行时最小规则不通过独立对外配置 API 维护

### Requirement: 高歧义样本按需触发 LLM 复核
系统 SHALL 仅对高歧义样本触发 LLM 复核，且 LLM 仅基于结构化证据做审核判断。

#### Scenario: 高歧义样本触发复核
- **GIVEN** 前两名候选在关键冲突证据之外的证据结果接近
- **AND** 当前样本被判定为高歧义
- **WHEN** 系统完成证据重排
- **THEN** 系统向 LLM 提交结构化证据摘要进行复核
- **AND** LLM 不作为全量候选的主排序器

### Requirement: 复核失败回退为人工确认
系统 SHALL 在进入 LLM 复核流程但复核失败或超时时回退为人工确认。

#### Scenario: 复核超时
- **GIVEN** 某样本已被判定为高歧义并进入 LLM 复核
- **AND** LLM 调用超时或返回失败
- **WHEN** 系统生成最终决策
- **THEN** 系统将该样本标记为需要人工确认
- **AND** 不得自动采用该匹配结果

### Requirement: 自动采用门禁
系统 SHALL 仅在 AI 等价裁决明确通过、证据充分且无需人工确认时自动采用匹配结果。在标准模式下，自动采用以"不存在关键冲突证据"为前提；在 `EnableLlmSemanticPriority` 开启时，存在关键冲突证据的候选 SHALL 改由 LLM 等价裁决决定是否自动采用，而不是被前置拦截。

#### Scenario: 满足自动采用条件
- **GIVEN** 最佳候选不存在关键冲突证据
- **AND** 关键字段关系为 `Exact` 或 `Compatible`
- **AND** 当前样本未被标记为高歧义，或高歧义但 LLM 复核通过
- **WHEN** 系统生成最终决策
- **THEN** 系统允许自动采用该候选

#### Scenario: 证据不足时禁止自动采用
- **GIVEN** 最佳候选不存在明确关键冲突证据
- **AND** 但关键字段仅得到 `Overlap` 或 `PossiblyRelated` 结果
- **WHEN** 系统生成最终决策
- **THEN** 系统将该样本标记为需要人工确认
- **AND** 不自动采用该候选

#### Scenario: 语义优先模式下冲突候选进入 LLM 裁决而非前置拦截
- **GIVEN** `EnableLlmSemanticPriority` 已开启
- **AND** 最佳候选存在关键冲突证据
- **WHEN** 系统进入自动采用门禁
- **THEN** 系统 SHALL 调用 LLM 等价裁决，由裁决结果决定是否自动采用
- **AND** 系统 SHALL NOT 仅因存在冲突证据就直接拦截为人工确认

### Requirement: AI 等价裁决门禁
The system SHALL support AI equivalence adjudication as an optional smart-fill gate, and SHALL NOT run it during synchronous matching unless the matching configuration explicitly enables it.

#### Scenario: 当前最佳候选进入 AI 等价裁决
- **WHEN** smart-fill matching configuration enables AI equivalence adjudication and the current best candidate reaches the configured adjudication gate
- **THEN** the system SHALL request an AI equivalence verdict for the current best candidate

#### Scenario: 等价裁决未通过时回退人工确认
- **WHEN** AI equivalence adjudication returns `different`, `uncertain`, no valid result, or fails
- **THEN** the system SHALL require manual confirmation for the row

#### Scenario: 默认跳过同步等价裁决
- **WHEN** smart-fill matching configuration does not explicitly enable AI equivalence adjudication
- **THEN** the system SHALL skip AI equivalence adjudication during synchronous matching and continue with local evidence-based review state

### Requirement: 匹配运行时仅保留最小安全归一化并由 AI 完成关键判别
系统必须（SHALL）在匹配时仅执行最小安全文本归一化，不再依赖运行时硬编码别名表、字段别名、单位换算表或冲突词对来驱动召回后判别；召回后的实体关系与语义等价判断由 AI 子链路完成。

#### Scenario: 匹配前只做最小安全归一化
- **WHEN** 系统执行匹配
- **THEN** 系统仅执行去首尾空白、空白折叠等最小安全文本归一化
- **AND** 不再应用硬编码实体别名、字段别名、单位换算或冲突词对规则来直接驱动召回后判别

#### Scenario: 召回后由 AI 判断实体关系
- **GIVEN** 系统已召回候选项
- **WHEN** 当前候选需要判断品牌或组织是否为同一实体
- **THEN** 系统通过 AI 实体判别链路返回 same、alias_same、conflict 或 unknown

#### Scenario: 召回后由 AI 判断语义等价
- **GIVEN** 系统已选出当前最佳候选
- **WHEN** 需要判断源规格与候选规格是否仅存在等价表达差异
- **THEN** 系统通过 AI 等价裁决返回 equivalent、different 或 uncertain
- **AND** 服务端执行门禁以该裁决结果和当前决策为准

### Requirement: LLM Prompt 模板按运行时子场景隔离
系统 SHALL 按运行时子场景分别使用智能填充复核、导入重复复核、实体判别与等价裁决模板，不再共享复核模板名称。

#### Scenario: 智能填充复核使用独立模板
- **GIVEN** 智能填充流程需要构建复核 Prompt
- **WHEN** 系统构建复核 Prompt
- **THEN** 系统读取 `matching-review` 系统模板

#### Scenario: 导入重复复核使用独立模板
- **GIVEN** 用户在导入疑似重复识别中开启 LLM 复核
- **WHEN** 系统构建复核 Prompt
- **THEN** 系统读取 `import-duplicate-review` 系统模板

#### Scenario: 实体判别使用独立模板
- **GIVEN** 系统对候选执行 LLM 实体关系判别
- **WHEN** 系统构建实体判别 Prompt
- **THEN** 系统读取 `matching-entity-resolution` 系统模板

#### Scenario: 等价裁决使用独立模板
- **GIVEN** 系统对当前最佳候选执行 AI 等价裁决
- **WHEN** 系统构建等价裁决 Prompt
- **THEN** 系统读取 `matching-equivalence-adjudication` 系统模板

### Requirement: Prompt 模板无效时拒绝进入运行时
系统 SHALL 在模板保存或预览阶段校验占位符和结构化输出要求，阻止无效模板进入运行时。

#### Scenario: 缺失必需占位符
- **WHEN** 管理员保存缺失必需占位符的系统模板
- **THEN** 系统拒绝保存
- **AND** 返回缺失的占位符列表

#### Scenario: 非法占位符
- **WHEN** 管理员保存包含未知占位符的系统模板
- **THEN** 系统拒绝保存
- **AND** 返回未知占位符列表

#### Scenario: 结构化输出预览失败
- **WHEN** 管理员对要求 JSON 输出的模板执行预览
- **AND** 模板渲染后的内容无法通过结构化解析
- **THEN** 系统返回预览失败原因

### Requirement: 智能填充当前最佳候选 AI 等价裁决门禁
系统 SHALL 在智能填充当前最佳候选中使用 AI 判断源项与候选项是否属于等价表达，而不是要求客户维护等价规则。

#### Scenario: 当前最佳候选触发 AI 等价裁决
- **GIVEN** 智能填充已通过 Embedding 召回并完成多阶段证据重排
- **AND** 最佳候选没有硬冲突
- **AND** 最佳候选最终得分大于等于 `0.6`
- **WHEN** 系统进入 AI 等价裁决门禁
- **THEN** 系统调用 AI 判断源项与候选项是否为等价表达
- **AND** 系统不要求客户维护符号、标点或等价表达规则

#### Scenario: AI 判断等价表达
- **GIVEN** 源项与候选项在换行、普通标点、符号表达或自然语言表达上不同
- **AND** AI 返回 `equivalent`
- **WHEN** 系统生成最终匹配结果
- **THEN** 系统 SHALL 不因该表现差异降低置信度
- **AND** 系统 SHALL 在结果中保留 AI 等价裁决说明

#### Scenario: AI 判断不同或不确定
- **GIVEN** AI 等价裁决返回 `different` 或 `uncertain`
- **WHEN** 系统生成最终匹配结果
- **THEN** 系统 SHALL 将该结果标记为需要人工确认
- **AND** 系统 SHALL 在结果中保留 AI 裁决原因

#### Scenario: AI 裁决失败回退
- **GIVEN** AI 等价裁决调用超时、失败或返回无法解析的结构
- **WHEN** 系统生成最终匹配结果
- **THEN** 系统 SHALL 将该结果按 `uncertain` 处理
- **AND** 系统 SHALL 默认进入人工确认

### Requirement: AI 等价裁决结构化输出
系统 SHALL 要求 AI 等价裁决返回固定结构，便于后端稳定解析和前端展示。

#### Scenario: 返回固定 JSON
- **WHEN** 系统调用 AI 等价裁决
- **THEN** AI 输出 SHALL 包含 `verdict`
- **AND** AI 输出 SHALL 包含 `reasonType`
- **AND** AI 输出 SHALL 包含中文 `reason`
- **AND** `verdict` 仅允许 `equivalent`、`different`、`uncertain`

#### Scenario: 等价表达原因分类
- **GIVEN** AI 判断源项与候选项语义等价
- **WHEN** 系统解析 AI 裁决结果
- **THEN** `reasonType` SHALL 支持 `format_only`、`punctuation_only`、`equivalent_expression` 或 `symbol_equivalent`
- **AND** 这些原因类型 SHALL 不单独触发置信度降低

#### Scenario: 差异原因分类
- **GIVEN** AI 判断源项与候选项存在语义差异或无法确认
- **WHEN** 系统解析 AI 裁决结果
- **THEN** `reasonType` SHALL 支持 `semantic_difference`、`symbol_conflict` 或 `uncertain`
- **AND** 这些原因类型 SHALL 使结果进入人工确认

### Requirement: Embedding 缓存定时预热
系统 SHALL 支持在配置的低峰时间批量预热验收规格 Embedding 缓存。

#### Scenario: 定时补齐历史缓存
- **GIVEN** 系统配置了每日预热时间
- **AND** 历史验收规格存在缺失的匹配用途缓存
- **WHEN** 到达预热时间
- **THEN** 后台任务批量生成缺失向量
- **AND** 写入 `EmbeddingCaches`
- **AND** 单轮处理数量不得超过配置上限

#### Scenario: Embedding 服务不可用
- **GIVEN** 到达预热时间
- **AND** Embedding 服务不可用
- **WHEN** 后台任务尝试预热缓存
- **THEN** 系统记录失败日志
- **AND** 不删除已有缓存
- **AND** 不改变智能填充请求中 Embedding 服务不可用时显式失败的行为

### Requirement: 智能填充手动规格匹配模式
系统 SHALL 支持由用户手动选择“仅规格”匹配方式，并在当前客户、机型、制程与权限范围内按规格匹配历史验收规格。

#### Scenario: 默认保持项目规格匹配
- **GIVEN** 用户未选择“仅规格”匹配方式
- **WHEN** 系统执行智能填充匹配
- **THEN** 系统继续按现有“项目+规格”语义匹配候选
- **AND** 不因项目不一致自动切换为仅规格匹配

#### Scenario: 用户选择仅规格匹配
- **GIVEN** 用户选择“仅规格”匹配方式
- **AND** 当前客户、机型、制程与权限范围内存在规格完全一致的历史验收规格
- **WHEN** 系统执行智能填充匹配
- **THEN** 系统允许忽略项目差异命中该历史验收规格
- **AND** 回填该历史验收规格的验收标准与备注
- **AND** 匹配结果标记匹配依据为“规格”

#### Scenario: 仅规格匹配存在多个候选
- **GIVEN** 用户选择“仅规格”匹配方式
- **AND** 当前范围内同一规格存在多条不同历史验收规格
- **WHEN** 系统执行智能填充匹配
- **THEN** 系统不得直接自动采用
- **AND** 系统将结果降级为人工确认

### Requirement: LLM 语义优先模式
系统 SHALL 提供可选的 LLM 语义优先模式（`EnableLlmSemanticPriority`，默认关闭）。开启后，LLM 等价裁决具有最高权威，覆盖确定性硬冲突门禁，以最大化语义命中率；关闭时系统行为与标准模式完全一致。

#### Scenario: 默认关闭不影响标准模式
- **GIVEN** 请求未开启 `EnableLlmSemanticPriority`
- **WHEN** 系统执行匹配门禁决策
- **THEN** 系统 SHALL 保持硬冲突绝对门禁优先级
- **AND** 硬冲突行 SHALL 强制人工确认，AI 裁决不可覆盖

#### Scenario: 语义优先模式下 LLM 等价覆盖硬冲突
- **GIVEN** 请求开启 `EnableLlmSemanticPriority`
- **AND** 当前最佳候选存在硬冲突（数值/单位/比较符/温度/方向差异）
- **WHEN** LLM 等价裁决返回 `equivalent`
- **AND** LLM 自评置信度大于等于 `LlmEquivalenceMinConfidence`
- **THEN** 系统 SHALL 将该结果判定为自动填充
- **AND** 系统 SHALL 在结果说明中标注「语义优先模式下交由 LLM 裁决」

#### Scenario: 语义优先模式下硬冲突行进入 LLM 裁决
- **GIVEN** 请求开启 `EnableLlmSemanticPriority`
- **AND** 当前最佳候选存在硬冲突
- **WHEN** 系统进入门禁决策
- **THEN** 系统 SHALL 调用 LLM 等价裁决，而不是前置拦截为人工确认

#### Scenario: 置信度门槛护栏在语义优先模式下仍生效
- **GIVEN** 请求开启 `EnableLlmSemanticPriority`
- **AND** `LlmEquivalenceMinConfidence` 大于 0
- **WHEN** LLM 等价裁决返回 `equivalent` 但自评置信度低于 `LlmEquivalenceMinConfidence`
- **THEN** 系统 SHALL 将该结果标记为需要人工确认
- **AND** 系统 SHALL NOT 自动填充

### Requirement: 语义优先模式召回阈值
系统 SHALL 在语义优先模式下使用独立的召回分数下限 `LlmSemanticRecallThreshold`（默认 0.5，取值范围 `[0.1, 0.9]`），以扩大 LLM 等价裁决的候选覆盖面。

#### Scenario: 低 Embedding 分候选被召回进入 LLM
- **GIVEN** 请求开启 `EnableLlmSemanticPriority`
- **AND** 候选 Embedding 分低于标准高置信阈值但高于 `LlmSemanticRecallThreshold`
- **WHEN** 系统执行召回
- **THEN** 该候选 SHALL 被召回并进入 LLM 等价裁决

### Requirement: 文档结构识别三层流水线
系统 SHALL 使用客户模板、规则字典和 LLM 结构裁决组成文档结构识别流水线。

#### Scenario: 客户模板优先命中
- **GIVEN** 当前客户存在与表格结构指纹匹配的模板
- **WHEN** 系统识别该表格
- **THEN** 系统使用模板结果
- **AND** 字段来源标记为 `template`

#### Scenario: 规则字典识别表头
- **GIVEN** 表头可被内置规则、全局学习词或客户学习词识别
- **WHEN** 系统识别该表格
- **THEN** 系统输出规则识别结果
- **AND** 字段来源标记为 `rule`

#### Scenario: 低置信结构进入 LLM 裁决
- **GIVEN** 规则层无法确定关键字段或表头位置置信度不足
- **WHEN** 系统识别该表格
- **THEN** 系统可以调用 LLM 结构裁决
- **AND** LLM 结果只填补未决字段，不覆盖已高置信规则结果

### Requirement: 结构识别自动采用前确定性体检
系统 MUST 在自动采用识别结果前执行确定性体检。

#### Scenario: 体检通过才允许自动采用
- **GIVEN** 识别结果声称高置信
- **WHEN** 系统准备输出自动采用决策
- **THEN** 系统检查必需字段、重复列、数据区非空率和项目规格疑似判反等规则
- **AND** 体检通过后才允许自动采用

#### Scenario: 体检失败降级确认
- **GIVEN** 识别结果中规格列为空或字段列重复
- **WHEN** 系统执行确定性体检
- **THEN** 系统将该表决策降级为需要确认
- **AND** 输出失败原因

### Requirement: 结构识别支持仅规格判定
系统 SHALL 在无法可靠识别项目列时区分仅规格模式和疑似漏识别项目列。

#### Scenario: 明确仅规格表
- **GIVEN** 表格中没有项目语义列
- **AND** 规格列识别高置信
- **WHEN** 系统完成结构识别
- **THEN** 系统标记该表为仅规格模式
- **AND** 智能填充可使用 `SpecificationOnly` 匹配模式

#### Scenario: 疑似存在项目列但低置信
- **GIVEN** 表格中存在疑似项目语义列但置信度不足
- **WHEN** 系统完成结构识别
- **THEN** 系统将该表标记为需要确认
- **AND** 不直接按仅规格模式自动采用

### Requirement: 结构识别 LLM 使用独立 Prompt 模板
系统 SHALL 为文档结构识别 LLM 裁决使用独立系统 Prompt 模板场景。

#### Scenario: 结构裁决读取独立模板
- **WHEN** 系统调用 LLM 进行文档结构裁决
- **THEN** 系统读取智能结构识别专用 Prompt 模板
- **AND** 不复用智能填充复核、导入重复复核、实体判别或等价裁决模板

### Requirement: 智能结构识别 LLM 裁决参考历史模板案例
系统 SHALL 在智能结构识别进入 LLM 结构裁决时，向 LLM 提供少量同客户历史结构模板案例作为参考上下文。

#### Scenario: 低置信结构裁决注入历史案例
- **GIVEN** 当前客户存在已经确认过的文档结构模板
- **AND** 当前表格规则识别未通过自动采用健康检查
- **WHEN** 系统调用 LLM 结构裁决
- **THEN** LLM 输入包含最多 3 个同客户历史结构案例
- **AND** 每个案例包含表头、列映射、表头行数、数据起始行、仅规格标记、使用次数和相似度

#### Scenario: 无可用历史案例时保持原流程
- **GIVEN** 当前请求没有客户 ID 或客户没有相似历史模板
- **WHEN** 系统调用 LLM 结构裁决
- **THEN** LLM 输入包含空历史案例数组
- **AND** 结构裁决流程继续使用当前表格摘要和规则候选执行

#### Scenario: 历史案例不得绕过健康检查
- **GIVEN** LLM 基于历史案例返回结构候选
- **WHEN** 系统融合 LLM 结果
- **THEN** 系统仍然执行确定性健康检查
- **AND** 只有健康检查通过的结果才允许自动采用

#### Scenario: 高置信模板命中不调用 LLM 裁决
- **GIVEN** 当前客户存在与当前表头精确或高相似匹配的模板
- **WHEN** 系统通过模板直接生成自动采用结果
- **THEN** 系统不为该表调用 LLM 结构裁决

### Requirement: 智能结构识别表格类型分类
系统 SHALL 在智能结构识别过程中基于外置路由规则和结构完整性对每张表进行类型分类，用于区分主验收表、专项验收表和辅助表。

#### Scenario: 混合 Excel 工作簿按配置分类
- **GIVEN** Excel 工作簿同时包含验收规格表、报价单、Layout、Utility 和备品清单
- **AND** 当前客户或全局规则中配置了这些辅助表的路由规则
- **WHEN** 系统执行智能结构识别
- **THEN** 系统为每张表输出业务类型
- **AND** 报价单、Layout、Utility 和备品清单不得仅因为含有“项目”或“备注”就被推荐为主验收表

#### Scenario: 未配置业务词不跳过
- **GIVEN** Excel 工作簿包含表名为报价单的工作表
- **AND** 当前客户和全局规则均未配置报价单相关路由规则
- **WHEN** 系统执行智能结构识别
- **THEN** 系统不得仅凭后端硬编码业务词输出 `Skip`
- **AND** 表格类型为 `Unknown` 或由结构完整性推断得到的类型

#### Scenario: Word 主规格表保持推荐
- **GIVEN** Word 文档包含标准项目、技术要求、供方能力和备注列
- **WHEN** 系统执行智能结构识别
- **THEN** 系统将该表推荐为可导入验收规格表

### Requirement: 智能结构识别文档级候选表排序
系统 SHALL 基于表格类型、字段完整性、健康检查结果、历史案例相似度和数据区质量为全文档表格生成候选排序。

#### Scenario: 推荐最可能导入的表
- **GIVEN** 一个文件包含多张表
- **WHEN** 系统返回智能结构识别结果
- **THEN** 最可能作为验收规格导入的表具有更高 `rankingScore`
- **AND** 前端可以按该排序优先展示推荐表

#### Scenario: 非验收表降低排序
- **GIVEN** 某张表命中辅助资料或非验收规格表的外置路由规则
- **WHEN** 系统计算候选排序
- **THEN** 该表的推荐级别为 `Skip` 或低优先级确认
- **AND** 不消耗优先 LLM 裁决预算

### Requirement: 智能结构识别原因可解释
系统 SHALL 对需要确认或建议跳过的表返回结构化问题原因。

#### Scenario: 缺少必需列
- **GIVEN** 某张表缺少规格列
- **WHEN** 系统返回识别结果
- **THEN** `issues` 包含缺少规格列的原因编码和用户可读说明

#### Scenario: 命中跳过规则
- **GIVEN** 某张表命中推荐结果为 `Skip` 的外置路由规则
- **WHEN** 系统返回识别结果
- **THEN** `issues` 包含命中路由规则的原因编码和用户可读说明

### Requirement: 智能结构识别 LLM 预算按候选价值分配
系统 SHALL 将有限的 LLM 结构裁决预算优先用于高价值灰区候选表，而不是简单按表格遍历顺序消耗。

#### Scenario: 优先裁决高价值灰区表
- **GIVEN** 一个 Excel 文件包含多张低价值辅助表和一张疑似验收规格表
- **AND** LLM 结构裁决预算有限
- **WHEN** 系统执行结构裁决
- **THEN** 系统优先对疑似验收规格表执行裁决
- **AND** 辅助表不得抢占优先裁决预算

### Requirement: 智能结构识别以结构案例为主信号
系统 SHALL 在智能结构识别中优先使用表头结构、列映射、结构健康检查和历史结构模板作为主识别信号，不得将 Excel Sheet 名或 Word 附近文本作为主识别依据。

#### Scenario: Word 多表按结构匹配
- **GIVEN** Word 文档中存在多张表且每张表表头不同
- **WHEN** 系统执行智能结构识别
- **THEN** 系统分别基于每张表的表头、列映射和数据结构查找历史结构案例
- **AND** 不依赖文件名、段落标题、表格序号或虚构表名决定表格类型

#### Scenario: Excel Sheet 名仅作弱信号
- **GIVEN** Excel 工作簿中的 Sheet 名与历史模板名称相似
- **WHEN** 当前表头与历史结构模板不相似
- **THEN** 系统不得仅凭 Sheet 名相似自动采用该历史模板

### Requirement: 智能结构路由规则作为辅助覆盖信号
系统 SHALL 将智能结构路由规则作为人工兜底、排除或推荐覆盖信号，而不是替代表头结构识别的主流程。

#### Scenario: 手工跳过规则命中辅助表
- **GIVEN** 管理员配置了匹配表头或样例内容的跳过规则
- **WHEN** 上传文档中的某张表命中该规则
- **THEN** 系统可将该表标记为建议跳过
- **AND** 响应中保留命中规则的原因说明

#### Scenario: 无路由规则时继续结构识别
- **GIVEN** 当前客户没有配置有效路由规则
- **WHEN** 系统识别 Word 或 Excel 表格
- **THEN** 系统继续通过表头、列映射、健康检查和历史结构模板判断
- **AND** 不因为表名或 Sheet 名看起来像辅助资料而直接跳过

#### Scenario: 忽略历史学习路由规则
- **GIVEN** 数据库中残留来源为 `Learned` 的智能结构路由规则
- **WHEN** 系统加载有效路由规则执行识别
- **THEN** 系统不得让这些历史学习路由规则参与匹配
- **AND** 仅使用人工、内置或 AI 建议的辅助覆盖规则

### Requirement: 表头字段识别词以数据库为唯一来源
智能结构识别在将表格列判定为 项目/规格/验收/备注 字段时，其表头识别关键词 SHALL 仅来自数据库 `ColumnMappingRules`（经 `GetEffectiveForCustomerAsync` 合并全局与客户级规则）；后端识别代码 SHALL NOT 内置这些表头字段词。判断"值是否像规格值/表是否像验收表"的内容特征词与单位符号不属于表头字段词，保留在代码，不受此要求约束。

#### Scenario: 识别仅依据数据库表头字段词
- **WHEN** 系统对上传文档执行结构识别
- **THEN** 列到字段的判定仅使用数据库中启用的表头字段词
- **AND** 运维在配置页新增/停用某表头词后，无需改代码即可改变后续识别结果

#### Scenario: 数据库无某字段词时的降级
- **WHEN** 某字段在数据库中没有任何启用的表头识别词
- **THEN** 该字段的表头关键词匹配跳过
- **AND** 系统仍可依据数据样本推断（如 OK/NG、技术参数格式）给出候选，不使整表识别失败

### Requirement: 最终提交流程沉淀列映射学习
系统 SHALL 在普通导入和普通智能填充最终成功后，将用户最终使用的表头列映射学习为客户级列映射规则。

#### Scenario: 普通导入成功后学习列映射
- **GIVEN** 用户在普通 Word 或 Excel 导入中选择了项目、规格、验收和备注列
- **WHEN** 导入最终成功写入或覆盖验收规格数据
- **THEN** 系统将对应表头文本学习为当前客户的 `ColumnMappingRules`
- **AND** 学习规则使用 `Source = Learned`、`MatchMode = Equals`、`Priority >= 100`

#### Scenario: 普通智能填充成功后学习列映射
- **GIVEN** 用户在普通 Word 或 Excel 智能填充中选择了项目、规格、验收和备注列
- **WHEN** 填充最终成功持久化结果
- **THEN** 系统将对应表头文本学习为当前客户的 `ColumnMappingRules`
- **AND** 系统不写入表格路由规则

#### Scenario: 非最终成功流程不学习
- **GIVEN** 普通导入或普通智能填充仍处于预览、待确认或失败状态
- **WHEN** 流程尚未最终成功
- **THEN** 系统不得写入新的列映射学习规则

#### Scenario: 学习失败不阻断主流程
- **GIVEN** 普通导入或普通智能填充已经最终成功
- **AND** 列映射学习写入失败
- **WHEN** 系统返回本次业务操作结果
- **THEN** 导入或填充仍按成功返回
- **AND** 系统记录可排查的告警日志

### Requirement: 智能结构列语义召回保守门禁
系统 SHALL 仅在确定性列映射不足时对未映射表头执行列语义召回，并将结果作为人工确认建议而非自动采用依据。

#### Scenario: 未映射规格语义生成候选
- **GIVEN** 表头包含未命中列映射规则的规格语义表头
- **AND** 规则识别未能得到规格列
- **WHEN** 系统执行智能结构识别
- **THEN** 系统可以调用列语义召回能力生成 `Specification` 候选建议
- **AND** 建议包含置信度和理由

#### Scenario: 方法列不得替代结果列
- **GIVEN** 表头同时包含验收方法类表头和确认结果类表头
- **WHEN** 系统执行列语义召回
- **THEN** 系统不得把验收方法类表头作为最终验收结果列自动采用
- **AND** 如存在确认结果类表头，应优先建议该表头作为 `Acceptance`

#### Scenario: 语义召回不得单独自动采用
- **GIVEN** 规则识别结果缺少关键字段
- **AND** 列语义召回给出了高置信候选
- **WHEN** 系统生成结构识别决策
- **THEN** 系统仍必须执行结构健康检查
- **AND** 最终结果不得仅凭该语义召回候选进入自动采用

#### Scenario: 召回失败回退规则结果
- **GIVEN** 列语义召回调用失败、超时或返回非法字段
- **WHEN** 系统生成结构识别结果
- **THEN** 系统丢弃该召回结果
- **AND** 保留确定性规则识别和健康检查结果

