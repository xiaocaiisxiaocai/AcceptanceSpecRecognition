# Change: 用数据库化匹配知识配置替换旧文本预处理体系

## Why
当前“文本预处理配置 / 同义词管理 / 关键字管理”将通用文本清洗、结构化匹配知识和历史遗留能力混在一起，页面入口分散，主链路职责不清，也不符合当前以证据驱动匹配为核心的设计方向。用户已经明确要求在线编辑并持久化匹配知识，同时移除旧体系。

## What Changes
- 新增数据库持久化的 `MatchingKnowledge` 单例配置能力，并提供统一的读写/重置 API。
- 新增“匹配知识配置”后台页面，用单页方式编辑实体别名、单位别名、单位换算、字段别名和冲突词对。
- 将匹配运行时知识来源从 `appsettings` 切换为数据库配置，系统默认值仅用于初始化和重置。
- **BREAKING** 移除文本预处理配置页面、同义词管理页面、关键字管理页面及对应 API。
- **BREAKING** 移除简繁转换、通用同义词替换、关键字高亮等可配置文本预处理主链路逻辑，仅保留最小安全归一化。
- 迁移旧同义词中可明确识别的数据到新的结构化知识槽位，直接废弃其余旧数据，并删除旧表。

## Impact
- Affected specs: `api`, `data-storage`, `matching-engine`, `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Api`
  - `src/AcceptanceSpecSystem.Core`
  - `src/AcceptanceSpecSystem.Data`
  - `web/src`
  - `tests/`
