# 批量回复能力设计

## 背景
现有“严格复用”能力只能从一次刚完成的智能填充任务发起，来源数据依赖填充任务快照。业务现在需要一个独立菜单“批量回复”，允许用户上传一份人工已经回复好的同模板文档，直接将其中的验收与备注批量应用到其他同模板文件。

## 目标
- 提供独立菜单“批量回复”，不依赖智能填充任务上下文。
- 支持 `docx` 和 `xlsx` 两种来源文件与目标文件。
- 来源文件和目标文件必须同格式，不支持跨格式复用。
- 支持像智能填充一样配置多个表格和数据区。
- 仅复制 `验收列` 与 `备注列`。
- 预检和执行规则沿用现有严格复用：文件类型、表格配置、数据区行数、项目+规格顺序必须一致。
- 提供独立 RBAC 权限，和“智能填充”解耦。

## 非目标
- 不引入 AI、匹配、语义搜索或模糊容错。
- 不把人工来源文档转成长期模板。
- 不支持跨格式应用，例如 `docx -> xlsx`。
- 不扩展为任意列复制，只处理验收与备注。

## 推荐方案
新增一条独立的 `BatchReply` 用例链路，复用现有文档读取、表格提取、严格校验、写回与下载打包基础设施，但不复用“智能填充任务快照”作为来源模型。

## 核心设计

### 产品入口
- 新增菜单“批量回复”。
- 页面支持上传 1 份已回复源文档和多份本地目标文档。
- 页面支持配置多个表格，配置项沿用智能填充的表格参数。
- 页面采用“两段式流程”：先预检，再执行并下载。

### 后端边界
- 新增 `BatchReplyController` 提供独立 API。
- 新增 `BatchReplyAppService` 承载预检、执行、下载编排。
- 新增 `BatchReplySessionService` 维护临时来源/目标文件与预检会话。
- 继续复用现有 `DocumentFileAccessService`、`DocumentTableAccessService`、`MatchingResultWriteBackService` 等共享组件。
- 现有 `StrictReuseAppService` 继续只服务于“智能填充完成后的一次性复用”。

### 临时会话
- `preview` 阶段上传来源文件和目标文件，并建立 `sessionId`。
- 会话中保存：
  - 源文件名、文件类型
  - 表格配置
  - 每个源表格的行签名与回复值
  - 目标文件临时路径或临时标识
- `execute` 阶段只接受 `sessionId` 与待执行目标集合。
- 执行前再次复检，避免绕过预检直接写回。

### 严格判定规则
- 文件类型必须一致。
- 目标文件必须具备来源表格配置要求的列范围。
- 每个表格的数据区行数必须一致。
- 每一行的 `项目 + 规格 + 行序` 必须与来源完全一致。
- 任意差异都拒绝该目标文件并返回差异原因。

### 写回范围
- 只把来源文件中的 `验收列` 与 `备注列` 写回目标文件对应单元格。
- 来源中的空值也按空值原样写回，不做“跳过空值”特殊处理。

### 权限模型
- `menu:batch-reply`
- `page:batch-reply:index`
- `btn:batch-reply:preview`
- `btn:batch-reply:execute`

## API 草案

### `POST /api/batch-reply/preview`
- 请求：`multipart/form-data`
- 字段：
  - `sourceFile`
  - `targetFiles[]`
  - `tableConfigsJson`
- 返回：
  - `sessionId`
  - `sourceFileName`
  - `sourceFileType`
  - 逐文件预检结果

### `POST /api/batch-reply/execute`
- 请求：
  - `sessionId`
  - `targetFileIds` 或目标临时标识
- 返回：
  - 成功数 / 失败数
  - 逐文件结果
  - 下载地址 / 下载文件名

### `GET /api/batch-reply/download/{taskId}`
- 单文件直接下载。
- 多文件返回 zip。

## 前端流程
1. 上传已回复源文档。
2. 配置一个或多个表格。
3. 上传多个本地目标文件。
4. 点击“预检批量回复”查看逐文件结果。
5. 点击“执行批量回复”生成结果并下载。

## 风险与约束
- Word 与 Excel 都要补齐同一条链路，不能只做一端。
- 大文件批量上传需要临时文件存储和过期清理策略。
- 表格配置错误会直接导致预检失败，需要提供清晰差异提示。

## 验证建议
- API 集成测试覆盖：来源/目标同格式、预检失败原因、执行成功与 zip 下载。
- 前端回归覆盖：菜单权限、页面预检、执行按钮与结果展示。
- 变更结束前运行：
  - `openspec validate add-batch-reply-from-replied-documents --strict`
  - `dotnet test AcceptanceSpecSystem.sln -c Debug`
  - `pnpm build`
