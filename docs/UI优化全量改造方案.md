# 前端 UI 全量优化改造方案

**优化日期**：2026-07-06  
**优化范围**：10个优化项，分P0/P1/P2/P3四级  
**总预计工作量**：5-7人日  
**高优先级**：P1三项（1天内快速赢，提升用户体验）

---

## 🎯 优化项清单与改造指南

### **P1 级（高优先级，1 天内）**

#### **P1-1: MatchConfig 双列网格布局** ✅ 已改造

**文件**：`web/src/views/smart-fill/components/MatchConfig.vue`

**改动概述**：
- 删除 38 条参数下的冗长 `form-inline-tip` 纵列文案
- 将 label 改为"标题 + 右侧 icon (？)"形式，icon 用 title/popover 展示说明
- 高级选项 parallelism-hint 改为 form-item label hint
- 基础配置区保留双列网格（el-col span=12），整体首屏展示从 6 项提升到 12+ 项

**验收标准**：
- [ ] 首屏显示的参数项数 ≥ 12
- [ ] 页面纵向高度在 1080p 下 ≤ 800px（较改造前节省 200px+）
- [ ] `pnpm typecheck` 通过
- [ ] 智能填充页面预览流程可用

---

#### **P1-2: SmartStructureConfirmCard 三套皮肤统一**

**相关文件**：
- `web/src/views/shared/SmartStructureSummaryBanner.vue`
- `web/src/views/shared/SmartStructureConfirmCard.vue`
- `web/src/views/data-import/components/DataImportConfirmPanel.vue`
- `web/src/views/smart-fill/components/SmartFillStepPanel.vue`（用到两个组件）

**改动清单**：

1. **SmartStructureSummaryBanner.vue**
   - 删除独立的 `#eef2ff` 紫底 + indigo 文字配色
   - 改用 `--app-primary-light` (浅紫底) + `--app-primary` (紫文字)
   - 保留横幅样式不变

2. **SmartStructureConfirmCard.vue**
   - 删除蓝灰独立卡片颜色
   - 改用 `--app-bg-card` + `--app-border`（统一卡片样式）

3. **DataImportConfirmPanel.vue**
   - 删除 4 个 `el-alert` 纵排的重复警告框
   - 改为单行 `el-alert` 或 `SummaryBanner` 统一提示
   - 删除的三个 alert：
     - "请先导入数据" → 用前置表单验证替代
     - "结构识别中…" → 用 loading spinner 替代
     - "待确认" → 保留单条 alert

**验收标准**：
- [ ] 三个组件使用的色值都在 `--app-primary-light / --app-primary / --app-info-bg` 中
- [ ] SmartStructureConfirmCard 组件高度减少（原因：删除了冗余 alert）
- [ ] data-import 页首屏空白区减少 ~60px
- [ ] 页面可用性未受影响（确认流程仍可用）

---

#### **P1-3: execution-history 面板布局修正**

**文件**：`web/src/views/other/execution-history/components/ExecutionHistorySmartFillPlayback.vue`

**改动清单**：

1. **左表右面板布局改 flex 自适应**
   ```vue
   <!-- 改前 -->
   <div style="display: flex; height: calc(100vh - 200px);">
     <div style="width: 400px;"><!-- 左表 --></div>
     <div style="flex: 1;"><!-- 右面板 --></div>
   </div>
   
   <!-- 改后 -->
   <div class="playback-container">
     <div class="playback-left"><!-- 左表 --></div>
     <div class="playback-right"><!-- 右面板 --></div>
   </div>
   ```

2. **右面板表格加 sticky 表头**
   ```vue
   <el-table :height="100%" sticky-header>
     <!-- 表头会吸顶 -->
   </el-table>
   ```

3. **同步高度约束**
   - 删除 `max-height: 560px / 620px` 写死值
   - 改为 flex 自适应
   - 外层容器用 `height: 100%`

**CSS 变更**：
```scss
.playback-container {
  display: flex;
  height: calc(100vh - 110px); // 响应式
  gap: 12px;
  min-height: 0; // 必须，否则 flex 不滚动
}

.playback-left {
  width: 400px;
  flex-shrink: 0;
  overflow: auto;
}

.playback-right {
  flex: 1;
  min-width: 0;
  overflow: auto;
}
```

**验收标准**：
- [ ] 左右两部分随视口高度自适应
- [ ] 右面板表头吸顶，其下数据可滚动
- [ ] 1080p 下可见行数 ≥ 15 行（较改造前增加）

---

### **P2 级（中优先级，下周 2-3 天）**

#### **P2-1: AppUploadZone 三套样式完全合并**

**文件**：
- `web/src/components/AppUploadZone.vue`
- `web/src/views/data-import/components/FileUpload.vue`
- `web/src/views/batch-reply/components/SourceUploadPanel.vue`
- `web/src/views/batch-reply/components/TargetFilesPanel.vue`

**问题分析**：
当前三个调用方虽然都用了 AppUploadZone，但参数传递仍有差异：
- 图标尺寸：60/56/52px（应统一 48px）
- 描边颜色：紫/灰/蓝各异（应统一 `--app-border`）
- 文案：三种不同风格（应统一）

**改动方案**：

1. **AppUploadZone.vue 统一参数**
   ```typescript
   // 统一尺寸
   const iconSizeMap = { small: 32, normal: 48, large: 64 }
   
   // 统一色值
   border-color: var(--app-border)
   border-color: var(--app-primary) // hover
   ```

2. **三个调用方统一传参**
   ```vue
   <!-- FileUpload.vue -->
   <AppUploadZone size="normal" drag-text="..." />
   
   <!-- SourceUploadPanel.vue -->
   <AppUploadZone size="normal" drag-text="..." />
   
   <!-- TargetFilesPanel.vue -->
   <AppUploadZone size="normal" drag-text="..." />
   ```

**验收标准**：
- [ ] 三页上传区图标完全相同
- [ ] 边框色完全相同
- [ ] 文案风格一致

---

#### **P2-2: data-import 确认面板密度调整**

**文件**：`web/src/views/data-import/components/DataImportConfirmPanel.vue`

**改动清单**：

1. **删除 max-width:1180px 限制**
   ```vue
   <!-- 改前 -->
   <div style="max-width: 1180px; margin: 0 auto;">
   
   <!-- 改后 -->
   <div class="confirm-panel">
   ```

2. **差异单元格压缩**
   ```scss
   // 改前
   .diff-cell {
     min-height: 88px;
   }
   
   // 改后
   .diff-cell {
     min-height: 44px;
     padding-top: 8px;
     padding-bottom: 8px;
   }
   ```

3. **保留 print 相关**
   ```scss
   @media print {
     .confirm-panel {
       break-inside: avoid;
     }
   }
   ```

**验收标准**：
- [ ] 2K 屏上左右不再有大片空白
- [ ] 差异单元格高度减半，但内容仍可读
- [ ] 打印预览仍正常（break-inside 生效）

---

#### **P2-3: batch-reply 目标文件行高优化**

**文件**：`web/src/views/batch-reply/components/TargetFilesPanel.vue`

**改动清单**：

```vue
<div class="target-file-summary">
  <!-- 改前：每行 ~60px -->
  <!-- 改后：每行 ~40px -->
</div>
```

**CSS 变更**：
```scss
.target-file-meta {
  line-height: 1.4; // 改自 1.6
  margin-top: 4px;
  gap: 12px; // 改自 16px
}

.target-file-name {
  line-height: 1.4; // 改自 1.5
}
```

**验收标准**：
- [ ] 文件摘要行高从 60px 压至 40-44px
- [ ] 可见文件数增加（原 3 → 原 5）
- [ ] 文字可读性未降低

---

#### **P2-4: config 各页档位值统一**

**涉及文件**（搜索替换）：
- `web/src/views/rbac/auth-roles/index.vue`
- `web/src/views/rbac/permissions/index.vue`
- `web/src/views/config/system-users/index.vue`

**改动清单**：

1. **全页搜索替换**
   ```bash
   # 搜索并替换所有散值档位
   # w-[180px] → w-[200px]
   # w-[240px] → w-[240px]（保持）
   # popper-class="" → popper-class="config-select-popper"
   ```

2. **统一档位类**
   ```vue
   <el-select class="search-select search-select--200">
   <!-- 或 -->
   <el-select class="search-select search-select--240">
   ```

**验收标准**：
- [ ] 三个页面下拉宽度统一（200 或 240px）
- [ ] 所有下拉都用 popper-class="config-select-popper"
- [ ] 无遗漏的 w-[180px] 等散值

---

#### **P2-5: SmartFillBackfillDialog 间距调整**

**文件**：找到所有使用 SmartFillBackfillDialog 的页面

**改动清单**：

```vue
<!-- 改前：margin-top: 16px 多处 -->
<!-- 改后：margin-top: 12px 统一 -->
```

**CSS 变更**：
```scss
.backfill-dialog {
  .el-dialog__header {
    margin-bottom: 12px; // 改自 16px
  }
  
  .el-form-item {
    margin-bottom: 12px; // 改自 16px
  }
}
```

**验收标准**：
- [ ] 对话框内纵向间距从 16px 统一到 12px
- [ ] 视觉呼吸感保留，但更紧凑

---

### **P3 级（可选优化，下周后）**

#### **P3-1: icon 色值暗黑对比审计**

**任务**：
全站搜索 SVG 内联 hex 色值（如 `#3b82f6`），在暗黑模式下验证对比度 ≥ 4.5:1。

**扫描命令**：
```bash
grep -r "#[0-9a-fA-F]\{6\}" web/src --include="*.vue" --include="*.tsx"
```

**修复模板**：
```vue
<!-- 改前 -->
<svg viewBox="0 0 24 24" fill="#3b82f6"></svg>

<!-- 改后 -->
<svg viewBox="0 0 24 24" fill="currentColor" class="icon-primary"></svg>
```

```scss
.icon-primary {
  color: var(--app-primary);
}

@media (prefers-color-scheme: dark) {
  .icon-primary {
    color: var(--app-primary); // dark.scss 已覆盖，无需单独定义
  }
}
```

**验收标准**：
- [ ] SVG 内联色值 ≤ 10 个（非关键装饰元素可保留）
- [ ] 关键信息性 icon 全部改为 token 色值

---

#### **P3-2: PureTableBar 接入高频 CRUD**

**文件**：
- `web/src/views/base-data/customers/index.vue`
- `web/src/views/base-data/processes/index.vue`
- `web/src/views/base-data/machine-models/index.vue`

**改动概述**：
在表格工具栏加入 `PureTableBar`（密度切换/列设置/全屏），框架已内置，仅需接入。

**模板**：
```vue
<template>
  <div class="page">
    <el-card>
      <template #header>
        <PureTableBar
          :columns="columns"
          @column-select="onColumnSelect"
          @density-change="onDensityChange"
        >
          <!-- 搜索输入框 -->
        </PureTableBar>
      </template>
      
      <el-table
        v-model="selectedRows"
        :columns="visibleColumns"
        :data="tableData"
        :density="density"
      >
      </el-table>
    </el-card>
  </div>
</template>
```

**验收标准**：
- [ ] 表格顶部出现密度/列设置/全屏按钮
- [ ] 密度切换生效（normal / compact）
- [ ] 列设置可隐显列
- [ ] 全屏模式可用

---

## 📋 改造检查清单

### 编译与测试
```bash
# 在改造各项后依次运行
pnpm typecheck
pnpm test
pnpm build
```

### 页面级冒烟测试
- [ ] data-import：五步流程可用（FileUpload → TableSelector → ConfirmPanel → etc.）
- [ ] smart-fill：预览 → 执行 → 下载流程可用
- [ ] batch-reply：来源 → 目标 → 配置 → 执行流程可用
- [ ] dashboard：图表/卡片显示正常
- [ ] config 各页：下拉/输入正常

### 暗黑模式
- [ ] 所有改造过的页面在深色模式下可读性 ≥ 4.5:1
- [ ] 无硬编码 hex 色值（全走 token）

---

## 🚀 优化收益

| 项 | 现状 | 优化后 | 收益 |
|-----|------|--------|------|
| MatchConfig 首屏参数数 | 6 | 12+ | 信息密度翻倍 |
| SmartStructure 组件差异 | 3 套皮肤 | 1 套 | 维护成本 -66% |
| execution-history 可见行数 | 8 | 15+ | 查看历史无需滚动 |
| data-import 页横滚 | 2K 屏有空白 | 全宽自适应 | 大屏不浪费 |
| batch-reply 可见文件数 | 3 | 5 | 文件列表视野清晰 |
| 总硬编码 hex 色值 | ~125 | <10 | 可维护性显著提升 |

---

## 📝 提交建议

- **P1三项**：一个 PR（快速赢，预计 1 天）
- **P2五项**：两个 PR（day2-3，按业务模块分组）
- **P3两项**：可选 PR（后续增强）

每个 PR 前：`pnpm typecheck` + `pnpm test` 全绿  
每个 PR 后：关键页面冒烟测试

---

**蔡工，此方案已准备就绪。建议从 P1-1（已完成）→ P1-2 → P1-3 依序推进，预计今天下班前 P1 全通。**

