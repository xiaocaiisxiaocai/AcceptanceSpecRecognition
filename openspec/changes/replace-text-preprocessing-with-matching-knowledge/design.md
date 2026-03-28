## Context
当前系统已经引入 `MatchingKnowledge` 作为品牌/组织别名、单位换算、字段别名和冲突词对的结构化知识来源，但该能力仍停留在后端配置文件层，用户无法在后台页面直接查看和维护。与此同时，旧的文本预处理、同义词、关键字体系仍保留页面、API 与部分服务接线，形成双轨结构。

用户已明确要求：
- 新页面必须支持在线编辑并持久化到数据库
- 文本预处理、同义词、关键字相关页面和逻辑全部移除
- 旧表不保留，迁移完成后直接清除
- 旧数据只迁移明显可识别的数据，其余直接废弃

## Goals / Non-Goals
- Goals:
  - 提供数据库持久化的统一匹配知识配置
  - 提供单页后台配置界面
  - 从匹配主链路移除旧文本预处理依赖
  - 删除旧页面、旧 API、旧表和旧服务接线
- Non-Goals:
  - 不实现复杂导入导出能力
  - 不实现多版本配置历史
  - 不保留旧页面兼容入口
  - 不迁移语义不明确的旧同义词或关键字数据

## Decisions

### Decision: 使用数据库单例表存储整套匹配知识
新增 `MatchingKnowledgeConfig` 单例实体，在一行中以 JSON 字段存储：
- `EntityAliasesJson`
- `UnitAliasesJson`
- `UnitFactorsJson`
- `FieldAliasesJson`
- `ConflictPairsJson`

同时保留 `Id`、`UpdatedAt` 等基础字段。

Reasons:
- 该数据本质是“一套配置”，而不是高频关系型业务数据
- 单表 JSON 更适合前端整页编辑和后端整包校验
- 避免拆成多张子表带来的额外仓储、排序、唯一性与事务复杂度

Alternatives considered:
- 多子表建模：结构更细，但实现与维护成本更高，不适合当前单套配置场景
- 继续使用 `appsettings.json`：不满足在线编辑与数据库持久化要求

### Decision: 运行时知识来源切换到数据库，配置文件仅用于初始化默认值
现有 `ConfigurationMatchingKnowledgeProvider` 改造为数据库读取 provider。系统默认知识仍保留在代码/配置中，仅用于：
- 首次初始化
- “重置默认”操作

Reasons:
- 满足在线配置与服务重启后保持一致
- 让运行时真实配置与用户界面一致

### Decision: 从匹配主链路中移除旧文本预处理配置能力
移除：
- 简繁转换
- 通用同义词替换
- 关键字高亮

匹配前仅保留最小安全归一化，如：
- `Trim`
- 空白折叠

结构化归一化和冲突校验统一交给 `MatchingKnowledge`。

Reasons:
- 旧预处理会在匹配前“洗平”文本，削弱关键字段证据
- 当前目标是“关键字段严查”，而不是尽量模糊兜底

### Decision: 采用保守迁移策略
旧数据迁移仅处理“明显可识别”的同义词数据：
- 可迁移到 `EntityAliases`
- 可迁移到 `UnitAliases`
- 可迁移到 `FieldAliases`

以下直接废弃：
- 无法明确归类的同义词
- 语义过泛或存在冲突的同义词
- `Keywords`
- `TextProcessingConfig`

Reasons:
- 宁可少迁，也不误迁
- 符合当前匹配引擎对硬冲突与结构化证据的高可信要求

### Decision: 迁移完成后直接删除旧表与旧接口
删除旧表：
- `SynonymGroups`
- `SynonymWords`
- `Keywords`
- `TextProcessingConfigs`

删除旧 API / 页面：
- `/api/text-processing/config`
- `/api/synonyms`
- `/api/keywords`
- 文本预处理配置页
- 同义词管理页
- 关键字管理页

Reasons:
- 用户明确要求不保留旧表
- 避免双轨维护与隐性回退路径

## Risks / Trade-offs
- 风险：旧同义词自动归类错误
  - Mitigation: 仅迁移规则明确的数据；对模糊数据直接丢弃
- 风险：删除旧接口后，前端或测试仍引用旧路径
  - Mitigation: 在同一变更中同步清理页面、路由、权限和测试
- 风险：运行时数据库读取失败导致无知识配置
  - Mitigation: provider 在首次读取时确保默认配置已初始化

## Migration Plan
1. 新增 `MatchingKnowledgeConfig` 表和仓储
2. 创建迁移逻辑：先写入默认知识，再迁移可识别旧同义词
3. 切换 `IMatchingKnowledgeProvider` 到数据库实现
4. 删除旧文本预处理/同义词/关键字服务接线和主链路依赖
5. 新增后台页面与 API
6. 删除旧页面、旧 API、旧表
7. 补回归测试并验证路由/权限/匹配主链路

## Open Questions
- 旧同义词到三类知识槽位的“明显可识别”规则需要在实现阶段通过一组固定白名单/正则/字典来落地，避免运行时启发式过度扩张。
