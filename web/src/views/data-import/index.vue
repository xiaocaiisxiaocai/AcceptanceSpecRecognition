<script setup lang="ts">
import { ref, computed, watch, onActivated, onMounted, nextTick } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import FileUpload from "./components/FileUpload.vue";
import TableSelector from "./components/TableSelector.vue";
import TablePreview from "./components/TablePreview.vue";
import ColumnMapping from "./components/ColumnMapping.vue";
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
import {
  importData,
  type FileUploadResponse,
  type TableInfo,
  type TableData,
  type ColumnMapping as ColumnMappingType,
  type ImportResult
} from "@/api/document";

defineOptions({
  name: "DataImport"
});

// 步骤
const currentStep = ref(0);
const steps = [
  { title: "上传文件", description: "选择Word文档" },
  { title: "选择表格", description: "选择要导入的表格" },
  { title: "配置映射", description: "设置列映射关系" },
  { title: "选择目标", description: "选择导入目标" },
  { title: "确认导入", description: "预览并确认" }
];

// 文件上传
const uploadedFile = ref<FileUploadResponse | null>(null);

// 表格选择（支持多选）
const selectedTableIndexes = ref<number[]>([]);
const selectedTables = ref<TableInfo[]>([]);
const activeTableIndex = ref<number | null>(null);

type TableImportConfig = {
  tableIndex: number;
  tableInfo?: TableInfo;
  mapping: ColumnMappingType;
  previewData: TableData | null;
};

const defaultMapping = (): ColumnMappingType => ({
  projectColumn: undefined,
  specificationColumn: undefined,
  acceptanceColumn: undefined,
  remarkColumn: undefined,
  headerRowIndex: 0,
  dataStartRowIndex: 1
});

const tableConfigs = ref<TableImportConfig[]>([]);

// 全局列映射规则（用于自动预填）
const mappingRules = ref<ColumnMappingRule[]>([]);
const loadingMappingRules = ref(false);

// 目标选择
const customers = ref<Customer[]>([]);
const processes = ref<Process[]>([]);
const selectedCustomerId = ref<number | undefined>(undefined);
const selectedProcessId = ref<number | undefined>(undefined);
const loadingCustomers = ref(false);
const loadingProcesses = ref(false);

// 导入结果
const importing = ref(false);
type ImportErrorWithTable = { tableIndex: number } & ImportResult["errors"][number];
type CombinedImportResult = Omit<ImportResult, "errors"> & {
  errors: ImportErrorWithTable[];
};
const importResult = ref<CombinedImportResult | null>(null);

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
      return selectedTableIndexes.value.length > 0;
    case 2:
      // 映射步骤允许点击“下一步”，在 goNext 中做校验并提示缺失项，避免按钮直接置灰导致“卡死”
      return tableConfigs.value.length > 0;
    case 3:
      return (
        selectedCustomerId.value !== undefined && selectedProcessId.value !== undefined
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

const validateAllTableMappings = () => {
  const missingByTable = tableConfigs.value
    .map(cfg => ({
      tableIndex: cfg.tableIndex,
      missing: getMissingMappingFields(cfg.mapping)
    }))
    .filter(x => x.missing.length > 0);

  return {
    ok: missingByTable.length === 0,
    missingByTable
  };
};

const nextDisabled = computed(() => {
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
        ? { ...old, tableInfo: t }
        : {
            tableIndex: t.index,
            tableInfo: t,
            mapping: defaultMapping(),
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
  const next: ColumnMappingType = { ...cfg.mapping };

  const setIfNeed = (key: keyof ColumnMappingType, val?: number) => {
    if (val === undefined) return;
    if (overwrite || next[key] === undefined) {
      // @ts-expect-error - key is one of numeric fields
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

  cfg.mapping = next;
};

const applyRulesToAll = (overwrite: boolean) => {
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

// 监听步骤变化
watch(currentStep, (step) => {
  if (step === 2 && mappingRules.value.length === 0 && !loadingMappingRules.value) {
    loadMappingRules();
  }
  if (step === 3 && customers.value.length === 0) {
    loadCustomers();
  }
  if (step === 3 && processes.value.length === 0) {
    loadProcesses();
  }
});

// 兼容 keep-alive：从“列映射规则”页面返回导入页时，确保规则能刷新并重新预填
onActivated(() => {
  if (currentStep.value === 2) {
    loadMappingRules();
  }
});

// 下一步
const goNext = () => {
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

  if (currentStep.value < steps.length - 1) currentStep.value++;
};

// 上一步
const goPrev = () => {
  if (currentStep.value > 0) {
    currentStep.value--;
  }
};

// 执行导入
const handleImport = async () => {
  if (
    !uploadedFile.value ||
    tableConfigs.value.length === 0 ||
    !selectedCustomerId.value ||
    !selectedProcessId.value
  ) {
    return;
  }

  try {
    await ElMessageBox.confirm(
      `确定要将 ${tableConfigs.value.length} 个表格的数据导入到所选客户/制程吗？`,
      "确认导入",
      {
        confirmButtonText: "确定",
        cancelButtonText: "取消",
        type: "warning"
      }
    );

    importing.value = true;
    const aggregate: CombinedImportResult = {
      successCount: 0,
      failedCount: 0,
      skippedCount: 0,
      totalCount: 0,
      errors: []
    };

    for (const cfg of tableConfigs.value) {
      const res = await importData({
        fileId: uploadedFile.value.fileId,
        tableIndex: cfg.tableIndex,
        customerId: selectedCustomerId.value,
        processId: selectedProcessId.value,
        mapping: cfg.mapping
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

      aggregate.successCount += res.data.successCount;
      aggregate.failedCount += res.data.failedCount;
      aggregate.skippedCount += res.data.skippedCount;
      aggregate.totalCount += res.data.totalCount;
      aggregate.errors.push(
        ...(res.data.errors || []).map(e => ({
          tableIndex: cfg.tableIndex,
          ...e
        }))
      );
    }

    importResult.value = aggregate;
    ElMessage.success(
      `导入完成：成功${aggregate.successCount}条，失败${aggregate.failedCount}条`
    );
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
  importResult.value = null;
};

// 预览数据条数
const previewDataCount = computed(() => {
  if (tableConfigs.value.length === 0) return 0;
  return tableConfigs.value.reduce((sum, cfg) => {
    if (!cfg.previewData) return sum;
    return sum + (cfg.previewData.totalRows - (cfg.mapping.dataStartRowIndex ?? 1));
  }, 0);
});
</script>

<template>
  <div class="data-import">
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
        <h3 class="step-title">上传Word文档</h3>
        <p class="step-desc">请选择包含验收规格数据的Word文档（.docx格式）</p>
        <FileUpload v-model="uploadedFile" @uploaded="handleFileUploaded" />
      </div>

      <!-- 步骤2: 选择表格 -->
      <div v-show="currentStep === 1" class="step-panel">
        <h3 class="step-title">选择表格</h3>
        <p class="step-desc">请选择要导入数据的表格（可多选）</p>
        <TableSelector
          v-if="uploadedFile"
          :file-id="uploadedFile.fileId"
          multiple
          v-model="selectedTableIndexes"
          @selected-multiple="handleTablesSelected"
        />
      </div>

      <!-- 步骤3: 配置映射 -->
      <div v-show="currentStep === 2" class="step-panel">
        <h3 class="step-title">配置列映射</h3>
        <div class="flex items-center justify-between mb-2">
          <p class="step-desc m-0">
            系统会根据“列映射规则”自动预填映射；若未命中你仍可手动调整
          </p>
          <div class="flex gap-2">
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
            :label="`表格 ${cfg.tableIndex + 1}`"
          >
            <!-- 表格预览 -->
            <div class="preview-section">
              <h4>表格预览</h4>
              <TablePreview
                :file-id="uploadedFile.fileId"
                :table-index="cfg.tableIndex"
                :header-row-index="cfg.mapping.headerRowIndex"
                :data-start-row-index="cfg.mapping.dataStartRowIndex"
                :mapping="cfg.mapping"
                @loaded="(data) => handlePreviewLoaded(cfg.tableIndex, data)"
              />
            </div>

            <!-- 列映射配置 -->
            <div class="mapping-section">
              <ColumnMapping :table-data="cfg.previewData" v-model="cfg.mapping" />
            </div>
          </el-tab-pane>
        </el-tabs>
      </div>

      <!-- 步骤4: 选择目标 -->
      <div v-show="currentStep === 3" class="step-panel">
        <h3 class="step-title">选择导入目标</h3>
        <p class="step-desc">请选择数据要导入的客户和制程</p>

        <el-form label-width="100px" class="target-form">
          <el-form-item label="选择客户" required>
            <el-select
              v-model="selectedCustomerId"
              placeholder="请选择客户"
              :loading="loadingCustomers"
              filterable
              class="w-full"
            >
              <el-option
                v-for="customer in customers"
                :key="customer.id"
                :label="customer.name"
                :value="customer.id"
              />
            </el-select>
          </el-form-item>

          <el-form-item label="选择制程" required>
            <el-select
              v-model="selectedProcessId"
              placeholder="请选择制程"
              :loading="loadingProcesses"
              filterable
              class="w-full"
            >
              <el-option
                v-for="process in processes"
                :key="process.id"
                :label="process.name"
                :value="process.id"
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
            :icon="importResult.failedCount === 0 ? 'success' : 'warning'"
            :title="importResult.failedCount === 0 ? '导入成功' : '导入完成'"
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
              </div>
            </template>
            <template #extra>
              <el-button type="primary" @click="handleRestart">继续导入</el-button>
            </template>
          </el-result>

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
        </div>

        <!-- 导入确认 -->
        <div v-else class="import-confirm">
          <el-descriptions :column="2" border>
            <el-descriptions-item label="源文件">
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
              {{ processes.find((p) => p.id === selectedProcessId)?.name }}
            </el-descriptions-item>
            <el-descriptions-item label="预计导入">
              约 {{ previewDataCount }} 条数据
            </el-descriptions-item>
          </el-descriptions>

          <div class="import-actions">
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
  padding: 0;
}

.steps-affix {
  width: 100%;
  background: var(--el-bg-color);
  /* 不要顶部 padding，否则 Affix 需要先滚动一段才会触发“固定” */
  padding: 0 20px 16px;
  /* 让固定时更有层次感，避免和内容“糊在一起” */
  border-bottom: 1px solid var(--el-border-color-lighter);
  /* 防止底下内容滚动时“透出来” */
  box-shadow: 0 2px 8px rgb(0 0 0 / 6%);
  z-index: 900;
}

.data-import-body {
  padding: 0 20px;
  /* 给固定底部操作栏预留空间，避免遮挡内容 */
  padding-bottom: 84px;
  padding-top: 20px;
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
  color: #303133;
  margin-bottom: 8px;
}

.step-desc {
  font-size: 14px;
  color: #909399;
  margin-bottom: 24px;
}

.preview-section {
  margin-bottom: 24px;
}

.preview-section h4,
.error-list h4 {
  font-size: 14px;
  font-weight: 500;
  color: #606266;
  margin-bottom: 12px;
}

.mapping-section {
  margin-top: 24px;
}

.target-form {
  max-width: 500px;
}

.w-full {
  width: 100%;
}

.import-confirm {
  max-width: 600px;
  margin: 0 auto;
}

.import-actions {
  margin-top: 32px;
  text-align: center;
}

.import-result {
  max-width: 600px;
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
  color: #909399;
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
