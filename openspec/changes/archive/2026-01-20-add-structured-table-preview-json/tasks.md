## 1. Implementation
- [x] 1.1 在 Core 文档模型中新增结构化单元格类型（支持嵌套表格）
- [x] 1.2 更新 Word 解析器：提取 `StructuredCellValue`，并在合并单元格“向下填充”时同步填充结构化值
- [x] 1.3 扩展 API 预览 DTO：新增 `structuredRows`，并在 `GetTablePreview` 中映射返回
- [x] 1.4 前端类型升级：`TableData` 增加 `structuredRows`，TablePreview 增加 JSON 预览开关
- [x] 1.5 本地构建验证：后端/前端均可编译通过

