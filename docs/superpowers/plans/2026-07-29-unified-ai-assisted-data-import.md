# Unified AI-Assisted Data Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 Excel 数据导入的智能识别和手动行列配置合并为一个支持多区域编辑的统一配置工作台。

**Architecture:** 继续以 `TableImportConfig.recognizedExcelMappings` 作为当前 Excel 区域列表，所有 AI、行列和 A1 修改通过区域操作函数写入；`excelMapping` 只作为活动区域的兼容投影。上传后统一进入结构配置步骤，AI 失败时创建默认区域；配置通过后复用现有多区域预览、差异处理和批量导入。

**Tech Stack:** Vue 3.5、TypeScript 5.9、Element Plus 2.11、Vitest 4、Node test runner、OpenSpec

## Global Constraints

- 仅修改数据导入中的 Excel 流程；Word 和智能填充保持现有行为。
- 不修改后端接口、数据库结构，不新增依赖。
- Excel 真实行号和绝对列号均为 1-based；界面列显示使用 Excel 字母。
- 当前工作区已有未提交修改，所有提交必须显式列出文件，禁止覆盖或回退现有改动。
- `recognizedExcelMappings` 是最终预览和导入的数据来源；`excelMapping` 不得成为并行真相。
- AI 失败、部分识别和重新识别失败均不得丢失可继续使用的人工配置。
- 每个行为变更先观察定向测试失败，再实现最小改动使其通过。
- OpenSpec 验证必须从 `D:\Temp\AcceptanceSpecificationSystem` 根目录执行。

---

### Task 1: 将 OpenSpec 收口为统一入口和统一配置

**Files:**

- Modify: `openspec/changes/add-excel-header-range-selection/proposal.md`
- Modify: `openspec/changes/add-excel-header-range-selection/specs/user-interface/spec.md`
- Modify: `openspec/changes/add-excel-header-range-selection/tasks.md`

**Interfaces:**

- Consumes: 已确认设计 `docs/superpowers/specs/2026-07-29-unified-ai-assisted-data-import-design.md`
- Produces: OpenSpec 规范“单入口 → Excel 结构配置 → 预览导入”，供后续任务作为验收基线

- [ ] **Step 1: 修改提案中的行为定义**

将提案中的“两种同权重独立入口”替换为以下行为：

```markdown
- 数据导入上传页只保留“分析并配置”入口。
- Excel 智能识别结果直接预填统一结构配置工作台，用户可继续修改工作表、区域、表头行、数据行和字段列。
- AI 不可用或识别失败时创建默认区域并继续进入结构配置。
- 行列配置与 A1 坐标编辑回写同一个区域列表。
```

- [ ] **Step 2: 重写用户界面增量规范中的入口场景**

规范至少包含以下场景，并删除“智能识别导入”和“手动配置导入”并列展示的断言：

```markdown
#### Scenario: Excel 通过单入口进入统一结构配置
- **GIVEN** 用户已上传 Excel 并选择客户
- **WHEN** 用户点击“分析并配置”
- **THEN** 系统执行规则识别和可用的 AI 辅助
- **AND** 系统进入可编辑的多区域结构配置工作台
- **AND** 智能识别结果成为工作台的初始配置

#### Scenario: AI 失败后继续手动配置
- **GIVEN** 当前文件为 Excel
- **WHEN** 智能识别不可用、失败或未识别出可用区域
- **THEN** 系统为可导入工作表创建默认区域
- **AND** 系统提示用户已进入手动配置
- **AND** 用户仍可完成行号、列映射、预览和导入
```

- [ ] **Step 3: 重写任务清单**

任务清单必须覆盖区域状态、表头列标题、A1 双向同步、统一步骤、工作台 UI、失败降级和定向验证；在对应代码完成前保持未勾选。

- [ ] **Step 4: 严格验证 OpenSpec**

Run:

```powershell
openspec validate add-excel-header-range-selection --strict
```

Expected: `add-excel-header-range-selection` 验证通过，退出码为 `0`。

- [ ] **Step 5: 提交规范变更**

```powershell
git add -- openspec/changes/add-excel-header-range-selection/proposal.md openspec/changes/add-excel-header-range-selection/specs/user-interface/spec.md openspec/changes/add-excel-header-range-selection/tasks.md
git diff --cached --check
git commit -m "docs: 统一 Excel 导入配置流程"
```

---

### Task 2: 建立可测试的多区域编辑状态

**Files:**

- Modify: `web/src/views/data-import/dataImport.types.ts`
- Modify: `web/src/views/data-import/dataImport.regions.ts`
- Modify: `web/src/views/data-import/dataImport.regions.test.ts`

**Interfaces:**

- Consumes: `ExcelRegionMapping`、`ExcelSheetMapping`、`TableInfo`
- Produces:
  - `cloneExcelRegionMappings(regions): ExcelRegionMapping[]`
  - `createManualExcelRegionMapping(input): ExcelRegionMapping`
  - `appendExcelRegionMapping(regions, region): ExcelRegionMapping[]`
  - `copyExcelRegionMapping(input): ExcelRegionMapping[]`
  - `removeExcelRegionMapping(input): { regions; activeRegionId }`
  - `getActiveExcelRegionMapping(regions, activeRegionId): ExcelRegionMapping | undefined`
  - `areExcelRegionMappingsEqual(left, right): boolean`
  - `replaceExcelRegionMapping(input): ExcelRegionMapping[]`

- [ ] **Step 1: 写区域增删复制和快照隔离的失败测试**

在 `dataImport.regions.test.ts` 增加：

```ts
it("人工区域使用调用方提供的稳定 regionId 并归一化序号", () => {
  const region = createManualExcelRegionMapping({
    tableIndex: 2,
    tableInfo: {
      index: 2,
      name: "Sheet3",
      rowCount: 20,
      columnCount: 8,
      isNested: false,
      previewText: "",
      headers: [],
      hasMergedCells: false,
      usedRangeStartRow: 6,
      usedRangeStartColumn: 2
    },
    regionId: "manual-2-a"
  });

  expect(region).toMatchObject({
    regionId: "manual-2-a",
    regionIndex: 0,
    headerRowStart: 6,
    dataStartRow: 7,
    dataEndRow: 25
  });
});

it("删除活动区域后选择相邻区域且至少保留一个区域", () => {
  const first = mapping(0, 9, 112);
  const second = mapping(1, 128, 143);

  expect(
    removeExcelRegionMapping({
      regions: [first, second],
      regionId: first.regionId,
      activeRegionId: first.regionId
    })
  ).toEqual({
    regions: [{ ...second, regionIndex: 0 }],
    activeRegionId: second.regionId
  });

  expect(
    removeExcelRegionMapping({
      regions: [first],
      regionId: first.regionId,
      activeRegionId: first.regionId
    })
  ).toEqual({ regions: [first], activeRegionId: first.regionId });
});

it("AI 快照深拷贝后不受人工修改污染", () => {
  const source = [mapping(0, 9, 112)];
  const snapshot = cloneExcelRegionMappings(source);
  source[0].dataEndRow = 120;
  expect(snapshot[0].dataEndRow).toBe(112);
});

it("复制区域使用新 ID 并追加为连续序号", () => {
  const first = mapping(0, 9, 112);
  expect(
    copyExcelRegionMapping({
      regions: [first],
      sourceRegionId: first.regionId,
      newRegionId: "manual-copy-1"
    })
  ).toEqual([
    first,
    {
      ...first,
      regionId: "manual-copy-1",
      regionIndex: 1
    }
  ]);
});

it("区域比较能识别人工行列修改", () => {
  const source = [mapping(0, 9, 112)];
  expect(areExcelRegionMappingsEqual(source, cloneExcelRegionMappings(source)))
    .toBe(true);
  expect(
    areExcelRegionMappingsEqual(source, [
      { ...source[0], specificationColumn: 5 }
    ])
  ).toBe(false);
});
```

- [ ] **Step 2: 运行区域测试并确认失败**

Run:

```powershell
pnpm exec vitest run src/views/data-import/dataImport.regions.test.ts
```

Expected: FAIL，缺少新的区域操作函数。

- [ ] **Step 3: 扩充配置类型**

为 `TableImportConfig` 增加：

```ts
activeExcelRegionId?: string;
aiExcelMappingsSnapshot?: ExcelRegionMapping[];
```

保留现有 `excelMapping` 和 `recognizedExcelMappings` 字段；后续任务将前者限制为活动区域投影。

- [ ] **Step 4: 实现区域操作函数**

核心实现遵循：

```ts
const normalizeRegionIndexes = (regions: readonly ExcelRegionMapping[]) =>
  regions.map((region, regionIndex) => ({ ...region, regionIndex }));

export const cloneExcelRegionMappings = (
  regions: readonly ExcelRegionMapping[]
) => regions.map(region => ({ ...region }));

export const getActiveExcelRegionMapping = (
  regions: readonly ExcelRegionMapping[],
  activeRegionId?: string
) =>
  regions.find(region => region.regionId === activeRegionId) ?? regions[0];
```

`createManualExcelRegionMapping` 使用 `createDefaultExcelMapping(tableInfo)` 生成行范围，并将字段列保持为空；`copyExcelRegionMapping` 必须接收新的 `regionId`，不得复用源 ID。

`areExcelRegionMappingsEqual` 按 `regionId`、区域顺序和所有 `ExcelSheetMapping` 字段比较，用于判断恢复或重新识别是否会覆盖人工修改。

- [ ] **Step 5: 运行区域测试并确认通过**

Run:

```powershell
pnpm exec vitest run src/views/data-import/dataImport.regions.test.ts
```

Expected: 所有区域测试 PASS。

- [ ] **Step 6: 提交区域状态能力**

```powershell
git add -- web/src/views/data-import/dataImport.types.ts web/src/views/data-import/dataImport.regions.ts web/src/views/data-import/dataImport.regions.test.ts
git diff --cached --check
git commit -m "feat: 增加 Excel 多区域编辑状态"
```

---

### Task 3: 统一表头列标题和 A1 双向转换

**Files:**

- Create: `web/src/views/data-import/dataImport.a1.ts`
- Create: `web/src/views/data-import/dataImport.a1.test.ts`
- Modify: `web/src/views/data-import/dataImport.helpers.ts`
- Modify: `web/src/views/shared/smart-structure-recognition.ts`
- Modify: `web/src/views/shared/smart-structure-recognition.test.ts`
- Modify: `web/src/views/shared/SmartStructureRangeEditorDrawer.vue`
- Modify: `web/tests/data-import-excel-range.test.ts`

**Interfaces:**

- Consumes: `resolveSmartStructureExcelRangeMapping`、`toExcelColumnLabel`、`ExcelSheetMapping`、`TableInfo`
- Produces:
  - `formatExcelRegionA1Ranges(mapping): ExcelRegionA1Ranges`
  - `buildExcelColumnOptions(tableInfo, previewData): ExcelColumnOption[]`
  - 共享 A1 校验的 `requireAcceptance` 选项，默认值保持 `true`

- [ ] **Step 1: 写表头标签和 A1 往返失败测试**

在 `data-import-excel-range.test.ts` 将列标题期望改为：

```ts
assert.equal(options[1].label, "D 列 · 项目");
assert.equal(options[2].label, "E 列 · 规格要求");
```

增加空标题兜底：

```ts
assert.equal(
  buildExcelColumnOptions(
    {
      index: 0,
      name: "Sheet1",
      rowCount: 20,
      columnCount: 2,
      isNested: false,
      previewText: "",
      headers: [],
      hasMergedCells: false,
      usedRangeStartRow: 6,
      usedRangeStartColumn: 2
    },
    {
      tableIndex: 0,
      headers: ["", ""],
      rows: [],
      totalRows: 0,
      columnCount: 2
    }
  )[0].label,
  "B 列"
);
```

在 `dataImport.a1.test.ts` 增加：

```ts
it("把绝对列和真实行号格式化为 A1", () => {
  const mapping = {
    projectColumn: 4,
    specificationColumn: 5,
    acceptanceColumn: 6,
    headerRowStart: 6,
    headerRowCount: 1,
    dataStartRow: 7,
    dataEndRow: 242
  };

  const ranges = formatExcelRegionA1Ranges(mapping);
  expect(ranges).toEqual({
    projectRange: "D7:D242",
    specificationRange: "E7:E242",
    acceptanceRange: "F7:F242",
    remarkRange: ""
  });
});

it("数据导入策略允许验收范围留空", () => {
  const result = resolveSmartStructureExcelRangeMapping(
    {
      projectRange: "D7:D242",
      specificationRange: "E7:E242",
      acceptanceRange: "",
      remarkRange: ""
    },
    { baseColumn: 2, columnCount: 8, baseRow: 6, maximumRow: 242 },
    { requireAcceptance: false }
  );

  expect(result.fieldErrors.acceptanceRange).toBeUndefined();
  expect(result.specificationColumnIndex).toBe(3);
});
```

- [ ] **Step 2: 运行定向测试并确认失败**

Run:

```powershell
pnpm exec vitest run src/views/data-import/dataImport.a1.test.ts src/views/shared/smart-structure-recognition.test.ts
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/data-import-excel-range.test.ts
```

Expected: FAIL，A1 适配器不存在且列标签仍使用“第 N 列”格式。

- [ ] **Step 3: 让共享 A1 校验支持可选验收列**

给共享函数增加默认保持兼容的选项：

```ts
export type SmartStructureExcelRangeRequirements = {
  requireAcceptance?: boolean;
};

export const validateSmartStructureExcelRanges = (
  ranges: Record<SmartStructureExcelRangeField, string>,
  bounds: {
    baseColumn: number;
    columnCount: number;
    baseRow: number;
    maximumRow: number;
  },
  requirements: SmartStructureExcelRangeRequirements = {
    requireAcceptance: true
  }
) => {
  // acceptanceRange.required 使用 requirements.requireAcceptance !== false
};
```

`resolveSmartStructureExcelRangeMapping` 接受并透传同一选项。现有智能填充调用不传选项，行为保持验收列必填；数据导入传 `{ requireAcceptance: false }`。

- [ ] **Step 4: 实现 A1 摘要格式化**

`dataImport.a1.ts` 使用绝对列号生成字段范围：

```ts
export type ExcelRegionA1Ranges = {
  projectRange: string;
  specificationRange: string;
  acceptanceRange: string;
  remarkRange: string;
};

const formatFieldRange = (
  column: number | undefined,
  dataStartRow: number,
  dataEndRow: number
) =>
  column == null
    ? ""
    : `${toExcelColumnLabel(column)}${dataStartRow}:${toExcelColumnLabel(column)}${dataEndRow}`;

export const formatExcelRegionA1Ranges = (
  mapping: ExcelSheetMapping
): ExcelRegionA1Ranges => ({
  projectRange: formatFieldRange(
    mapping.projectColumn,
    mapping.dataStartRow,
    mapping.dataEndRow
  ),
  specificationRange: formatFieldRange(
    mapping.specificationColumn,
    mapping.dataStartRow,
    mapping.dataEndRow
  ),
  acceptanceRange: formatFieldRange(
    mapping.acceptanceColumn,
    mapping.dataStartRow,
    mapping.dataEndRow
  ),
  remarkRange: formatFieldRange(
    mapping.remarkColumn,
    mapping.dataStartRow,
    mapping.dataEndRow
  )
});
```

- [ ] **Step 5: 让共享抽屉透传验收列要求**

`SmartStructureRangeEditorDrawer.vue` 增加默认兼容的 prop：

```ts
requireAcceptanceRange?: boolean;
```

默认值为 `true`，所有校验和保存前解析都传：

```ts
{ requireAcceptance: props.requireAcceptanceRange }
```

数据导入包装组件在 Task 6 传 `false`，智能填充和现有确认卡不传该 prop，行为保持不变。

- [ ] **Step 6: 更新列选项显示**

`buildExcelColumnOptions` 使用：

```ts
label: header ? `${letter} 列 · ${header}` : `${letter} 列`
```

标题只来自当前 `previewData.headers`；不得回退到上传时 `tableInfo.headers`。

- [ ] **Step 7: 运行定向测试并确认通过**

Run:

```powershell
pnpm exec vitest run src/views/data-import/dataImport.a1.test.ts src/views/shared/smart-structure-recognition.test.ts
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/data-import-excel-range.test.ts
```

Expected: 所有定向测试 PASS。

- [ ] **Step 8: 提交 A1 和列标题能力**

```powershell
git add -- web/src/views/data-import/dataImport.a1.ts web/src/views/data-import/dataImport.a1.test.ts web/src/views/data-import/dataImport.helpers.ts web/src/views/shared/smart-structure-recognition.ts web/src/views/shared/smart-structure-recognition.test.ts web/src/views/shared/SmartStructureRangeEditorDrawer.vue web/tests/data-import-excel-range.test.ts
git diff --cached --check
git commit -m "feat: 统一 Excel 行列与 A1 映射"
```

---

### Task 4: 将 AI 结果和手动默认区域汇入同一配置

**Files:**

- Modify: `web/src/views/data-import/dataImport.smartRecognition.ts`
- Modify: `web/src/views/data-import/dataImport.smartRecognition.test.ts`
- Modify: `web/src/views/data-import/composables/useDataImportSmartStructureRecognition.ts`
- Modify: `web/src/views/data-import/composables/useDataImportSmartStructureRecognition.test.ts`

**Interfaces:**

- Consumes: Task 2 的区域创建、克隆和活动区域函数
- Produces:
  - `buildDataImportConfigsFromRecognizedTables(...)` 生成当前区域、AI 快照和活动区域
  - `buildManualDataImportConfig(...)` 为 Excel 创建一个规范化区域
  - `restoreExcelAiSnapshot(config): TableImportConfig`
  - `buildDataImportRangeEditorTable(input): SmartConfigRecognizedTable`
  - `mapRangeEditorRegionsToExcel(input): ExcelRegionMapping[]`

- [ ] **Step 1: 写识别结果、人工降级和快照恢复失败测试**

在 `dataImport.smartRecognition.test.ts` 增加：

```ts
const tableInfoFromB6 = {
  index: 0,
  name: "Sheet1",
  rowCount: 237,
  columnCount: 8,
  isNested: false,
  previewText: "",
  headers: [],
  hasMergedCells: false,
  usedRangeStartRow: 6,
  usedRangeStartColumn: 2
};

const configWithRegionDToF: TableImportConfig = {
  tableIndex: 0,
  tableInfo: tableInfoFromB6,
  previewData: null,
  activeExcelRegionId: "sheet-0-region-0",
  recognizedExcelMappings: [
    {
      regionId: "sheet-0-region-0",
      regionIndex: 0,
      isSpecificationOnly: false,
      projectColumn: 4,
      specificationColumn: 5,
      acceptanceColumn: 6,
      headerRowStart: 6,
      headerRowCount: 1,
      dataStartRow: 7,
      dataEndRow: 242
    }
  ]
};

const recognizedTableWithTwoRegions: SmartConfigRecognizedTable = {
  tableIndex: 0,
  tableName: "Sheet1",
  headers: [],
  headerRowIndex: 0,
  headerRowCount: 1,
  dataStartRowIndex: 1,
  projectColumnIndex: 2,
  specificationColumnIndex: 3,
  acceptanceColumnIndex: 4,
  isSpecificationOnly: false,
  confidence: 0.9,
  source: "Fused",
  decision: "NeedConfirm",
  recommendation: "Recommended",
  fields: [],
  regions: [
    {
      regionId: "sheet-0-region-0",
      regionIndex: 0,
      headers: [],
      headerRowIndex: 0,
      headerRowCount: 1,
      dataStartRowIndex: 1,
      dataEndRowIndex: 100,
      projectColumnIndex: 2,
      specificationColumnIndex: 3,
      acceptanceColumnIndex: 4,
      isSpecificationOnly: false,
      confidence: 0.9,
      source: "Fused",
      decision: "NeedConfirm",
      fields: []
    },
    {
      regionId: "sheet-0-region-1",
      regionIndex: 1,
      headers: [],
      headerRowIndex: 120,
      headerRowCount: 1,
      dataStartRowIndex: 121,
      dataEndRowIndex: 150,
      projectColumnIndex: 2,
      specificationColumnIndex: 3,
      acceptanceColumnIndex: 4,
      isSpecificationOnly: false,
      confidence: 0.8,
      source: "Fused",
      decision: "NeedConfirm",
      fields: []
    }
  ]
};

it("AI 多区域结果同时成为当前配置和隔离快照", () => {
  const [config] = buildDataImportConfigsFromRecognizedTables({
    isExcelFile: true,
    tables: [recognizedTableWithTwoRegions],
    tableInfos: [tableInfoFromB6]
  });
  expect(config.recognizedExcelMappings).toHaveLength(2);
  expect(config.aiExcelMappingsSnapshot).toEqual(
    config.recognizedExcelMappings
  );
  expect(config.aiExcelMappingsSnapshot).not.toBe(
    config.recognizedExcelMappings
  );
  expect(config.activeExcelRegionId).toBe(
    config.recognizedExcelMappings?.[0].regionId
  );
});

it("Excel 手动降级配置也创建一个可编辑区域", () => {
  const config = buildManualDataImportConfig({
    isExcelFile: true,
    tableInfo,
    regionId: "manual-sheet-0"
  });
  expect(config.recognizedExcelMappings).toHaveLength(1);
  expect(config.activeExcelRegionId).toBe("manual-sheet-0");
  expect(config.aiExcelMappingsSnapshot).toBeUndefined();
});

it("共享 A1 抽屉适配往返后保留绝对列号和区域 ID", () => {
  const editorTable = buildDataImportRangeEditorTable({
    config: configWithRegionDToF,
    tableInfo: tableInfoFromB6
  });

  const mappings = mapRangeEditorRegionsToExcel({
    tableInfo: tableInfoFromB6,
    regions: editorTable.regions ?? []
  });

  expect(mappings[0]).toMatchObject({
    regionId: configWithRegionDToF.recognizedExcelMappings?.[0].regionId,
    projectColumn: 4,
    specificationColumn: 5,
    acceptanceColumn: 6,
    dataStartRow: 7,
    dataEndRow: 242
  });
});
```

- [ ] **Step 2: 运行识别测试并确认失败**

Run:

```powershell
pnpm exec vitest run src/views/data-import/dataImport.smartRecognition.test.ts src/views/data-import/composables/useDataImportSmartStructureRecognition.test.ts
```

Expected: FAIL，缺少快照、活动区域和手动区域。

- [ ] **Step 3: 改造智能配置投影**

`buildDataImportConfigsFromRecognizedTables` 对 Excel 返回：

```ts
const currentRegions = cloneExcelRegionMappings(excelMappings);
return {
  ...base,
  recognizedExcelMappings: currentRegions,
  aiExcelMappingsSnapshot: cloneExcelRegionMappings(currentRegions),
  activeExcelRegionId: currentRegions[0]?.regionId,
  excelMapping: currentRegions[0]
};
```

`buildManualDataImportConfig` 增加必需的 `regionId` 参数，并使用 `createManualExcelRegionMapping`。

- [ ] **Step 4: 实现恢复 AI 快照**

恢复函数只在快照非空时替换当前工作表区域：

```ts
export const restoreExcelAiSnapshot = (
  config: TableImportConfig
): TableImportConfig => {
  const regions = cloneExcelRegionMappings(
    config.aiExcelMappingsSnapshot ?? []
  );
  if (regions.length === 0) return config;
  return {
    ...config,
    recognizedExcelMappings: regions,
    activeExcelRegionId: regions[0].regionId,
    excelMapping: regions[0],
    previewData: null,
    excelPreviewRowLocations: undefined
  };
};
```

- [ ] **Step 5: 提取共享 A1 抽屉需要的双向适配器**

`buildDataImportRangeEditorTable` 将绝对行列转换为 `SmartStructureRangeEditorDrawer.vue` 使用的相对索引；手动降级区域没有 AI 元数据时使用：

```ts
{
  confidence: 0,
  source: "Rule",
  decision: "NeedConfirm",
  fields: [],
  issues: []
}
```

`mapRangeEditorRegionsToExcel` 使用 `toActualColumnNumber` 和 `toActualRowNumber` 转回 `ExcelRegionMapping[]`，保留每个 `regionId`，并重新生成连续的 `regionIndex`。该函数只返回当前区域，不修改 `aiExcelMappingsSnapshot`。

- [ ] **Step 6: 让识别应用失败保留现有配置**

`useDataImportSmartStructureRecognition` 重新识别当前工作表时先生成局部结果，只有识别和投影全部成功后才替换对应 `TableImportConfig`；catch 分支只更新错误提示，不清空现有区域。

- [ ] **Step 7: 运行识别测试并确认通过**

Run:

```powershell
pnpm exec vitest run src/views/data-import/dataImport.smartRecognition.test.ts src/views/data-import/composables/useDataImportSmartStructureRecognition.test.ts
```

Expected: 所有定向测试 PASS。

- [ ] **Step 8: 提交统一配置准备逻辑**

```powershell
git add -- web/src/views/data-import/dataImport.smartRecognition.ts web/src/views/data-import/dataImport.smartRecognition.test.ts web/src/views/data-import/composables/useDataImportSmartStructureRecognition.ts web/src/views/data-import/composables/useDataImportSmartStructureRecognition.test.ts
git diff --cached --check
git commit -m "feat: 将 AI 结果预填到统一导入配置"
```

---

### Task 5: 合并 Excel 导入步骤状态机

**Files:**

- Modify: `web/src/views/data-import/dataImport.smartRecognition.ts`
- Modify: `web/src/views/data-import/dataImport.smartRecognition.test.ts`
- Modify: `web/src/views/data-import/composables/useDataImportPage.ts`
- Modify: `web/src/views/data-import/composables/useDataImportBatchExecution.ts`
- Modify: `web/src/views/data-import/composables/useDataImportBatchExecution.test.ts`
- Modify: `web/tests/data-import-confirm-layout.test.ts`

**Interfaces:**

- Consumes: Task 4 的统一配置和快照
- Produces:
  - `createDataImportSteps(): [{上传/目标}, {结构配置}, {预览/导入}, {完成}]`
  - `analyzeAndConfigure(): Promise<void>`
  - `goToImportPreview(): Promise<void>`
  - 批量执行始终优先读取 `recognizedExcelMappings`

- [ ] **Step 1: 写单一状态机失败测试**

在 `dataImport.smartRecognition.test.ts` 增加：

```ts
expect(createDataImportSteps()).toEqual([
  { title: "上传/目标" },
  { title: "结构配置" },
  { title: "预览/导入" },
  { title: "完成" }
]);
```

在 `data-import-confirm-layout.test.ts` 增加源码行为断言：

```ts
assert.doesNotMatch(dataImportSource, />\s*智能识别导入\s*</);
assert.doesNotMatch(dataImportSource, />\s*手动配置导入\s*</);
assert.match(dataImportSource, />\s*分析并配置\s*</);
assert.doesNotMatch(dataImportPageSource, /advancedMode\.value \?/);
```

批量执行测试增加：当 `excelMapping` 与 `recognizedExcelMappings[0]` 不同时，请求使用区域列表中的值。

- [ ] **Step 2: 运行状态机和批量执行测试并确认失败**

Run:

```powershell
pnpm exec vitest run src/views/data-import/dataImport.smartRecognition.test.ts src/views/data-import/composables/useDataImportBatchExecution.test.ts
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/data-import-confirm-layout.test.ts
```

Expected: FAIL，当前仍有两套步骤和两个入口。

- [ ] **Step 3: 替换步骤常量**

使用：

```ts
export const DATA_IMPORT_STEP_UPLOAD = 0;
export const DATA_IMPORT_STEP_CONFIGURE = 1;
export const DATA_IMPORT_STEP_PREVIEW = 2;
export const DATA_IMPORT_STEP_COMPLETE = 3;
```

删除 `ADVANCED_STEP_TABLE_SELECT`、`ADVANCED_STEP_MAPPING` 和 `getDataImportAdvancedStep` 的页面导航用途。

- [ ] **Step 4: 实现上传后的统一分流**

`analyzeAndConfigure` 的 Excel 路径：

1. 校验文件和客户。
2. 尝试现有智能识别。
3. 成功时使用 AI 区域配置。
4. 失败或无可用区域时获取可导入工作表并创建默认区域。
5. 设置活动工作表和活动区域。
6. 进入 `DATA_IMPORT_STEP_CONFIGURE`。

Word 保持现有流程并直接进入现有确认预览步骤。

- [ ] **Step 5: 实现配置到预览的门禁**

`goToImportPreview` 对每张已选工作表和区域调用现有配置校验；错误消息包含工作表名和区域序号，例如：

```ts
ElMessage.warning(
  `工作表“${tableName}”区域 ${region.regionIndex + 1}：请选择规格列`
);
```

校验通过后加载所有区域预览，再进入 `DATA_IMPORT_STEP_PREVIEW`。

- [ ] **Step 6: 收口批量执行数据源**

Excel 执行路径使用：

```ts
const mappings = cfg.recognizedExcelMappings ?? [];
```

统一配置初始化保证数组至少包含一个区域，因此执行阶段不再用可能陈旧的 `excelMapping` 回退。Word 路径不变。

- [ ] **Step 7: 运行状态机和批量执行测试并确认通过**

Run:

```powershell
pnpm exec vitest run src/views/data-import/dataImport.smartRecognition.test.ts src/views/data-import/composables/useDataImportBatchExecution.test.ts
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/data-import-confirm-layout.test.ts
```

Expected: 所有定向测试 PASS。

- [ ] **Step 8: 提交统一状态机**

```powershell
git add -- web/src/views/data-import/dataImport.smartRecognition.ts web/src/views/data-import/dataImport.smartRecognition.test.ts web/src/views/data-import/composables/useDataImportPage.ts web/src/views/data-import/composables/useDataImportBatchExecution.ts web/src/views/data-import/composables/useDataImportBatchExecution.test.ts web/tests/data-import-confirm-layout.test.ts
git diff --cached --check
git commit -m "refactor: 合并 Excel 导入步骤状态机"
```

---

### Task 6: 实现统一多区域配置工作台

**Files:**

- Create: `web/src/views/data-import/components/ExcelRegionWorkspace.vue`
- Create: `web/src/views/data-import/components/ExcelRegionRangeEditor.vue`
- Modify: `web/src/views/data-import/components/ExcelColumnMapping.vue`
- Modify: `web/src/views/data-import/components/TablePreview.vue`
- Modify: `web/src/views/data-import/index.vue`
- Modify: `web/src/views/data-import/index.styles.css`
- Modify: `web/src/views/data-import/composables/useDataImportPage.ts`
- Modify: `web/tests/data-import-confirm-layout.test.ts`
- Modify: `web/tests/data-import-excel-range.test.ts`
- Modify: `web/tests/table-preview-layout.test.ts`

**Interfaces:**

- Consumes: Task 2 的区域操作、Task 3 的 A1 适配器、Task 5 的统一步骤
- Produces:
  - `ExcelRegionWorkspace` 展示全宽预览、区域列表和活动区域编辑
  - `ExcelRegionRangeEditor` 包装并复用 `SmartStructureRangeEditorDrawer.vue`
  - 页面只显示“分析并配置”主入口

- [ ] **Step 1: 写工作台结构和下拉可见性失败测试**

在静态 Node 测试中断言：

```ts
const regionWorkspaceSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/data-import/components/ExcelRegionWorkspace.vue"
  ),
  "utf8"
);

assert.match(dataImportSource, /<ExcelRegionWorkspace/);
assert.match(dataImportSource, />\s*分析并配置\s*</);
assert.doesNotMatch(dataImportSource, />\s*手动配置导入\s*</);
assert.match(regionWorkspaceSource, /class="excel-region-list"/);
assert.match(regionWorkspaceSource, /class="excel-region-editor"/);
assert.match(regionWorkspaceSource, />\s*新增区域\s*</);
assert.match(regionWorkspaceSource, />\s*恢复 AI 结果\s*</);
assert.match(
  excelColumnMappingSource,
  /popper-class="excel-column-select-popper"/
);
assert.match(excelColumnMappingSource, /column-option-letter/);
assert.match(excelColumnMappingSource, /column-option-title/);
```

- [ ] **Step 2: 运行布局测试并确认失败**

Run:

```powershell
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/data-import-confirm-layout.test.ts ./tests/data-import-excel-range.test.ts ./tests/table-preview-layout.test.ts
```

Expected: FAIL，工作台组件和单入口尚不存在。

- [ ] **Step 3: 实现工作台组件**

`ExcelRegionWorkspace.vue` props 和 emits 固定为：

```ts
const props = defineProps<{
  config: TableImportConfig;
  fileId: number;
  loading?: boolean;
}>();

const emit = defineEmits<{
  "select-region": [regionId: string];
  "update-region": [regionId: string, mapping: ExcelSheetMapping];
  "add-region": [];
  "copy-region": [regionId: string];
  "remove-region": [regionId: string];
  "restore-ai": [];
  "re-recognize": [];
  "reload-preview": [regionId: string];
}>();
```

布局顺序：

1. 工作表预览。
2. 左侧区域列表。
3. 右侧 `ExcelColumnMapping`。
4. A1 摘要和“调整范围”。
5. 顶部“重新识别当前工作表”和“恢复 AI 结果”。
6. 页面底部现有上一步、校验配置和预览导入按钮。

区域的“AI 识别”标识通过 `aiExcelMappingsSnapshot` 中是否存在相同 `regionId` 判断；人工新增和复制的 ID 不在快照中，显示“人工新增”。

- [ ] **Step 4: 用适配器复用共享 A1 抽屉**

`ExcelRegionRangeEditor.vue` 不重新实现四范围表单，而是包装现有共享组件：

```ts
const props = defineProps<{
  modelValue: boolean;
  config: TableImportConfig;
  fileId: number;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: boolean];
  "save-regions": [mappings: ExcelRegionMapping[]];
}>();

const visible = computed({
  get: () => props.modelValue,
  set: value => emit("update:modelValue", value)
});
```

```vue
<SmartStructureRangeEditorDrawer
  v-model="visible"
  :table="editorTable"
  :table-info="config.tableInfo"
  :file-id="fileId"
  :is-excel-file="true"
  :require-acceptance-range="false"
  :regions="editorTable.regions ?? []"
  @save="handleSave"
/>
```

`editorTable` 由 Task 4 的 `buildDataImportRangeEditorTable` 生成。`handleSave` 调用 `mapRangeEditorRegionsToExcel`，然后：

```ts
emit("save-regions", mappings);
```

工作台收到新区域后整体替换 `recognizedExcelMappings`，保持 `aiExcelMappingsSnapshot` 不变，并根据原活动 `regionId` 恢复活动区域。

- [ ] **Step 5: 修正列下拉的完整显示**

四个 `el-select` 使用统一选项模板：

```vue
<el-select
  v-model="mapping.projectColumn"
  class="column-select"
  popper-class="excel-column-select-popper"
  filterable
  clearable
>
  <el-option
    v-for="opt in columnOptions"
    :key="opt.value"
    :label="opt.label"
    :value="opt.value"
  >
    <span class="column-option-letter">{{ opt.letter }}</span>
    <span class="column-option-title">{{ opt.header || "未命名列" }}</span>
  </el-option>
</el-select>
```

选中值始终由数字列值匹配；标题为空或预览刷新时保留列字母。使用全局 popper 类设置 `min-width: 360px`、视口内最大宽度和自动高度；选项标题允许两行显示，选中框使用省略号并通过 `title` 提供完整文本。

- [ ] **Step 6: 接入区域操作**

`useDataImportPage.ts` 暴露：

```ts
selectExcelRegion(tableIndex, regionId)
addExcelRegion(tableIndex)
copyExcelRegion(tableIndex, regionId)
removeExcelRegion(tableIndex, regionId)
restoreExcelAiRegions(tableIndex)
reRecognizeExcelTable(tableIndex)
updateExcelRegion(tableIndex, regionId, mapping)
```

每次更新后：

- 同步 `activeExcelRegionId`。
- 从活动区域重新生成 `excelMapping`。
- 行范围变化时清空区域预览；只改字段列时保留当前工作表预览。
- 表头范围变化后调用 `loadAdvancedPreview`，以新的 `headerRowStart` 和 `headerRowCount` 获取 `previewData.headers`，再刷新列选项。
- 清理对应区域的排除行和待确认预览选择。

- [ ] **Step 7: 替换 `index.vue` 的两套入口和映射模板**

上传步骤只保留：

```vue
<el-button
  v-if="currentStep === DATA_IMPORT_STEP_UPLOAD"
  type="primary"
  :disabled="nextDisabled"
  :loading="smartRecognizing"
  @click="analyzeAndConfigure"
>
  {{ smartStageText || "分析并配置" }}
</el-button>
```

Excel 配置步骤按工作表标签渲染 `ExcelRegionWorkspace`；删除 `advancedMode` 驱动的第二套步骤模板。Word 现有确认模板保留。

- [ ] **Step 8: 运行布局和范围测试并确认通过**

Run:

```powershell
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/data-import-confirm-layout.test.ts ./tests/data-import-excel-range.test.ts ./tests/table-preview-layout.test.ts
pnpm exec vitest run src/views/data-import/dataImport.regions.test.ts src/views/data-import/dataImport.a1.test.ts
```

Expected: 所有定向测试 PASS。

- [ ] **Step 9: 提交统一工作台**

```powershell
git add -- web/src/views/data-import/components/ExcelRegionWorkspace.vue web/src/views/data-import/components/ExcelRegionRangeEditor.vue web/src/views/data-import/components/ExcelColumnMapping.vue web/src/views/data-import/components/TablePreview.vue web/src/views/data-import/index.vue web/src/views/data-import/index.styles.css web/src/views/data-import/composables/useDataImportPage.ts web/tests/data-import-confirm-layout.test.ts web/tests/data-import-excel-range.test.ts web/tests/table-preview-layout.test.ts
git diff --cached --check
git commit -m "feat: 实现 Excel 统一多区域配置工作台"
```

---

### Task 7: 完成失败保护、验证和 OpenSpec 收尾

**Files:**

- Modify: `web/src/views/data-import/composables/useDataImportPage.ts`
- Modify: `web/src/views/data-import/components/ExcelRegionWorkspace.vue`
- Modify: `web/src/views/data-import/dataImport.regions.ts`
- Modify: `web/src/views/data-import/dataImport.regions.test.ts`
- Modify: `web/tests/data-import-confirm-layout.test.ts`
- Modify: `openspec/changes/add-excel-header-range-selection/tasks.md`

**Interfaces:**

- Consumes: Tasks 1–6 的统一状态机和工作台
- Produces: 可验证的 AI 失败降级、覆盖确认、区域冲突阻断和最终交付证据

- [ ] **Step 1: 写失败降级和危险操作确认测试**

静态测试必须匹配以下行为：

```ts
assert.match(dataImportPageSource, /已进入手动配置，可继续完成导入/);
assert.match(dataImportPageSource, /重新识别将覆盖当前工作表的人工修改/);
assert.match(dataImportPageSource, /恢复 AI 结果将覆盖当前工作表的人工修改/);
assert.match(dataImportPageSource, /工作表.*区域.*重复覆盖/);
```

区域单元测试增加：相同行范围但不同字段列允许存在，同一字段列和相同行范围被两个区域覆盖时返回冲突。

- [ ] **Step 2: 运行保护测试并确认失败**

Run:

```powershell
pnpm exec vitest run src/views/data-import/dataImport.regions.test.ts
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/data-import-confirm-layout.test.ts
```

Expected: FAIL，覆盖确认和单元格冲突校验尚未完整实现。

- [ ] **Step 3: 实现明确的失败语义**

- AI 失败：保留 `smartRecognitionError` 供上传区域展示，同时创建默认区域并进入结构配置。
- 重新识别失败：不修改当前区域和快照。
- 恢复 AI、重新识别、删除有人工修改的区域：使用 `ElMessageBox.confirm`。
- 冲突校验按字段列与数据行交集判断，不仅按行范围判断。
- 校验错误包含工作表名、区域序号和冲突字段。

- [ ] **Step 4: 运行所有受影响的定向测试**

Run from `web`:

```powershell
pnpm exec vitest run src/views/data-import/dataImport.regions.test.ts src/views/data-import/dataImport.a1.test.ts src/views/data-import/dataImport.helpers.test.ts src/views/data-import/dataImport.smartRecognition.test.ts src/views/data-import/composables/useDataImportSmartStructureRecognition.test.ts src/views/data-import/composables/useDataImportBatchExecution.test.ts src/views/shared/smart-structure-recognition.test.ts
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/data-import-confirm-layout.test.ts ./tests/data-import-excel-range.test.ts ./tests/table-preview-layout.test.ts ./tests/table-selector-filter.test.ts
```

Expected: 所有受影响测试 PASS，失败数为 `0`。

- [ ] **Step 5: 运行前端静态验证**

Run from `web`:

```powershell
pnpm typecheck
pnpm lint:check
pnpm stylelint:check
pnpm format:check
```

Expected: 四个命令均退出码 `0`。

- [ ] **Step 6: 严格验证 OpenSpec 并完成任务清单**

先把已真实完成的 `tasks.md` 项目改为 `- [x]`，再从仓库根目录运行：

```powershell
openspec validate add-excel-header-range-selection --strict
git diff --check
```

Expected: OpenSpec 通过，`git diff --check` 无输出。

- [ ] **Step 7: 执行浏览器行为验证**

在本项目现有测试服务可用时验证：

1. 上传 Excel、选择客户，上传页只显示“分析并配置”。
2. 点击后进入结构配置，AI 区域可以继续修改。
3. 把表头起始行改为 6，列选项标题来自第 6 行且选中值不为空。
4. 新增第二个区域并配置不同数据块。
5. 打开 A1 编辑器修改规格范围，行号和列映射同步更新。
6. 进入预览，两个区域的记录均存在且真实行号正确。
7. 模拟 AI 不可用，仍进入默认区域配置。

Expected: 七个场景均通过；若环境无法提供真实 AI 或浏览器服务，在交付报告中单独标为未验证，不以静态测试代替。

- [ ] **Step 8: 提交收尾改动**

```powershell
git add -- web/src/views/data-import/composables/useDataImportPage.ts web/src/views/data-import/components/ExcelRegionWorkspace.vue web/src/views/data-import/dataImport.regions.ts web/src/views/data-import/dataImport.regions.test.ts web/tests/data-import-confirm-layout.test.ts openspec/changes/add-excel-header-range-selection/tasks.md
git diff --cached --check
git commit -m "test: 完善统一导入配置回归保护"
```

- [ ] **Step 9: 最终核对提交范围**

```powershell
git status --short
git log --oneline -8
git diff HEAD~4..HEAD --stat
```

Expected: 本任务文件已提交；任何任务开始前就存在且未纳入本任务提交的改动仍保持原样并在交付报告中列明。
