# UI 规范

本规范用于前端页面、表格、弹窗和表单控件，目标是保证企业后台的中性主题、紧凑密度和内容优先布局。

## 设计令牌

- 全局颜色、圆角、字号、阴影统一从 `web/src/style/tokens.scss` 取值。
- 业务页面样式优先使用 `var(--app-*)` 或 `var(--el-*)`，不得新增未归档的十六进制色值。
- 紫色 `--app-primary` 只用于按钮、链接、选中态、进度条、菜单激活等交互强调。
- 正文、背景、边框、表头必须使用中性令牌：`--app-text-primary`、`--app-bg-page`、`--app-border`、`--app-info-bg`。
- 成功、警告、危险、AI 裁决和 diff 统一使用 `--app-success`、`--app-warning`、`--app-danger`、`--app-decision-*`、`--app-diff-*`。

## 密度规则

- Element Plus 默认控件尺寸不超过 `32px`。
- 主内容区外边距不超过 `12px`。
- 卡片 body 默认 `12px`，表格 cell 默认 `6px 0`。
- 数据卡片不做 hover 抬升；只允许轻量边框或背景反馈。
- 页面标题、筛选条件和主操作优先同屏紧凑呈现，避免重复副标题和说明横幅。
- 向导页步骤条优先合并进 `.page-header`，步骤内容组件不再重复展示 `step-title` / `step-desc`。
- 上传入口统一使用 `app-upload-area`，上传框背景、边框和 hover 状态必须来自 `--app-*` 或 `--el-*` 令牌。

## 全高表格

- 列表页和预览页优先使用 `.page.page--fill`、`.table-card`、`.table-region`。
- 主表格使用 `height="100%"`，由父容器分配剩余高度。
- 主表格不得使用固定三位数像素 `max-height`。
- 弹窗内表格使用 `max-height: calc(100vh - Npx)` 或弹窗内部 flex 剩余高度。
- 常规列表分页默认 `50`，选项为 `[20, 50, 100, 200]`；大分页业务页可按场景保留。

## 弹窗宽度

- 弹窗宽度使用四档：S `480px`、M `640px`、L `960px`、XL `1200px`。
- 小屏必须使用 `min(Npx, calc(100vw - 32px))` 或等效响应式写法。
- 禁止新增 520、1080、1100 等非规格宽度，除非组件已有业务约束并在代码注释说明。

## 下拉统一规则

### 1) 搜索区下拉（表单内联筛选）
- 使用类：`search-select` + 宽度档位
- 推荐档位：`search-select--200`（更紧凑）；可选档位收敛为 `200 | 240 | 300`
- 示例：
  - `class="search-select search-select--200"`
  - `popper-class="app-select-popper"`

### 2) 弹窗/弹窗类表单下拉
- 使用类：`dialog-select` + 宽度档位（默认 320）
- 可选档位：`dialog-select--280 | dialog-select--320 | dialog-select--360`
- 示例：
  - `class="dialog-select dialog-select--320"`
  - `popper-class="app-select-popper"`

### 3) 表格内下拉（表格单元格编辑）
- 使用类：`table-select` + 宽度档位
- 可选档位：`table-select--280 | table-select--320 | table-select--360`
- 配合列宽同步：`:width="tableSelectWidth"`
- 示例：
  - `const tableSelectWidth = 320;`
  - `const tableSelectClass = \`table-select table-select--${tableSelectWidth}\`;`

## Popper 统一风格
- 非配置页统一使用：`app-select-popper`（选项行高/字号一致）
- 配置页统一使用：`config-select-popper`
