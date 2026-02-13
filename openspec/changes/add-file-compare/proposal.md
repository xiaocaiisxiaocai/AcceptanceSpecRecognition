# Change: 新增文件对比功能（Word/Excel 同类型）

## Why
当前系统缺少对上传文件的对比能力，用户需要在不同版本的验收文档/表格间快速定位差异。

## What Changes
- 新增“文件对比”能力与页面（仅同类型：Word 对比、Excel 全工作簿对比）
- 提供对比结果预览（差异列表 + 高亮）
- 新增对比相关 API（上传/预览/导出）

## Impact
- Affected specs: file-compare（新增），api（新增接口），user-interface（新增页面）
- Affected code: Web 路由与页面、API 控制器与服务、Core 文档抽取与对比逻辑
