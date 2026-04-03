## 1. 实现
- [x] 1.1 扩展批量回复单表预览 DTO，支持重复键冲突分组与用户决议
- [x] 1.2 调整后端单表预览逻辑，在重复键场景下返回结构化冲突并按决议重新预览
- [x] 1.3 调整前端批量回复页与 Sheet 配置组件，弹出冲突处理对话框并支持重试预览
- [x] 1.4 补充 API 与前端结构回归测试
- [x] 1.5 运行 `openspec validate update-batch-reply-duplicate-resolution --strict`、相关 `dotnet test` 与 `pnpm --dir web build`
