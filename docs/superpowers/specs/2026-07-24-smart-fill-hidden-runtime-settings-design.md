# 智能填充运行时配置隐藏设计

## 目标

简化智能填充“匹配配置”界面，移除用户无需干预的运行时设置：

- 不再显示 Embedding 服务。
- 不再显示 LLM 服务。
- 不再显示全局和表级“过滤空行”开关。
- 所有预览与执行请求始终启用过滤空行。

## 设计边界

本次只调整智能填充前端的配置展示和请求默认值，不修改后端接口、AI 服务配置中心、运行状态检测、权限判断、匹配算法或预览阻断规则。

Embedding 与 LLM 仍由现有运行时服务选择逻辑自动获取并写入匹配配置。隐藏服务信息不等于禁用服务；仅精确匹配、Embedding 召回和 LLM 复核仍按原有条件运行。

## 组件调整

### 全局匹配配置

`MatchConfig.vue` 删除 Embedding 服务、LLM 服务和过滤空行三个表单项。组件继续在后台加载两类服务状态，并通过现有 `refreshAiServices`、`getServiceStatus` 和配置同步逻辑提供给预览及执行流程。

### 表级配置

`BatchTableConfig.vue` 删除每张表的过滤空行开关，不再允许单表覆盖全局行为。其他表级字段保持不变。

## 数据流

- `defaultMatchConfig.filterEmptySourceRows` 保持为 `true`。
- 全局有效值解析固定返回 `true`。
- 表级预览请求固定发送 `filterEmptySourceRows: true`。
- 执行请求固定发送 `filterEmptySourceRows: true`。
- 已缓存或历史配置中的 `false` 不再影响智能填充请求。

保留 `MatchConfig` 和请求类型中的可选字段以兼容现有 API，不进行跨模块协议清理。

## 测试

- 增加回归测试，确认全局配置组件不再渲染三个表单项。
- 确认表级配置不再渲染过滤空行开关。
- 覆盖全局值或表级值为 `false` 时，预览与执行请求仍解析为 `true`。
- 运行相关 Node/Vitest 测试、Vue 类型检查、ESLint、Prettier 和 `git diff --check`。

## 非目标

- 不删除 AI 服务运行状态检测。
- 不改变 Embedding 或 LLM 不可用时的提示与阻断行为。
- 不修改数据导入、批量回复或其他模块的过滤空行设置。
- 不提交或推送 Git。
