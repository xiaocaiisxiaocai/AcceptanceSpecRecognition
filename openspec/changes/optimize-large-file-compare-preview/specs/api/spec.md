## ADDED Requirements

### Requirement: 文件对比预览使用有界差异载荷

系统 SHALL 允许文件对比预览仅返回差异项，同时保持覆盖完整文档的分类统计和独立导出能力。

#### Scenario: 仅请求差异项

- **WHEN** 客户端调用文件对比并设置 `includeUnchanged=false`
- **THEN** 响应 items 不包含未变化单元格或段落
- **AND** added、removed、modified、unchanged 和 total 统计仍以完整对比结果计算

#### Scenario: 导出完整对比结果

- **WHEN** 用户导出文件对比结果
- **THEN** 服务端生成完整结果
- **AND** 导出内容不受前端当前预览窗口或仅差异显示状态影响
