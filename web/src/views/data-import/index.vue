<script setup lang="ts">
import { ref, computed, watch, onActivated, onMounted, nextTick } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import FileUpload from "./components/FileUpload.vue";
import TableSelector from "./components/TableSelector.vue";
import TablePreview from "./components/TablePreview.vue";
import ColumnMapping from "./components/ColumnMapping.vue";
import ExcelColumnMapping, {
  type ExcelSheetMapping
} from "./components/ExcelColumnMapping.vue";
import {
  ColumnMappingMatchMode,
  ColumnMappingTargetField,
  getEffectiveColumnMappingRules,
  type ColumnMappingRule
} from "@/api/column-mapping-rules";
import {
  getCustomerList,
  type Customer
} from "@/api/customer";
import { getProcessList, type Process } from "@/api/process";
import { getMachineModelList, type MachineModel } from "@/api/machine-model";
import {
  getFileTables,
  importData,
  importExcelData,
  type FileUploadResponse,
  type TableInfo,
  type TableData,
  type ColumnMapping as ColumnMappingType,
  type ExcelImportDataRequest,
  type ImportResult
} from "@/api/document";

defineOptions({
  name: "DataImport"
});

// 步骤
const currentStep = ref(0);
const steps = computed(() => [
  { title: "上传文件", description: isExcelFile.value ? "选择 Excel 文件" : "选择 Word/Excel 文件" },
  { title: isExcelFile.value ? "选择工作表" : "选择表格", description: "选择要导入的数据范围" },
  { title: "配置映射", description: isExcelFile.value ? "按列序号指定字段" : "设置列映射关系" },
  { title: "选择目标", description: "选择导入目标" },
  { title: "确认导入", description: "预览并确认" }
]);

// 文件上传
const uploadedFile = ref<FileUploadResponse | null>(null);
const isExcelFile = computed(() => uploadedFile.value?.fileType === 1);

// 表格选择（支持多选）
const selectedTableIndexes = ref<number[]>([]);
const selectedTables = ref<TableInfo[]>([]);
const activeTableIndex = ref<number | null>(null);

type TableImportConfig = {
  tableIndex: number;
  tableInfo?: TableInfo;
  wordMapping?: ColumnMappingType;
  excelMapping?: ExcelSheetMapping;
  previewData: TableData | null;
};

const defaultWordMapping = (): ColumnMappingType => ({
  projectColumn: undefined,
  specificationColumn: undefined,
  acceptanceColumn: undefined,
  remarkColumn: undefined,
  headerRowIndex: 0,
  dataStartRowIndex: 1
});

const defaultExcelMapping = (): ExcelSheetMapping => ({
  projectColumn: undefined,
  specificationColumn: undefined,
  acceptanceColumn: undefined,
  remarkColumn: undefined,
  headerRowStart: 1,
  headerRowCount: 1,
  dataStartRow: 2
});

const normalizeExcelMappingByTable = (
  tableInfo: TableInfo | undefined,
  mapping?: ExcelSheetMapping
): ExcelSheetMapping => {
  const usedStartRow = Math.max(1, tableInfo?.usedRangeStartRow ?? 1);
  const current = mapping ?? defaultExcelMapping();
  const headerRowCount = Math.max(0, current.headerRowCount ?? 1);
  const headerRowStart = Math.max(usedStartRow, current.headerRowStart || usedStartRow);
  const minDataStart = headerRowStart + headerRowCount;
  const dataStartRow = Math.max(minDataStart, current.dataStartRow || minDataStart);

  return {
    ...current,
    headerRowStart,
    headerRowCount,
    dataStartRow
  };
};

const createDefaultExcelMapping = (tableInfo?: TableInfo): ExcelSheetMapping => {
  return normalizeExcelMappingByTable(tableInfo, defaultExcelMapping());
};

const getExcelPreviewOptions = (cfg: TableImportConfig) => {
  const usedStartRow = cfg.tableInfo?.usedRangeStartRow ?? 1;
  const m = normalizeExcelMappingByTable(cfg.tableInfo, cfg.excelMapping);

  return {
    headerRowIndex: Math.max(0, (m.headerRowStart || usedStartRow) - usedStartRow),
    headerRowCount: Math.max(1, m.headerRowCount ?? 1),
    dataStartRowIndex: Math.max(0, (m.dataStartRow || usedStartRow) - usedStartRow)
  };
};

const tableConfigs = ref<TableImportConfig[]>([]);
type MappingClipboard =
  | { kind: "excel"; value: ExcelSheetMapping }
  | { kind: "word"; value: ColumnMappingType };
const mappingClipboard = ref<MappingClipboard | null>(null);
const mappingClipboardSourceIndex = ref<number | null>(null);

// 全局列映射规则（用于自动预填）
const mappingRules = ref<ColumnMappingRule[]>([]);
const loadingMappingRules = ref(false);

// 目标选择
const customers = ref<Customer[]>([]);
const processes = ref<Process[]>([]);
const machineModels = ref<MachineModel[]>([]);
const selectedCustomerId = ref<number | undefined>(undefined);
const selectedProcessId = ref<number | undefined>(undefined);
const selectedMachineModelId = ref<number | undefined>(undefined);
const loadingCustomers = ref(false);
const loadingProcesses = ref(false);
const loadingMachineModels = ref(false);

// 导入结果
const importing = ref(false);
type ImportErrorWithTable = { tableIndex: number } & ImportResult["errors"][number];
type ImportSkippedRowWithTable = { tableIndex: number } & NonNullable<ImportResult["skippedRows"]>[number];
type ImportPendingDifferenceWithTable = { tableIndex: number } & NonNullable<
  ImportResult["pendingDifferences"]
>[number];
type DifferenceDecision = "import" | "skip";
type CombinedImportResult = Omit<ImportResult, "errors" | "skippedRows" | "pendingDifferences"> & {
  errors: ImportErrorWithTable[];
  skippedRows: ImportSkippedRowWithTable[];
  pendingDifferences: ImportPendingDifferenceWithTable[];
};
const importResult = ref<CombinedImportResult | null>(null);
const previewSkippedRows = ref(false);
const differenceDecisionMap = ref<Record<string, DifferenceDecision | undefined>>(
  {}
);

// 让步骤条吸顶到实际滚动容器（pure-admin 使用 el-scrollbar）
const affixTarget = ref<string>("");
const affixOffset = ref<number>(0);

const refreshAffix = async () => {
  await nextTick();

  // fixedHeader=true 时：LayContent 内部有 .app-main .el-scrollbar__wrap
  const appMainWrap = document.querySelector(".app-main .el-scrollbar__wrap");
  if (appMainWrap) {
    affixTarget.value = ".app-main .el-scrollbar__wrap";
    // 关键：读取 app-main 的 padding-top（tabs/header 高度），让 affix 从一开始就“贴住”并且不盖住顶部栏
    const appMain = document.querySelector(".app-main") as HTMLElement | null;
    const pt = appMain ? parseInt(getComputedStyle(appMain).paddingTop || "0", 10) : 0;
    affixOffset.value = Number.isFinite(pt) && pt > 0 ? pt : 86;
    return;
  }

  // fixedHeader=false 时：Layout 外层有 .main-container .el-scrollbar__wrap
  const mainWrap = document.querySelector(".main-container .el-scrollbar__wrap");
  if (mainWrap) {
    affixTarget.value = ".main-container .el-scrollbar__wrap";
    // header 不固定时，Affix 贴在容器顶部即可
    affixOffset.value = 0;
    return;
  }

  // fallback：不设置 target，则 Affix 会绑定 window（但本项目通常不会走到这里）
  affixTarget.value = "";
  affixOffset.value = 0;
};

onMounted(() => {
  // 首次进入
  refreshAffix();
  // 某些情况下 layout/scroll 容器渲染更晚，做一次轻量重试
  setTimeout(refreshAffix, 50);
  setTimeout(refreshAffix, 200);
});

onActivated(() => {
  // keep-alive 返回页面时，重新绑定一次
  refreshAffix();
});

// 计算属性
const canGoNext = computed(() => {
  switch (currentStep.value) {
    case 0:
      return uploadedFile.value !== null;
    case 1:
      return (
        selectedTableIndexes.value.length > 0 ||
        selectedTables.value.length > 0 ||
        tableConfigs.value.length > 0 ||
        (uploadedFile.value?.tableCount ?? 0) > 0
      );
    case 2:
      // 映射步骤允许点击“下一步”，在 goNext 中做校验并提示缺失项，避免按钮直接置灰导致“卡死”
      return tableConfigs.value.length > 0;
    case 3:
      return (
        selectedCustomerId.value !== undefined
      );
    case 4:
      return true;
    default:
      return false;
  }
});

const getMissingMappingFields = (m: ColumnMappingType) => {
  const missing: string[] = [];
  if (m.projectColumn === undefined) missing.push("项目名称列");
  if (m.specificationColumn === undefined) missing.push("规格内容列");
  if (m.acceptanceColumn === undefined) missing.push("验收标准列");
  if (m.remarkColumn === undefined) missing.push("备注列");
  return missing;
};

const getMissingExcelMappingFields = (m?: ExcelSheetMapping) => {
  const missing: string[] = [];
  if (!m) return ["Excel 映射未配置"];
  if (!m.projectColumn) missing.push("项目列");
  if (!m.specificationColumn) missing.push("规格列");
  if (m.headerRowStart < 1) missing.push("表头起始行");
  if (m.headerRowCount < 0) missing.push("表头行数");
  if (m.dataStartRow < 1) missing.push("数据起始行");
  return missing;
};

const validateAllTableMappings = () => {
  const missingByTable = tableConfigs.value
    .map(cfg => ({
      tableIndex: cfg.tableIndex,
      missing: isExcelFile.value
        ? getMissingExcelMappingFields(cfg.excelMapping)
        : getMissingMappingFields(cfg.wordMapping!)
    }))
    .filter(x => x.missing.length > 0);

  return {
    ok: missingByTable.length === 0,
    missingByTable
  };
};

const nextDisabled = computed(() => {
  // 步骤1（选择工作表）不置灰，点击后在 goNext 内做兜底同步，避免被状态不同步卡住
  if (currentStep.value === 1) return false;
  // 步骤2（配置映射）永不置灰：允许点击后提示缺失项
  if (currentStep.value === 2) return !canGoNext.value;
  return !canGoNext.value;
});

// 文件上传完成
const handleFileUploaded = (file: FileUploadResponse) => {
  uploadedFile.value = file;
  // 重置后续步骤数据
  selectedTableIndexes.value = [];
  selectedTables.value = [];
  activeTableIndex.value = null;
  tableConfigs.value = [];
  mappingClipboard.value = null;
  mappingClipboardSourceIndex.value = null;
};

// 表格选择（多选）
const handleTablesSelected = (tables: TableInfo[]) => {
  selectedTables.value = tables;
  selectedTableIndexes.value = tables.map(t => t.index).sort((a, b) => a - b);
  if (activeTableIndex.value == null && selectedTableIndexes.value.length > 0) {
    activeTableIndex.value = selectedTableIndexes.value[0];
  }

  const existing = new Map(tableConfigs.value.map(c => [c.tableIndex, c]));
  const next: TableImportConfig[] = [];
  for (const t of tables) {
    const old = existing.get(t.index);
    next.push(
      old
        ? {
            ...old,
            tableInfo: t,
            ...(isExcelFile.value
              ? { excelMapping: normalizeExcelMappingByTable(t, old.excelMapping) }
              : { wordMapping: old.wordMapping ?? defaultWordMapping() })
          }
        : {
            tableIndex: t.index,
            tableInfo: t,
            ...(isExcelFile.value
              ? { excelMapping: createDefaultExcelMapping(t) }
              : { wordMapping: defaultWordMapping() }),
            previewData: null
          }
    );
  }
  tableConfigs.value = next.sort((a, b) => a.tableIndex - b.tableIndex);

  // 若已加载规则，自动预填一次（不覆盖用户已有选择）
  applyRulesToAll(false);
};

const removeSelectedTable = (tableIndex: number) => {
  // 从选择中移除
  selectedTableIndexes.value = selectedTableIndexes.value.filter(i => i !== tableIndex);
  selectedTables.value = selectedTables.value.filter(t => t.index !== tableIndex);
  tableConfigs.value = tableConfigs.value.filter(c => c.tableIndex !== tableIndex);

  // 调整当前激活 tab
  if (activeTableIndex.value === tableIndex) {
    const nextIdx = selectedTableIndexes.value.length > 0 ? selectedTableIndexes.value[0] : null;
    activeTableIndex.value = nextIdx;
  }
};

const handleTabRemove = (name: string | number) => {
  const idx = typeof name === "number" ? name : Number(name);
  if (!Number.isFinite(idx)) return;
  removeSelectedTable(idx);
};

const handlePreviewLoaded = (tableIndex: number, data: TableData) => {
  const cfg = tableConfigs.value.find(c => c.tableIndex === tableIndex);
  if (cfg) {
    cfg.previewData = data;
    // 表头/预览更新后，若尚未选择映射列，则再次尝试按规则自动预填（不覆盖手工选择）
    applyRulesToConfig(cfg, false);
  }
};

const updateExcelMapping = (tableIndex: number, value: ExcelSheetMapping) => {
  const cfg = tableConfigs.value.find(c => c.tableIndex === tableIndex);
  if (!cfg) return;
  cfg.excelMapping = normalizeExcelMappingByTable(cfg.tableInfo, value);
};

const getActiveTableConfig = (): TableImportConfig | null => {
  if (tableConfigs.value.length === 0) return null;
  if (activeTableIndex.value === null) return tableConfigs.value[0];
  return (
    tableConfigs.value.find(c => c.tableIndex === activeTableIndex.value) ??
    tableConfigs.value[0]
  );
};

const canPasteClipboard = computed(() => {
  if (!mappingClipboard.value) return false;
  if (isExcelFile.value) return mappingClipboard.value.kind === "excel";
  return mappingClipboard.value.kind === "word";
});

const copyActiveMappingConfig = () => {
  const activeCfg = getActiveTableConfig();
  if (!activeCfg) {
    ElMessage.warning(`请先选择一个${isExcelFile.value ? "工作表" : "表格"}`);
    return;
  }

  if (isExcelFile.value) {
    const normalized = normalizeExcelMappingByTable(
      activeCfg.tableInfo,
      activeCfg.excelMapping ?? defaultExcelMapping()
    );
    mappingClipboard.value = {
      kind: "excel",
      value: { ...normalized }
    };
  } else {
    mappingClipboard.value = {
      kind: "word",
      value: { ...(activeCfg.wordMapping ?? defaultWordMapping()) }
    };
  }

  mappingClipboardSourceIndex.value = activeCfg.tableIndex;
  ElMessage.success(
    `已复制${isExcelFile.value ? "工作表" : "表格"} ${activeCfg.tableIndex + 1} 的配置`
  );
};

const pasteMappingConfigToOthers = () => {
  const activeCfg = getActiveTableConfig();
  if (!activeCfg) {
    ElMessage.warning(`请先选择一个${isExcelFile.value ? "工作表" : "表格"}`);
    return;
  }

  if (!mappingClipboard.value || !canPasteClipboard.value) {
    ElMessage.warning(
      `请先复制${isExcelFile.value ? "Excel 列序号配置" : "列映射配置"}`
    );
    return;
  }

  let pastedCount = 0;
  for (const cfg of tableConfigs.value) {
    if (cfg.tableIndex === activeCfg.tableIndex) continue;

    if (isExcelFile.value && mappingClipboard.value.kind === "excel") {
      cfg.excelMapping = normalizeExcelMappingByTable(cfg.tableInfo, {
        ...mappingClipboard.value.value
      });
      pastedCount++;
      continue;
    }

    if (!isExcelFile.value && mappingClipboard.value.kind === "word") {
      cfg.wordMapping = { ...mappingClipboard.value.value };
      pastedCount++;
    }
  }

  if (pastedCount === 0) {
    ElMessage.warning(`没有可粘贴的其他${isExcelFile.value ? "工作表" : "表格"}`);
    return;
  }

  ElMessage.success(
    `已粘贴到 ${pastedCount} 个其他${isExcelFile.value ? "工作表" : "表格"}`
  );
};

const normalizeHeader = (s?: string) => (s || "").trim().toLowerCase();

const isMatch = (header: string, rule: ColumnMappingRule) => {
  const h = normalizeHeader(header);
  const p = normalizeHeader(rule.pattern);
  if (!h || !p) return false;
  switch (rule.matchMode) {
    case ColumnMappingMatchMode.Equals:
      return h === p;
    case ColumnMappingMatchMode.Regex:
      try {
        return new RegExp(rule.pattern).test(header);
      } catch {
        return false;
      }
    case ColumnMappingMatchMode.Contains:
    default:
      return h.includes(p);
  }
};

const pickBestColumnIndex = (
  headers: string[],
  rulesForTarget: ColumnMappingRule[],
  used: Set<number>
) => {
  let bestIndex: number | undefined = undefined;
  let bestScore = -Infinity;

  for (let i = 0; i < headers.length; i++) {
    if (used.has(i)) continue;
    const header = headers[i] || "";

    for (const r of rulesForTarget) {
      if (!r.enabled) continue;
      if (!isMatch(header, r)) continue;

      // 评分：优先级 > 匹配词长度 > 列越靠前越好
      const score = (r.priority ?? 0) * 10000 + (r.pattern?.length ?? 0) * 10 - i;
      if (score > bestScore) {
        bestScore = score;
        bestIndex = i;
      }
    }
  }

  return bestIndex;
};

const applyRulesToConfig = (cfg: TableImportConfig, overwrite: boolean) => {
  const headers = cfg.tableInfo?.headers || cfg.previewData?.headers || [];
  if (!headers || headers.length === 0) return;

  const byTarget = new Map<ColumnMappingTargetField, ColumnMappingRule[]>();
  for (const r of mappingRules.value) {
    if (!r.enabled) continue;
    const list = byTarget.get(r.targetField) || [];
    list.push(r);
    byTarget.set(r.targetField, list);
  }
  // 每个目标字段内部：优先级倒序
  for (const [k, list] of byTarget.entries()) {
    list.sort((a, b) => (b.priority ?? 0) - (a.priority ?? 0) || a.id - b.id);
    byTarget.set(k, list);
  }

  // 注意：ColumnMapping 组件只监听 modelValue 引用变化，因此这里必须“生成新对象”触发刷新
  const used = new Set<number>();
  if (!cfg.wordMapping) cfg.wordMapping = defaultWordMapping();
  const next: ColumnMappingType = { ...cfg.wordMapping };

  const setIfNeed = (key: keyof ColumnMappingType, val?: number) => {
    if (val === undefined) return;
    if (overwrite || next[key] === undefined) {
      next[key] = val;
      used.add(val);
    } else if (typeof next[key] === "number") {
      used.add(next[key] as number);
    }
  };

  // 先把已选的列加入 used，避免重复占用
  for (const k of [
    "projectColumn",
    "specificationColumn",
    "acceptanceColumn",
    "remarkColumn"
  ] as const) {
    const v = next[k];
    if (typeof v === "number") used.add(v);
  }

  setIfNeed(
    "projectColumn",
    pickBestColumnIndex(
      headers,
      byTarget.get(ColumnMappingTargetField.Project) || [],
      used
    )
  );
  setIfNeed(
    "specificationColumn",
    pickBestColumnIndex(
      headers,
      byTarget.get(ColumnMappingTargetField.Specification) || [],
      used
    )
  );
  setIfNeed(
    "acceptanceColumn",
    pickBestColumnIndex(
      headers,
      byTarget.get(ColumnMappingTargetField.Acceptance) || [],
      used
    )
  );
  setIfNeed(
    "remarkColumn",
    pickBestColumnIndex(
      headers,
      byTarget.get(ColumnMappingTargetField.Remark) || [],
      used
    )
  );

  cfg.wordMapping = next;
};

const applyRulesToAll = (overwrite: boolean) => {
  if (isExcelFile.value) return;
  if (!mappingRules.value.length) return;
  for (const cfg of tableConfigs.value) {
    applyRulesToConfig(cfg, overwrite);
  }
};

const loadMappingRules = async () => {
  loadingMappingRules.value = true;
  try {
    const res = await getEffectiveColumnMappingRules();
    if (res.code === 0) {
      mappingRules.value = res.data || [];
      // 进入映射步骤后，自动预填一次（不覆盖手工选择）
      applyRulesToAll(false);
    } else {
      ElMessage.error(res.message || "加载列映射规则失败");
    }
  } catch {
    ElMessage.error("加载列映射规则失败");
  } finally {
    loadingMappingRules.value = false;
  }
};

// 加载客户列表
const loadCustomers = async () => {
  loadingCustomers.value = true;
  try {
    const res = await getCustomerList({ page: 1, pageSize: 100 });
    if (res.code === 0) {
      customers.value = res.data.items;
    }
  } catch {
    ElMessage.error("加载客户列表失败");
  } finally {
    loadingCustomers.value = false;
  }
};

// 加载制程列表
const loadProcesses = async () => {
  loadingProcesses.value = true;
  try {
    const res = await getProcessList({ page: 1, pageSize: 1000 });
    if (res.code === 0) {
      processes.value = res.data.items;
    }
  } catch {
    ElMessage.error("加载制程列表失败");
  } finally {
    loadingProcesses.value = false;
  }
};

// 加载机型列表
const loadMachineModels = async () => {
  loadingMachineModels.value = true;
  try {
    const res = await getMachineModelList({ page: 1, pageSize: 1000 });
    if (res.code === 0) {
      machineModels.value = res.data.items;
    }
  } catch {
    ElMessage.error("加载机型列表失败");
  } finally {
    loadingMachineModels.value = false;
  }
};

// 监听步骤变化
watch(currentStep, (step) => {
  if (
    step === 2 &&
    !isExcelFile.value &&
    mappingRules.value.length === 0 &&
    !loadingMappingRules.value
  ) {
    loadMappingRules();
  }
  if (step === 3 && customers.value.length === 0) {
    loadCustomers();
  }
  if (step === 3 && processes.value.length === 0) {
    loadProcesses();
  }
  if (step === 3 && machineModels.value.length === 0) {
    loadMachineModels();
  }
});

// 兼容 keep-alive：从“列映射规则”页面返回导入页时，确保规则能刷新并重新预填
onActivated(() => {
  if (currentStep.value === 2 && !isExcelFile.value) {
    loadMappingRules();
  }
});

// 下一步
const ensureStepTwoSelection = async () => {
  if (selectedTableIndexes.value.length > 0 || selectedTables.value.length > 0 || tableConfigs.value.length > 0) {
    return true;
  }

  if (!uploadedFile.value?.fileId) {
    ElMessage.warning("请先上传文件");
    return false;
  }

  try {
    const res = await getFileTables(uploadedFile.value.fileId);
    if (res.code !== 0 || !res.data?.length) {
      ElMessage.warning("请至少选择一个工作表");
      return false;
    }

    // 兜底：父状态丢失时自动补齐为“全选”，避免界面卡死在步骤2
    handleTablesSelected(res.data);
    return true;
  } catch {
    ElMessage.warning("请至少选择一个工作表");
    return false;
  }
};

const goNext = async () => {
  // 步骤1（选择工作表）：不依赖 canGoNext，统一走兜底同步
  if (currentStep.value === 1) {
    if (selectedTableIndexes.value.length === 0) {
      if (selectedTables.value.length > 0) {
        selectedTableIndexes.value = selectedTables.value
          .map(t => t.index)
          .sort((a, b) => a - b);
      } else if (tableConfigs.value.length > 0) {
        selectedTableIndexes.value = tableConfigs.value
          .map(c => c.tableIndex)
          .sort((a, b) => a - b);
      } else {
        const ok = await ensureStepTwoSelection();
        if (!ok) return;
      }
    }

    if (currentStep.value < steps.value.length - 1) currentStep.value++;
    return;
  }

  if (!canGoNext.value) return;

  // 步骤3：配置映射。这里做完整校验，缺失则提示并跳转到对应表格，避免“按钮置灰卡死”
  if (currentStep.value === 2) {
    const v = validateAllTableMappings();
    if (!v.ok) {
      const first = v.missingByTable[0];
      activeTableIndex.value = first.tableIndex;
      const summary = v.missingByTable
        .slice(0, 3)
        .map(x => `表格${x.tableIndex + 1}：缺 ${x.missing.join("、")}`)
        .join("；");
      const more = v.missingByTable.length > 3 ? `（另有 ${v.missingByTable.length - 3} 个表格未完成映射）` : "";
      ElMessage.warning(`请先完成列映射：${summary}${more}`);
      return;
    }
  }

  if (currentStep.value < steps.value.length - 1) currentStep.value++;
};

// 上一步
const goPrev = () => {
  if (currentStep.value > 0) {
    currentStep.value--;
  }
};

const buildEmptyImportAggregate = (): CombinedImportResult => ({
  successCount: 0,
  failedCount: 0,
  skippedCount: 0,
  totalCount: 0,
  errors: [],
  skippedRows: [],
  requiresConfirmation: false,
  pendingCount: 0,
  pendingDifferences: []
});

const buildDifferenceKeysByTable = (tableIndex: number) => {
  const confirmed: string[] = [];
  const skipped: string[] = [];
  for (const item of pendingDifferences.value) {
    if (item.tableIndex !== tableIndex) continue;
    const decision = differenceDecisionMap.value[item.key];
    if (decision === "import") confirmed.push(item.key);
    if (decision === "skip") skipped.push(item.key);
  }
  return { confirmed, skipped };
};

const executeImportBatch = async (
  configs: TableImportConfig[],
  includeDifferenceDecisions: boolean
) => {
  const aggregate = buildEmptyImportAggregate();
  let hasPendingEncountered = false;

  for (const [idx, cfg] of configs.entries()) {
    const cleanupSourceFile = !hasPendingEncountered && idx === configs.length - 1;
    const { confirmed, skipped } = includeDifferenceDecisions
      ? buildDifferenceKeysByTable(cfg.tableIndex)
      : { confirmed: [] as string[], skipped: [] as string[] };

    const res = isExcelFile.value
      ? await importExcelData({
          fileId: uploadedFile.value!.fileId,
          sheetIndex: cfg.tableIndex,
          customerId: selectedCustomerId.value!,
          processId: selectedProcessId.value || undefined,
          machineModelId: selectedMachineModelId.value || undefined,
          cleanupSourceFile,
          previewSkippedRows: previewSkippedRows.value,
          confirmedDifferenceKeys: confirmed,
          skippedDifferenceKeys: skipped,
          ...(normalizeExcelMappingByTable(
            cfg.tableInfo,
            cfg.excelMapping ?? defaultExcelMapping()
          ) as ExcelImportDataRequest)
        })
      : await importData({
          fileId: uploadedFile.value!.fileId,
          tableIndex: cfg.tableIndex,
          customerId: selectedCustomerId.value!,
          processId: selectedProcessId.value || undefined,
          machineModelId: selectedMachineModelId.value || undefined,
          cleanupSourceFile,
          previewSkippedRows: previewSkippedRows.value,
          confirmedDifferenceKeys: confirmed,
          skippedDifferenceKeys: skipped,
          mapping: cfg.wordMapping!
        });

    if (res.code !== 0) {
      aggregate.failedCount += 1;
      aggregate.errors.push({
        tableIndex: cfg.tableIndex,
        rowIndex: 0,
        message: res.message || "导入失败"
      });
      continue;
    }

    if (res.data.requiresConfirmation && (res.data.pendingCount || 0) > 0) {
      hasPendingEncountered = true;
    }

    aggregate.successCount += res.data.successCount;
    aggregate.failedCount += res.data.failedCount;
    aggregate.skippedCount += res.data.skippedCount;
    aggregate.totalCount += res.data.totalCount;
    aggregate.requiresConfirmation =
      aggregate.requiresConfirmation || !!res.data.requiresConfirmation;
    aggregate.pendingCount += res.data.pendingCount || 0;
    aggregate.errors.push(
      ...(res.data.errors || []).map(e => ({
        tableIndex: cfg.tableIndex,
        ...e
      }))
    );
    aggregate.skippedRows.push(
      ...((res.data.skippedRows || []).map(s => ({
        tableIndex: cfg.tableIndex,
        ...s
      })))
    );
    aggregate.pendingDifferences.push(
      ...((res.data.pendingDifferences || []).map(d => ({
        tableIndex: cfg.tableIndex,
        ...d
      })))
    );
  }

  return aggregate;
};

const handleImport = async () => {
  if (!uploadedFile.value || tableConfigs.value.length === 0 || !selectedCustomerId.value) {
    return;
  }

  const isConfirmingDifferences =
    importResult.value?.requiresConfirmation && pendingDifferences.value.length > 0;

  if (isConfirmingDifferences) {
    if (pendingUndecidedCount.value > 0) {
      ElMessage.warning(`请先逐条确认差异（仍有 ${pendingUndecidedCount.value} 条未选择）`);
      return;
    }

    importing.value = true;
    try {
      const pendingSet = new Set(pendingTableIndexes.value);
      const pendingConfigs = tableConfigs.value.filter(cfg => pendingSet.has(cfg.tableIndex));
      const aggregate = await executeImportBatch(pendingConfigs, true);
      importResult.value = aggregate;

      if (aggregate.requiresConfirmation && aggregate.pendingDifferences.length > 0) {
        for (const d of aggregate.pendingDifferences) {
          differenceDecisionMap.value[d.key] = differenceDecisionMap.value[d.key];
        }
        ElMessage.warning(`仍有 ${aggregate.pendingCount || 0} 条差异未确认`);
        return;
      }

      differenceDecisionMap.value = {};
      ElMessage.success(`导入完成：成功${aggregate.successCount}条，失败${aggregate.failedCount}条`);
    } finally {
      importing.value = false;
    }
    return;
  }

  try {
    await ElMessageBox.confirm(
      `确定要将 ${tableConfigs.value.length} 个${isExcelFile.value ? "工作表" : "表格"}的数据导入到所选客户/制程/机型吗？`,
      "确认导入",
      {
        confirmButtonText: "确定",
        cancelButtonText: "取消",
        type: "warning"
      }
    );

    importing.value = true;
    const aggregate = await executeImportBatch(tableConfigs.value, false);
    importResult.value = aggregate;

    if (aggregate.requiresConfirmation && aggregate.pendingDifferences.length > 0) {
      const nextDecisionMap: Record<string, DifferenceDecision | undefined> = {};
      for (const item of aggregate.pendingDifferences) {
        nextDecisionMap[item.key] = undefined;
      }
      differenceDecisionMap.value = nextDecisionMap;
      ElMessage.warning(`检测到 ${aggregate.pendingCount || 0} 条差异，请逐条确认后导入`);
      return;
    }

    differenceDecisionMap.value = {};
    ElMessage.success(`导入完成：成功${aggregate.successCount}条，失败${aggregate.failedCount}条`);
  } catch {
    // 用户取消
  } finally {
    importing.value = false;
  }
};

// 重新开始
const handleRestart = () => {
  currentStep.value = 0;
  uploadedFile.value = null;
  selectedTableIndexes.value = [];
  selectedTables.value = [];
  activeTableIndex.value = null;
  tableConfigs.value = [];
  selectedCustomerId.value = undefined;
  selectedProcessId.value = undefined;
  selectedMachineModelId.value = undefined;
  importResult.value = null;
  previewSkippedRows.value = false;
  differenceDecisionMap.value = {};
  mappingClipboard.value = null;
  mappingClipboardSourceIndex.value = null;
};

// 预览数据条数（totalRows 已是纯数据行数，无需再减表头）
const previewDataCount = computed(() => {
  if (tableConfigs.value.length === 0) return 0;
  return tableConfigs.value.reduce((sum, cfg) => {
    if (!cfg.previewData) return sum;
    return sum + cfg.previewData.totalRows;
  }, 0);
});

const pendingDifferences = computed<ImportPendingDifferenceWithTable[]>(() => {
  return importResult.value?.pendingDifferences || [];
});

const pendingUndecidedCount = computed(() => {
  return pendingDifferences.value.filter(
    item => !differenceDecisionMap.value[item.key]
  ).length;
});

const pendingTableIndexes = computed<number[]>(() => {
  return Array.from(new Set(pendingDifferences.value.map(item => item.tableIndex)));
});

type SkippedPreviewColumn = { index: number; label: string };
type SkippedRowsGroup = {
  tableIndex: number;
  rows: ImportSkippedRowWithTable[];
  columns: SkippedPreviewColumn[];
};

const skippedRowsGroups = computed<SkippedRowsGroup[]>(() => {
  const rows = importResult.value?.skippedRows || [];
  if (rows.length === 0) return [];

  const grouped = new Map<number, ImportSkippedRowWithTable[]>();
  for (const row of rows) {
    const list = grouped.get(row.tableIndex) || [];
    list.push(row);
    grouped.set(row.tableIndex, list);
  }

  return Array.from(grouped.entries())
    .sort((a, b) => a[0] - b[0])
    .map(([tableIndex, groupRows]) => {
      const tableCfg = tableConfigs.value.find(cfg => cfg.tableIndex === tableIndex);
      const headers = tableCfg?.previewData?.headers || tableCfg?.tableInfo?.headers || [];
      const maxColumnCount = groupRows.reduce(
        (max, row) => Math.max(max, row.rowValues?.length || 0),
        0
      );

      const columns: SkippedPreviewColumn[] = Array.from(
        { length: maxColumnCount },
        (_, i) => {
          const header = (headers[i] || "").trim();
          return {
            index: i,
            label: header || `列${i + 1}`
          };
        }
      );

      return {
        tableIndex,
        rows: groupRows,
        columns
      };
    });
});
</script>

<template>
  <div class="page data-import">
    <div class="page-header">
      <div>
        <div class="page-title">数据导入</div>
        <div class="page-subtitle">导入验收规格数据，支持 Word/Excel</div>
      </div>
    </div>
    <!-- 步骤条 -->
    <el-affix v-if="affixTarget" :offset="affixOffset" :target="affixTarget">
      <div class="steps-affix">
        <el-card class="steps-card">
          <el-steps :active="currentStep" finish-status="success">
            <el-step
              v-for="(step, index) in steps"
              :key="index"
              :title="step.title"
              :description="step.description"
            />
          </el-steps>
        </el-card>
      </div>
    </el-affix>
    <div v-else class="steps-affix">
      <el-card class="steps-card">
        <el-steps :active="currentStep" finish-status="success">
          <el-step
            v-for="(step, index) in steps"
            :key="index"
            :title="step.title"
            :description="step.description"
          />
        </el-steps>
      </el-card>
    </div>

    <div class="data-import-body">
      <!-- 步骤内容 -->
      <el-card class="step-content">
      <!-- 步骤1: 上传文件 -->
      <div v-show="currentStep === 0" class="step-panel">
        <h3 class="step-title">上传文件</h3>
        <p class="step-desc">请选择包含验收规格数据的 Word（.docx）或 Excel（.xlsx）文件</p>
        <FileUpload v-model="uploadedFile" @uploaded="handleFileUploaded" />
      </div>

      <!-- 步骤2: 选择表格 -->
      <div v-show="currentStep === 1" class="step-panel">
        <h3 class="step-title">{{ isExcelFile ? "选择工作表" : "选择表格" }}</h3>
        <p class="step-desc">请选择要导入数据的{{ isExcelFile ? "工作表" : "表格" }}（可多选）</p>
        <TableSelector
          v-if="uploadedFile"
          :file-id="uploadedFile.fileId"
          :item-label="isExcelFile ? '工作表' : '表格'"
          multiple
          v-model="selectedTableIndexes"
          @selected-multiple="handleTablesSelected"
        />
      </div>

      <!-- 步骤3: 配置映射 -->
      <div v-show="currentStep === 2" class="step-panel">
        <h3 class="step-title">{{ isExcelFile ? "配置列序号" : "配置列映射" }}</h3>
        <div class="flex items-center justify-between mb-2">
          <p class="step-desc m-0">
            <span v-if="!isExcelFile">
              系统会根据“列映射规则”自动预填映射；若未命中你仍可手动调整
            </span>
            <span v-else>按列序号指定字段（列号 1-based：第 1 列为 A）。</span>
          </p>
          <div v-if="!isExcelFile" class="flex gap-2">
            <el-button
              size="small"
              :loading="loadingMappingRules"
              @click="loadMappingRules"
              >重新加载规则</el-button
            >
            <el-button
              size="small"
              type="primary"
              :disabled="!mappingRules.length"
              @click="applyRulesToAll(true)"
              >重新应用规则</el-button
            >
          </div>
        </div>
        <div v-if="uploadedFile && tableConfigs.length > 0" class="mapping-quick-actions">
          <el-button
            size="small"
            @click="copyActiveMappingConfig"
          >
            复制当前{{ isExcelFile ? "工作表" : "表格" }}配置
          </el-button>
          <el-button
            size="small"
            type="primary"
            :disabled="tableConfigs.length < 2 || !canPasteClipboard"
            @click="pasteMappingConfigToOthers"
          >
            粘贴到其他{{ isExcelFile ? "工作表" : "表格" }}
          </el-button>
          <span v-if="mappingClipboardSourceIndex !== null" class="mapping-clipboard-tip">
            已复制{{ isExcelFile ? "工作表" : "表格" }} {{ mappingClipboardSourceIndex + 1 }} 的配置
          </span>
        </div>

        <el-tabs
          v-if="uploadedFile && tableConfigs.length > 0"
          v-model="activeTableIndex"
          type="border-card"
          closable
          @tab-remove="handleTabRemove"
        >
          <el-tab-pane
            v-for="cfg in tableConfigs"
            :key="cfg.tableIndex"
            :name="cfg.tableIndex"
            :label="`${isExcelFile ? '工作表' : '表格'} ${cfg.tableIndex + 1}`"
          >
            <!-- 表格预览 -->
            <div class="preview-section">
              <h4>{{ isExcelFile ? "工作表预览" : "表格预览" }}</h4>
              <TablePreview
                :file-id="uploadedFile.fileId"
                :table-index="cfg.tableIndex"
                :header-row-index="
                  isExcelFile
                    ? getExcelPreviewOptions(cfg).headerRowIndex
                    : (cfg.wordMapping?.headerRowIndex ?? 0)
                "
                :header-row-count="
                  isExcelFile ? getExcelPreviewOptions(cfg).headerRowCount : 1
                "
                :data-start-row-index="
                  isExcelFile
                    ? getExcelPreviewOptions(cfg).dataStartRowIndex
                    : (cfg.wordMapping?.dataStartRowIndex ?? 1)
                "
                :mapping="isExcelFile ? undefined : cfg.wordMapping"
                @loaded="(data) => handlePreviewLoaded(cfg.tableIndex, data)"
              />
            </div>

            <!-- 列映射配置 -->
            <div class="mapping-section">
              <ExcelColumnMapping
                v-if="isExcelFile"
                :model-value="cfg.excelMapping"
                :used-range-start-row="cfg.tableInfo?.usedRangeStartRow"
                :used-range-start-column="cfg.tableInfo?.usedRangeStartColumn"
                @update:model-value="(value) => updateExcelMapping(cfg.tableIndex, value)"
              />
              <ColumnMapping
                v-else
                :table-data="cfg.previewData"
                v-model="cfg.wordMapping"
              />
            </div>
          </el-tab-pane>
        </el-tabs>
      </div>

      <!-- 步骤4: 选择目标 -->
      <div v-show="currentStep === 3" class="step-panel">
        <h3 class="step-title">选择导入目标</h3>
        <p class="step-desc">请选择数据要导入的客户、制程与机型（制程/机型可选）</p>

        <el-form label-width="100px" class="target-form">
          <el-form-item label="选择客户" required>
            <el-select
              v-model="selectedCustomerId"
              placeholder="请选择客户"
              :loading="loadingCustomers"
              filterable
              class="dialog-select dialog-select--320"
              popper-class="app-select-popper"
            >
              <el-option
                v-for="customer in customers"
                :key="customer.id"
                :label="customer.name"
                :value="customer.id"
              />
            </el-select>
          </el-form-item>

          <el-form-item label="选择制程">
            <el-select
              v-model="selectedProcessId"
              placeholder="请选择制程（可选）"
              :loading="loadingProcesses"
              filterable
              class="dialog-select dialog-select--320"
              popper-class="app-select-popper"
            >
              <el-option
                v-for="process in processes"
                :key="process.id"
                :label="process.name"
                :value="process.id"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="选择机型">
            <el-select
              v-model="selectedMachineModelId"
              placeholder="请选择机型（可选）"
              :loading="loadingMachineModels"
              filterable
              class="dialog-select dialog-select--320"
              popper-class="app-select-popper"
            >
              <el-option
                v-for="model in machineModels"
                :key="model.id"
                :label="model.name"
                :value="model.id"
              />
            </el-select>
          </el-form-item>
        </el-form>
      </div>

      <!-- 步骤5: 确认导入 -->
      <div v-show="currentStep === 4" class="step-panel">
        <h3 class="step-title">确认导入</h3>
        <p class="step-desc">请确认以下导入信息</p>

        <!-- 导入结果 -->
        <div v-if="importResult" class="import-result">
          <el-result
            :icon="importResult.requiresConfirmation ? 'warning' : (importResult.failedCount === 0 ? 'success' : 'warning')"
            :title="importResult.requiresConfirmation ? '发现差异，待确认' : (importResult.failedCount === 0 ? '导入成功' : '导入完成')"
          >
            <template #sub-title>
              <div class="result-stats">
                <div class="stat-item success">
                  <span class="stat-value">{{ importResult.successCount }}</span>
                  <span class="stat-label">成功</span>
                </div>
                <div class="stat-item warning">
                  <span class="stat-value">{{ importResult.skippedCount }}</span>
                  <span class="stat-label">跳过</span>
                </div>
                <div class="stat-item danger">
                  <span class="stat-value">{{ importResult.failedCount }}</span>
                  <span class="stat-label">失败</span>
                </div>
                <div v-if="importResult.requiresConfirmation" class="stat-item warning">
                  <span class="stat-value">{{ importResult.pendingCount || 0 }}</span>
                  <span class="stat-label">待确认</span>
                </div>
              </div>
            </template>
            <template #extra>
              <el-button
                v-if="importResult.requiresConfirmation"
                type="primary"
                :loading="importing"
                :disabled="pendingUndecidedCount > 0"
                @click="handleImport"
              >
                {{ importing ? "处理中..." : "确认差异并导入" }}
              </el-button>
              <el-button type="primary" @click="handleRestart">继续导入</el-button>
            </template>
          </el-result>

          <div
            v-if="importResult.requiresConfirmation && importResult.pendingDifferences.length > 0"
            class="error-list"
          >
            <h4>差异确认</h4>
            <el-alert
              type="warning"
              :closable="false"
              show-icon
              :title="`检测到 ${importResult.pendingCount || 0} 条与数据库已有数据不一致，请逐条选择“导入”或“跳过”。`"
            />
            <el-table
              :data="importResult.pendingDifferences"
              max-height="320"
              size="small"
              style="margin-top: 12px"
            >
              <el-table-column prop="tableIndex" label="表格" width="70">
                <template #default="{ row }">
                  {{ row.tableIndex + 1 }}
                </template>
              </el-table-column>
              <el-table-column prop="rowIndex" label="行号" width="80" />
              <el-table-column prop="incomingProject" label="导入-项目" min-width="140" />
              <el-table-column prop="incomingSpecification" label="导入-规格" min-width="180" />
              <el-table-column prop="incomingAcceptance" label="导入-验收" min-width="130">
                <template #default="{ row }">
                  {{ row.incomingAcceptance || "-" }}
                </template>
              </el-table-column>
              <el-table-column prop="existingProject" label="库中-项目" min-width="140" />
              <el-table-column prop="existingSpecification" label="库中-规格" min-width="180" />
              <el-table-column prop="existingAcceptance" label="库中-验收" min-width="130">
                <template #default="{ row }">
                  {{ row.existingAcceptance || "-" }}
                </template>
              </el-table-column>
              <el-table-column label="处理" width="180" fixed="right">
                <template #default="{ row }">
                  <el-radio-group v-model="differenceDecisionMap[row.key]" size="small">
                    <el-radio-button label="import">导入</el-radio-button>
                    <el-radio-button label="skip">跳过</el-radio-button>
                  </el-radio-group>
                </template>
              </el-table-column>
            </el-table>
            <div class="pending-tip">未选择条数：{{ pendingUndecidedCount }}</div>
          </div>

          <!-- 错误详情 -->
          <div v-if="importResult.errors.length > 0" class="error-list">
            <h4>错误详情</h4>
            <el-table :data="importResult.errors" max-height="200" size="small">
              <el-table-column prop="tableIndex" label="表格" width="80">
                <template #default="{ row }">
                  {{ row.tableIndex + 1 }}
                </template>
              </el-table-column>
              <el-table-column prop="rowIndex" label="行号" width="80">
                <template #default="{ row }">
                  {{ row.rowIndex + 1 }}
                </template>
              </el-table-column>
              <el-table-column prop="message" label="错误信息" />
            </el-table>
          </div>

          <div v-if="importResult.skippedCount > 0" class="error-list">
            <h4>未导入（跳过）详情</h4>
            <el-alert
              v-if="!importResult.skippedRows.length"
              type="info"
              :closable="false"
              show-icon
              title="已跳过部分数据（未开启明细预览）"
              description="如需查看具体哪些行被跳过，请在导入前开启“预览未导入明细”。"
            />
            <div v-else>
              <div
                v-for="group in skippedRowsGroups"
                :key="`skip-group-${group.tableIndex}`"
                class="skipped-group"
              >
                <div v-if="skippedRowsGroups.length > 1" class="skipped-group-title">
                  表格 {{ group.tableIndex + 1 }}
                </div>
                <el-table :data="group.rows" max-height="220" size="small">
                  <el-table-column prop="tableIndex" label="表格" width="80">
                    <template #default="{ row }">
                      {{ row.tableIndex + 1 }}
                    </template>
                  </el-table-column>
                  <el-table-column prop="rowIndex" label="行号" width="100" />
                  <el-table-column
                    prop="message"
                    label="跳过原因"
                    min-width="220"
                    show-overflow-tooltip
                  />
                  <el-table-column
                    v-for="col in group.columns"
                    :key="`skip-col-${group.tableIndex}-${col.index}`"
                    :label="col.label"
                    min-width="140"
                  >
                    <template #default="{ row }">
                      <div class="skipped-cell-value">{{ row.rowValues?.[col.index] || "" }}</div>
                    </template>
                  </el-table-column>
                </el-table>
              </div>
            </div>
          </div>
        </div>

        <!-- 导入确认 -->
        <div v-else class="import-confirm">
          <el-descriptions class="import-confirm-desc" :column="3" border size="small">
            <el-descriptions-item label="源文件" :span="2">
              {{ uploadedFile?.fileName }}
            </el-descriptions-item>
            <el-descriptions-item label="表格">
              共 {{ tableConfigs.length }} 个（{{
                tableConfigs.map(t => t.tableIndex + 1).join("、")
              }}）
            </el-descriptions-item>
            <el-descriptions-item label="目标客户">
              {{ customers.find((c) => c.id === selectedCustomerId)?.name }}
            </el-descriptions-item>
            <el-descriptions-item label="目标制程">
              {{ processes.find((p) => p.id === selectedProcessId)?.name || "-" }}
            </el-descriptions-item>
            <el-descriptions-item label="目标机型">
              {{ machineModels.find((m) => m.id === selectedMachineModelId)?.name || "-" }}
            </el-descriptions-item>
            <el-descriptions-item label="预计导入">
              约 {{ previewDataCount }} 条数据
            </el-descriptions-item>
          </el-descriptions>

          <div class="import-actions">
            <div class="skip-preview-switch">
              <span class="label">预览未导入明细</span>
              <el-switch
                v-model="previewSkippedRows"
                :disabled="importing"
                active-text="开启"
                inactive-text="关闭"
              />
            </div>
            <el-button
              type="primary"
              size="large"
              :loading="importing"
              @click="handleImport"
            >
              {{ importing ? "导入中..." : "开始导入" }}
            </el-button>
          </div>
        </div>
      </div>

      <!-- 步骤按钮 -->
      <div class="step-actions">
        <el-button v-if="currentStep > 0 && !importResult" @click="goPrev">
          上一步
        </el-button>
        <el-button
          v-if="currentStep < steps.length - 1"
          type="primary"
          :disabled="nextDisabled"
          @click="goNext"
        >
          下一步
        </el-button>
      </div>
      </el-card>
    </div>
  </div>
</template>

<style scoped>
.data-import {
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.steps-affix {
  width: 100%;
  background: var(--el-bg-color);
  /* 不要顶部 padding，否则 Affix 需要先滚动一段才会触发“固定” */
  padding: 0 0 16px;
  /* 让固定时更有层次感，避免和内容“糊在一起” */
  border-bottom: 1px solid var(--el-border-color-lighter);
  /* 防止底下内容滚动时“透出来” */
  box-shadow: 0 2px 8px rgb(0 0 0 / 6%);
  z-index: 900;
}

.data-import-body {
  padding: 0;
  /* 给固定底部操作栏预留空间，避免遮挡内容 */
  padding-bottom: 84px;
  padding-top: 4px;
}

.steps-card {
  margin-bottom: 0;
}

.step-content {
  min-height: 500px;
}

.step-panel {
  padding: 20px 0;
}

.step-title {
  font-size: 18px;
  font-weight: 600;
  color: var(--color-text);
  margin-bottom: 8px;
}

.step-desc {
  font-size: 14px;
  color: #6b7280;
  margin-bottom: 24px;
}

.preview-section {
  margin-bottom: 24px;
}

.preview-section h4,
.error-list h4 {
  font-size: 14px;
  font-weight: 500;
  color: #4b5563;
  margin-bottom: 12px;
}

.mapping-section {
  margin-top: 24px;
}

.mapping-quick-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
}

.mapping-clipboard-tip {
  font-size: 12px;
  color: #6b7280;
}

.target-form {
  max-width: 500px;
}

.w-full {
  width: 100%;
}

.import-confirm {
  width: 100%;
  max-width: 900px;
  margin: 0 auto;
}

.import-confirm-desc {
  width: 100%;
}

.import-confirm-desc :deep(.el-descriptions__cell) {
  padding: 6px 10px;
}

.import-confirm-desc :deep(.el-descriptions__label) {
  width: 80px;
  color: #6b7280;
}

.import-actions {
  margin-top: 20px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
}

.skip-preview-switch {
  display: flex;
  align-items: center;
  gap: 10px;
  color: #4b5563;
  font-size: 13px;
}

.skip-preview-switch .label {
  font-weight: 500;
}

.import-result {
  width: 100%;
  max-width: 1200px;
  margin: 0 auto;
}

.result-stats {
  display: flex;
  justify-content: center;
  gap: 48px;
  margin-top: 16px;
}

.stat-item {
  text-align: center;
}

.stat-value {
  display: block;
  font-size: 32px;
  font-weight: 600;
}

.stat-label {
  display: block;
  font-size: 14px;
  color: #6b7280;
  margin-top: 4px;
}

.stat-item.success .stat-value {
  color: #67c23a;
}

.stat-item.warning .stat-value {
  color: #e6a23c;
}

.stat-item.danger .stat-value {
  color: #f56c6c;
}

.error-list {
  margin-top: 24px;
}

.pending-tip {
  margin-top: 8px;
  font-size: 12px;
  color: #6b7280;
}

.skipped-group + .skipped-group {
  margin-top: 12px;
}

.skipped-group-title {
  margin-bottom: 8px;
  font-size: 12px;
  color: #6b7280;
}

.skipped-cell-value {
  white-space: pre-wrap;
  word-break: break-word;
  color: #4b5563;
  line-height: 1.5;
  font-size: 12px;
}

.step-actions {
  position: fixed;
  left: 0;
  right: 0;
  bottom: 0;
  z-index: 1000;
  margin-top: 0;
  padding: 12px 0;
  border-top: 1px solid #e4e7ed;
  background: var(--el-bg-color);
  display: flex;
  justify-content: center;
  gap: 16px;
}
</style>
