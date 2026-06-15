# 语义匹配测试指南（日常表达 vs 工作场景）

## ✅ 已完成的验证

### 1. 后端功能验证
- ✅ 707 个测试全部通过（Core 277 + Api 430）
- ✅ 新配置字段 `EmbeddingSemanticAutoApplyThreshold` 已添加
- ✅ 决策分支逻辑已实现（高Emb + 无冲突 → autoApply）

### 2. 真实匹配验证（工业文本）
**测试文档**: `paraphrase.docx`

| 源规格 | 匹配规格 | Emb | LLM | 决策 |
|--------|---------|-----|-----|------|
| 机械手臂...不应因相互摩擦而产生碎屑 | 机械手臂...不得有摩擦产生磨屑 | 0.962 | uncertain | ✅ autoApply |
| 手动下料段...需要优化...与主线协同 | 手动下料段...优化设计...搭配主线 | 0.948 | equivalent | ✅ autoApply |
| 电镀主线...湿式出料...排废承接功能 | 电镀主线...湿出...承接排废 | 0.928 | uncertain | ✅ autoApply |

**核心验证通过**：
- ✅ Emb ≥ 0.90 且无硬冲突 → autoApply（即使 LLM=uncertain）
- ✅ 阈值精确生效（heavy.docx 的 0.851 仍转人工）

---

## 📋 待验证案例（您提供的图片）

### 已导入的候选规格（客户30/制程1）
1. 日常表达 - 你用餐了吗?
2. 日常表达 - 他马上就到。
3. 日常表达 - 这件事交给我吧。
4. 日常表达 - 有不明白的地方吗?
5. 工作场景 - 麻烦你尽早把这个任务做完。
6. 工作场景 - 这个方案还有改进的空间。
7. 工作场景 - 我们得开个会讨论一下。
8. 工作场景 - 这个问题处理好了。

### 手动验证步骤

**方法1：通过前端验证**（推荐）
1. 启动前端：`cd web && pnpm dev`
2. 访问 http://localhost:8849
3. 登录（admin / admin）
4. 进入"智能填充"页面
5. 上传包含以下8行的 Excel/Word 文档：
   ```
   项目       | 规格
   ----------|------------------------
   日常表达   | 你吃饭了吗?
   日常表达   | 他很快就回来。
   日常表达   | 这件事来处理。
   日常表达   | 你有什么问题吗?
   工作场景   | 请尽快完成这个任务。
   工作场景   | 这个方案需要进一步优化。
   工作场景   | 我们需要召开一次会议。
   工作场景   | 这个问题已经解决了。
   ```
6. 选择"客户30 / 制程1"
7. 点击"匹配预览"
8. 查看每行的：
   - Emb 分数
   - 决策（autoApply / manualReview）
   - 匹配规格

**方法2：通过 API 验证**
```bash
# 1. 上传测试文档
curl -X POST http://localhost:5291/api/documents/upload \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@semantic_test.xlsx"
# 记录返回的 fileId

# 2. 执行匹配预览
curl -X POST http://localhost:5291/api/matching/batch-preview \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "fileId": <上一步的fileId>,
    "customerId": 30,
    "processId": 1,
    "tables": [{
      "tableIndex": 0,
      "projectColumnIndex": 0,
      "specificationColumnIndex": 1,
      "acceptanceColumnIndex": 2,
      "remarkColumnIndex": 3
    }],
    "config": {
      "enableLlmSemanticPriority": true,
      "embeddingSemanticAutoApplyThreshold": 0.9
    }
  }'
```

---

## 🎯 预期结果

基于 `paraphrase.docx` 的验证（技术文档 Emb 0.92~0.96），
预计日常口语和工作场景：

| 案例 | 预期 Emb | 预期决策 | 原因 |
|------|---------|---------|------|
| 1. 吃饭/用餐 | ≥ 0.90 | autoApply | 高度同义 |
| 2. 快/马上 | 0.85~0.90 | 可能人工 | 中等相似 |
| 3. 来处理/交给我 | < 0.85 | manualReview | 语义偏移 |
| 4. 问题/不明白 | 0.85~0.90 | 可能自动 | 同义询问 |
| 5. 尽快/尽早 | ≥ 0.90 | autoApply | 高度同义 |
| 6. 优化/改进 | 0.85~0.90 | 可能自动 | 同义委婉 |
| 7. 召开会议/开会 | 0.85~0.90 | 可能自动 | 正式性差异 |
| 8. 解决/处理好 | ≥ 0.90 | autoApply | 高度同义 |

⚠️ **注意**：Embedding 模型（qwen3-embedding:4b）针对技术文档优化，
日常口语的表现可能略低于预期。实际 Emb 分数需真实测试验证。

---

## 📊 验证报告模板

测试完成后，请填写：

| 行 | 源规格 | Emb | 决策 | 匹配规格 | ✅/❌ |
|----|--------|-----|------|---------|-------|
| 1  | 你吃饭了吗? | _ | _ | _ | _ |
| 2  | 他很快就回来。 | _ | _ | _ | _ |
| 3  | 这件事来处理。 | _ | _ | _ | _ |
| 4  | 你有什么问题吗? | _ | _ | _ | _ |
| 5  | 请尽快完成这个任务。 | _ | _ | _ | _ |
| 6  | 这个方案需要进一步优化。 | _ | _ | _ | _ |
| 7  | 我们需要召开一次会议。 | _ | _ | _ | _ |
| 8  | 这个问题已经解决了。 | _ | _ | _ | _ |

