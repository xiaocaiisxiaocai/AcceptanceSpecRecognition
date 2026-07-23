## 1. Proposal Approval
- [x] 1.1 用户确认阶段 4 只做“仅规格导入时规格补项目”，不做空项目入库。
- [x] 1.2 用户确认疑似漏识别项目列时必须人工确认，不自动补项目。

## 2. Core / Application Rules
- [x] 2.1 增加仅规格导入资格判断，覆盖明确仅规格、疑似项目列、规格列健康和列越界。
- [x] 2.2 抽出 Word / Excel 共用的项目回填转换逻辑。
- [x] 2.3 导入行构造时在符合资格时设置 `Project = Specification`。
- [x] 2.4 不符合资格时返回明确错误或待确认状态。

## 3. Import Duplicate Detection
- [x] 3.1 确保规则判重使用回填后的 `Project + Specification`。
- [x] 3.2 确保 AI 疑似重复识别使用回填后的组合文本。
- [x] 3.3 覆盖已有记录时按现有覆盖语义更新，不新增重复数据。

## 4. API / UI Contract
- [x] 4.1 导入配置保留 `isSpecificationOnly` 或等价确认字段。
- [x] 4.2 响应或预览中暴露“项目由规格补齐”的提示信息。
- [x] 4.3 前端确认页展示仅规格导入风险提示。
- [x] 4.4 用户可在确认页改为手动选择项目列。

## 5. Tests
- [x] 5.1 API 测试：Excel 明确仅规格导入写入 `Project = Specification`。
- [x] 5.2 API 测试：Word 明确仅规格导入写入 `Project = Specification`。
- [x] 5.3 API/Core 测试：疑似项目列时不自动补项目。
- [x] 5.4 API 测试：重复检测按回填后的 `规格 + 规格` 命中。
- [x] 5.5 前端回归测试：确认页提示项目由规格补齐。

## 6. 2026-07-10 回归收口
- [x] 6.1 `NeedConfirm + IsSpecificationOnly` 不默认参与导入，用户显式确认后才可进入仅规格导入配置。
- [x] 6.2 自动仅规格候选拦截未映射且存在样本数据的疑似项目列。
- [x] 6.3 增加 API 与前端回归测试并完成验证。
