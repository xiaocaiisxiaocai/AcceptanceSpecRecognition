# UI 规范（非配置页）

本规范用于非配置页的筛选区、弹窗表单与表格内下拉控件，保证宽度与交互密度一致。

## 下拉统一规则

### 1) 搜索区下拉（表单内联筛选）
- 使用类：`search-select` + 宽度档位
- 推荐档位：`search-select--200`（更紧凑）
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

