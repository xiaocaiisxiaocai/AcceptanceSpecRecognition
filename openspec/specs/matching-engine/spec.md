# Matching Engine Capability

## Purpose
定义当前已实现的 Embedding 召回、候选重排、阈值过滤与 AI-only 判定行为，作为智能匹配与预览结果输出的基础能力说明。
## Requirements
### Requirement: Embedding 向量匹配
系统 SHALL 使用 Embedding 向量相似度作为第一阶段召回能力，而不是最终裁决依据。

#### Scenario: Embedding 用于召回候选
- **GIVEN** 输入查询文本"不锈钢管材"
- **WHEN** 系统执行匹配
- **THEN** 系统计算查询文本与候选【项目+规格】组合文本的 Embedding 相似度
- **AND** 使用该相似度召回 TopK 候选
- **AND** 不直接以 Embedding 分数作为最终高置信判定结果

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
系统 SHALL 支持针对验收规格数据执行语义检索，并返回 TopN 相似结果。

#### Scenario: 组合文本语义检索
- **GIVEN** 一条查询文本
- **WHEN** 系统执行验收规格语义检索
- **THEN** 系统将查询文本与候选规格的组合文本进行 Embedding 相似度计算
- **AND** 组合文本至少包含项目、规格、验收标准、备注信息

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
系统 SHALL 在导入阶段支持基于规则和 AI 的疑似重复识别。

#### Scenario: 规则命中优先
- **GIVEN** 导入行与数据库已有规格完全一致，或者项目与规格完全一致但验收/备注不同
- **WHEN** 系统执行导入前检查
- **THEN** 系统优先返回规则命中结果
- **AND** 不再对该导入行继续执行 AI 疑似重复识别

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
系统 SHALL 在最终排序前优先检查关键字段冲突证据，并对存在冲突证据的候选保守降级为人工确认，而不是直接自动采用。

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
系统 SHALL 仅在 AI 等价裁决明确通过、证据充分且无需人工确认时自动采用匹配结果。

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

### Requirement: AI 等价裁决门禁
系统 SHALL 对达到中置信门槛的当前最佳候选执行 AI 等价裁决，并以该裁决作为自动采用前的服务端门禁。

#### Scenario: 当前最佳候选进入 AI 等价裁决
- **AND** 当前最佳候选最终得分大于等于 `0.6`
- **WHEN** 系统完成服务端证据重排
- **THEN** 系统对当前最佳候选执行 AI 等价裁决
- **AND** 即使该候选已经是 Embedding 第一名，也不能跳过该门禁

#### Scenario: 等价裁决未通过时回退人工确认
- **GIVEN** AI 等价裁决返回 `different`、`uncertain`，或调用失败、超时、解析失败
- **WHEN** 系统生成最终决策
- **THEN** 系统将该样本标记为 `manualReview`
- **AND** 不允许自动采用

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

