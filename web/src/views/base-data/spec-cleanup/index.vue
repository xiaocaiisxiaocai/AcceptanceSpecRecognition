<script setup lang="ts">
import {
  computed,
  nextTick,
  onBeforeUnmount,
  onMounted,
  reactive,
  ref,
  watch
} from "vue";
import { useRouter } from "vue-router";
import {
  Check,
  CircleCheck,
  Close,
  Delete,
  FirstAidKit,
  Refresh,
  RefreshLeft,
  WarningFilled
} from "@element-plus/icons-vue";
import { ElMessage, ElMessageBox } from "element-plus";
import type { TableInstance } from "element-plus";
import {
  SpecCleanupCategory,
  SpecCleanupScanStatus,
  cancelSpecCleanupScan,
  getQuarantinedSpecs,
  getIgnoredSpecs,
  getSpecCleanupScanItems,
  getSpecCleanupScanStatus,
  ignoreSpecCleanupItems,
  keepSpecCleanupItems,
  permanentlyDeleteQuarantinedSpecs,
  quarantineSpecCleanupItems,
  restoreQuarantinedSpecs,
  startSpecCleanupScan,
  unignoreSpecs,
  type IgnoredAcceptanceSpec,
  type QuarantinedAcceptanceSpec,
  type SpecCleanupBatchResult,
  type SpecCleanupScanItem,
  type SpecCleanupScanStatusResponse
} from "@/api/spec-cleanup";
import { hasPerms } from "@/utils/auth";
import { formatApiUtcDateTime } from "@/utils/date-time";
import { getRequestErrorMessage } from "@/utils/error-message";
import { isMessageBoxCancel } from "@/utils/message-box";
import {
  cleanupProgress,
  cleanupReasonLabel,
  cleanupStatusLabel,
  failedActionItemIds
} from "./cleanup-view";

defineOptions({ name: "AcceptanceSpecCleanup" });

const props = withDefaults(defineProps<{ embedded?: boolean }>(), {
  embedded: false
});

type CleanupView =
  | "recommended"
  | "review"
  | "healthy"
  | "ignored"
  | "quarantine";

const router = useRouter();
const thresholds = reactive({ newItemGraceDays: 30, unusedDays: 365 });
const scan = ref<SpecCleanupScanStatusResponse | null>(null);
const scanItems = ref<SpecCleanupScanItem[]>([]);
const quarantinedItems = ref<QuarantinedAcceptanceSpec[]>([]);
const ignoredItems = ref<IgnoredAcceptanceSpec[]>([]);
const selectedScanItems = ref<SpecCleanupScanItem[]>([]);
const selectedQuarantinedItems = ref<QuarantinedAcceptanceSpec[]>([]);
const selectedIgnoredItems = ref<IgnoredAcceptanceSpec[]>([]);
const scanTable = ref<TableInstance>();
const quarantineTable = ref<TableInstance>();
const ignoredTable = ref<TableInstance>();
const activeView = ref<CleanupView>("recommended");
const loading = ref(false);
const actionLoading = ref(false);
const total = ref(0);
const page = ref(1);
const pageSize = ref(50);
const lastBatchResult = ref<SpecCleanupBatchResult | null>(null);
let pollTimer: number | undefined;

const canRead = computed(() => hasPerms("api:spec-cleanup:read"));
const canScan = computed(() => hasPerms("btn:spec-cleanup:scan"));
const canCancel = computed(() => hasPerms("btn:spec-cleanup:cancel"));
const canKeep = computed(() => hasPerms("btn:spec-cleanup:keep"));
const canIgnore = computed(() => hasPerms("btn:spec-cleanup:ignore"));
const canQuarantine = computed(() => hasPerms("btn:spec-cleanup:quarantine"));
const canRestore = computed(() => hasPerms("btn:spec-cleanup:restore"));
const canUnignore = computed(() => hasPerms("btn:spec-cleanup:unignore"));
const canPermanentDelete = computed(() =>
  hasPerms("btn:spec-cleanup:permanent-delete")
);
const isScanning = computed(
  () =>
    scan.value?.status === SpecCleanupScanStatus.Pending ||
    scan.value?.status === SpecCleanupScanStatus.Running
);
const progress = computed(() =>
  cleanupProgress(scan.value?.processedCount ?? 0, scan.value?.totalCount ?? 0)
);
const scanSelectionCount = computed(() => selectedScanItems.value.length);
const quarantineSelectionCount = computed(
  () => selectedQuarantinedItems.value.length
);
const ignoredSelectionCount = computed(() => selectedIgnoredItems.value.length);
const selectedCanPermanentlyDelete = computed(
  () =>
    selectedQuarantinedItems.value.length > 0 &&
    selectedQuarantinedItems.value.every(
      item => new Date(item.quarantineExpiresAtUtc).getTime() <= Date.now()
    )
);
const category = computed(() => {
  if (activeView.value === "review") return SpecCleanupCategory.ManualReview;
  if (activeView.value === "healthy") return SpecCleanupCategory.Healthy;
  return SpecCleanupCategory.RecommendedCleanup;
});
const segments = computed(() => [
  {
    label: `建议清理 ${scan.value?.recommendedCleanupCount ?? 0}`,
    value: "recommended"
  },
  {
    label: `人工确认 ${scan.value?.manualReviewCount ?? 0}`,
    value: "review"
  },
  { label: `正常 ${scan.value?.healthyCount ?? 0}`, value: "healthy" },
  { label: "已忽略", value: "ignored" },
  { label: "隔离区", value: "quarantine" }
]);

const clearPoll = () => {
  if (pollTimer != null) window.clearTimeout(pollTimer);
  pollTimer = undefined;
};

const schedulePoll = () => {
  clearPoll();
  pollTimer = window.setTimeout(pollStatus, 500);
};

const pollStatus = async () => {
  if (!scan.value?.id) return;
  try {
    const response = await getSpecCleanupScanStatus(scan.value.id);
    if (response.code !== 0 || !response.data) {
      throw new Error(response.message || "读取扫描状态失败");
    }
    scan.value = response.data;
    if (isScanning.value) {
      schedulePoll();
    } else {
      await loadCurrentView();
    }
  } catch (error) {
    ElMessage.error(getRequestErrorMessage(error, "读取扫描状态失败"));
  }
};

const startScan = async () => {
  if (thresholds.unusedDays <= thresholds.newItemGraceDays) {
    ElMessage.warning("长期未引用阈值必须大于新数据保护期");
    return;
  }
  actionLoading.value = true;
  lastBatchResult.value = null;
  try {
    const response = await startSpecCleanupScan({ ...thresholds });
    if (response.code !== 0 || !response.data) {
      throw new Error(response.message || "启动扫描失败");
    }
    scan.value = response.data;
    activeView.value = "recommended";
    page.value = 1;
    scanItems.value = [];
    total.value = 0;
    schedulePoll();
  } catch (error) {
    ElMessage.error(getRequestErrorMessage(error, "启动扫描失败"));
  } finally {
    actionLoading.value = false;
  }
};

const cancelScan = async () => {
  if (!scan.value?.id) return;
  try {
    await cancelSpecCleanupScan(scan.value.id);
    await pollStatus();
  } catch (error) {
    ElMessage.error(getRequestErrorMessage(error, "取消扫描失败"));
  }
};

const clearSelections = () => {
  scanTable.value?.clearSelection();
  quarantineTable.value?.clearSelection();
  ignoredTable.value?.clearSelection();
  selectedScanItems.value = [];
  selectedQuarantinedItems.value = [];
  selectedIgnoredItems.value = [];
};

const loadCurrentView = async (scanItemIdsToReselect: number[] = []) => {
  loading.value = true;
  clearSelections();
  try {
    if (activeView.value === "quarantine") {
      const response = await getQuarantinedSpecs({
        page: page.value,
        pageSize: pageSize.value
      });
      if (response.code !== 0 || !response.data) {
        throw new Error(response.message || "读取隔离区失败");
      }
      quarantinedItems.value = response.data.items;
      total.value = response.data.total;
      return;
    }
    if (activeView.value === "ignored") {
      const response = await getIgnoredSpecs({
        page: page.value,
        pageSize: pageSize.value
      });
      if (response.code !== 0 || !response.data) {
        throw new Error(response.message || "读取已忽略规格失败");
      }
      ignoredItems.value = response.data.items;
      total.value = response.data.total;
      return;
    }
    if (!scan.value?.id) {
      scanItems.value = [];
      total.value = 0;
      return;
    }
    const response = await getSpecCleanupScanItems(scan.value.id, {
      category: category.value,
      page: page.value,
      pageSize: pageSize.value
    });
    if (response.code !== 0 || !response.data) {
      throw new Error(response.message || "读取扫描结果失败");
    }
    scanItems.value = response.data.items;
    total.value = response.data.total;
    if (scanItemIdsToReselect.length) {
      const failedIds = new Set(scanItemIdsToReselect);
      await nextTick();
      scanItems.value
        .filter(item => failedIds.has(item.id))
        .forEach(item => scanTable.value?.toggleRowSelection(item, true));
    }
  } catch (error) {
    ElMessage.error(getRequestErrorMessage(error, "读取清理数据失败"));
  } finally {
    loading.value = false;
  }
};

const runScanAction = async (action: "keep" | "ignore" | "quarantine") => {
  if (!selectedScanItems.value.length) return;
  const handlers = {
    keep: keepSpecCleanupItems,
    ignore: ignoreSpecCleanupItems,
    quarantine: quarantineSpecCleanupItems
  };
  const labels = {
    keep: "保留",
    ignore: "忽略后续扫描",
    quarantine: "移入隔离区"
  };
  try {
    if (action !== "keep") {
      await ElMessageBox.confirm(
        `确认将已选 ${selectedScanItems.value.length} 项${labels[action]}？`,
        labels[action],
        {
          type: action === "quarantine" ? "warning" : "info",
          confirmButtonText: labels[action],
          cancelButtonText: "取消"
        }
      );
    }
    actionLoading.value = true;
    const response = await handlers[action](
      selectedScanItems.value.map(item => ({ scanItemId: item.id }))
    );
    if (response.code !== 0 || !response.data) {
      throw new Error(response.message || `${labels[action]}失败`);
    }
    lastBatchResult.value = response.data;
    ElMessage.success(`成功 ${response.data.successCount} 项`);
    await loadCurrentView(failedActionItemIds(response.data.items));
  } catch (error) {
    if (!isMessageBoxCancel(error)) {
      ElMessage.error(getRequestErrorMessage(error, `${labels[action]}失败`));
    }
  } finally {
    actionLoading.value = false;
  }
};

const restoreSelected = async () => {
  if (!selectedQuarantinedItems.value.length) return;
  actionLoading.value = true;
  try {
    const response = await restoreQuarantinedSpecs(
      selectedQuarantinedItems.value.map(item => item.id)
    );
    if (response.code !== 0 || !response.data) {
      throw new Error(response.message || "恢复失败");
    }
    lastBatchResult.value = response.data;
    ElMessage.success(`已恢复 ${response.data.successCount} 项`);
    await loadCurrentView();
  } catch (error) {
    ElMessage.error(getRequestErrorMessage(error, "恢复失败"));
  } finally {
    actionLoading.value = false;
  }
};

const unignoreSelected = async () => {
  if (!selectedIgnoredItems.value.length) return;
  actionLoading.value = true;
  try {
    const response = await unignoreSpecs(
      selectedIgnoredItems.value.map(item => item.id)
    );
    if (response.code !== 0 || !response.data) {
      throw new Error(response.message || "重新纳入扫描失败");
    }
    lastBatchResult.value = response.data;
    ElMessage.success(`已重新纳入扫描 ${response.data.successCount} 项`);
    await loadCurrentView();
  } catch (error) {
    ElMessage.error(getRequestErrorMessage(error, "重新纳入扫描失败"));
  } finally {
    actionLoading.value = false;
  }
};

const permanentlyDeleteSelected = async () => {
  if (!selectedCanPermanentlyDelete.value) return;
  try {
    await ElMessageBox.confirm(
      `永久删除已选 ${selectedQuarantinedItems.value.length} 项及其版本、引用历史和缓存，此操作不可恢复。`,
      "永久删除",
      {
        type: "error",
        confirmButtonText: "确认永久删除",
        cancelButtonText: "取消"
      }
    );
    actionLoading.value = true;
    const response = await permanentlyDeleteQuarantinedSpecs(
      selectedQuarantinedItems.value.map(item => ({
        specId: item.id,
        referenceVersion: item.referenceVersion
      }))
    );
    if (response.code !== 0 || !response.data) {
      throw new Error(response.message || "永久删除失败");
    }
    lastBatchResult.value = response.data;
    ElMessage.success(`已永久删除 ${response.data.successCount} 项`);
    await loadCurrentView();
  } catch (error) {
    if (!isMessageBoxCancel(error)) {
      ElMessage.error(getRequestErrorMessage(error, "永久删除失败"));
    }
  } finally {
    actionLoading.value = false;
  }
};

const isExpired = (value: string) => new Date(value).getTime() <= Date.now();
const handlePageChange = (value: number) => {
  page.value = value;
  void loadCurrentView();
};
const handlePageSizeChange = (value: number) => {
  pageSize.value = value;
  page.value = 1;
  void loadCurrentView();
};

watch(activeView, () => {
  page.value = 1;
  lastBatchResult.value = null;
  void loadCurrentView();
});

onMounted(() => {
  if (!canRead.value) ElMessage.error("缺少验规清理查看权限");
});
onBeforeUnmount(clearPoll);
</script>

<template>
  <div class="cleanup-page" :class="{ embedded: props.embedded }">
    <header class="cleanup-header">
      <div class="title-group">
        <el-button
          v-if="!props.embedded"
          :icon="RefreshLeft"
          text
          circle
          title="返回验收规格"
          @click="router.push('/base-data/specs')"
        />
        <div class="title-mark">
          <el-icon><FirstAidKit /></el-icon>
        </div>
        <div>
          <h1>验规清理</h1>
          <span>{{ scan ? cleanupStatusLabel(scan.status) : "等待扫描" }}</span>
        </div>
      </div>
      <div class="scan-controls">
        <div class="threshold-control">
          <label>保护期</label>
          <el-input-number
            v-model="thresholds.newItemGraceDays"
            :min="1"
            :max="3650"
            controls-position="right"
          />
          <span>天</span>
        </div>
        <div class="threshold-control">
          <label>未引用</label>
          <el-input-number
            v-model="thresholds.unusedDays"
            :min="2"
            :max="36500"
            controls-position="right"
          />
          <span>天</span>
        </div>
        <el-button
          v-if="canScan"
          type="primary"
          :icon="FirstAidKit"
          :loading="actionLoading"
          :disabled="isScanning"
          @click="startScan"
        >
          {{ scan ? "重新扫描" : "开始扫描" }}
        </el-button>
        <el-button
          v-if="canCancel && isScanning"
          :icon="Close"
          @click="cancelScan"
          >取消</el-button
        >
      </div>
    </header>

    <section class="scan-band" :class="{ active: isScanning }">
      <div class="progress-block">
        <div class="progress-meta">
          <span>{{ scan ? cleanupStatusLabel(scan.status) : "尚未扫描" }}</span>
          <strong
            >{{ scan?.processedCount ?? 0 }} /
            {{ scan?.totalCount ?? 0 }}</strong
          >
        </div>
        <el-progress
          :percentage="scan ? progress : 0"
          :stroke-width="8"
          :show-text="false"
          :status="
            scan?.status === SpecCleanupScanStatus.Failed
              ? 'exception'
              : undefined
          "
        />
      </div>
      <div class="metrics" aria-label="扫描统计">
        <div class="metric danger">
          <strong>{{ scan?.recommendedCleanupCount ?? 0 }}</strong
          ><span>建议清理</span>
        </div>
        <div class="metric warning">
          <strong>{{ scan?.manualReviewCount ?? 0 }}</strong
          ><span>人工确认</span>
        </div>
        <div class="metric safe">
          <strong>{{ scan?.healthyCount ?? 0 }}</strong
          ><span>正常</span>
        </div>
      </div>
    </section>

    <section
      v-if="lastBatchResult"
      class="batch-result"
      :class="{ warning: lastBatchResult.failedCount > 0 }"
    >
      <el-icon
        ><WarningFilled v-if="lastBatchResult.failedCount" /><CircleCheck
          v-else
      /></el-icon>
      <span
        >成功 {{ lastBatchResult.successCount }} 项，失败
        {{ lastBatchResult.failedCount }} 项</span
      >
      <span v-if="lastBatchResult.failedCount" class="failure-detail">
        {{
          lastBatchResult.items
            .filter(item => !item.success)
            .map(item => item.message)
            .join("；")
        }}
      </span>
    </section>

    <section class="result-section">
      <div class="result-toolbar">
        <el-segmented v-model="activeView" :options="segments" />
        <div
          v-if="
            !['quarantine', 'ignored'].includes(activeView) &&
            scanSelectionCount
          "
          class="bulk-actions"
        >
          <span>已选 {{ scanSelectionCount }} 项</span>
          <el-button v-if="canKeep" :icon="Check" @click="runScanAction('keep')"
            >保留</el-button
          >
          <el-button
            v-if="canIgnore"
            :icon="CircleCheck"
            @click="runScanAction('ignore')"
            >忽略后续扫描</el-button
          >
          <el-button
            v-if="canQuarantine"
            type="warning"
            :icon="Delete"
            @click="runScanAction('quarantine')"
            >移入隔离区</el-button
          >
        </div>
        <div
          v-else-if="activeView === 'quarantine' && quarantineSelectionCount"
          class="bulk-actions"
        >
          <span>已选 {{ quarantineSelectionCount }} 项</span>
          <el-button v-if="canRestore" :icon="Refresh" @click="restoreSelected"
            >恢复</el-button
          >
          <el-button
            v-if="canPermanentDelete"
            type="danger"
            :icon="Delete"
            :disabled="!selectedCanPermanentlyDelete"
            @click="permanentlyDeleteSelected"
            >永久删除</el-button
          >
        </div>
        <div
          v-else-if="activeView === 'ignored' && ignoredSelectionCount"
          class="bulk-actions"
        >
          <span>已选 {{ ignoredSelectionCount }} 项</span>
          <el-button
            v-if="canUnignore"
            :icon="Refresh"
            @click="unignoreSelected"
          >
            重新纳入扫描
          </el-button>
        </div>
      </div>

      <div class="table-shell">
        <el-table
          v-if="!['quarantine', 'ignored'].includes(activeView)"
          ref="scanTable"
          v-loading="loading"
          :data="scanItems"
          row-key="id"
          height="100%"
          @selection-change="selectedScanItems = $event"
        >
          <el-table-column type="selection" width="46" reserve-selection />
          <el-table-column label="判定" width="132">
            <template #default="{ row }">
              <div class="decision-tags">
                <el-tag
                  :type="
                    row.category === SpecCleanupCategory.RecommendedCleanup
                      ? 'danger'
                      : row.category === SpecCleanupCategory.ManualReview
                        ? 'warning'
                        : 'success'
                  "
                  effect="light"
                >
                  {{ cleanupReasonLabel(row.reason) }}
                </el-tag>
                <el-tag
                  v-if="row.reviewStatus === 1"
                  type="info"
                  effect="plain"
                >
                  已保留
                </el-tag>
              </div>
            </template>
          </el-table-column>
          <el-table-column
            prop="project"
            label="项目"
            min-width="130"
            show-overflow-tooltip
          />
          <el-table-column
            prop="specification"
            label="规格内容"
            min-width="190"
            show-overflow-tooltip
          />
          <el-table-column
            prop="acceptance"
            label="验收规范"
            min-width="190"
            show-overflow-tooltip
          >
            <template #default="{ row }">{{ row.acceptance || "-" }}</template>
          </el-table-column>
          <el-table-column
            prop="remark"
            label="备注"
            min-width="160"
            show-overflow-tooltip
          >
            <template #default="{ row }">{{ row.remark || "-" }}</template>
          </el-table-column>
          <el-table-column label="归属" min-width="145" show-overflow-tooltip>
            <template #default="{ row }"
              >{{ row.customerName
              }}<span v-if="row.processName">
                / {{ row.processName }}</span
              ></template
            >
          </el-table-column>
          <el-table-column label="引用" width="112" align="center">
            <template #default="{ row }"
              ><strong>{{ row.currentReferenceCount }}</strong> / 全版本
              {{ row.recordedReferenceCount }}</template
            >
          </el-table-column>
          <el-table-column label="最近引用" width="148">
            <template #default="{ row }">{{
              formatApiUtcDateTime(row.lastReferencedAtUtc) || "-"
            }}</template>
          </el-table-column>
          <el-table-column label="内容时间" width="148">
            <template #default="{ row }">{{
              formatApiUtcDateTime(row.contentActivityAtUtc)
            }}</template>
          </el-table-column>
          <el-table-column label="版本" width="60" align="center"
            ><template #default="{ row }"
              >V{{ row.referenceVersion }}</template
            ></el-table-column
          >
        </el-table>

        <el-table
          v-else-if="activeView === 'ignored'"
          ref="ignoredTable"
          v-loading="loading"
          :data="ignoredItems"
          row-key="id"
          height="100%"
          @selection-change="selectedIgnoredItems = $event"
        >
          <el-table-column type="selection" width="46" reserve-selection />
          <el-table-column label="状态" width="104">
            <template #default><el-tag type="info">已忽略</el-tag></template>
          </el-table-column>
          <el-table-column
            prop="project"
            label="项目"
            min-width="170"
            show-overflow-tooltip
          />
          <el-table-column
            prop="specification"
            label="规格内容"
            min-width="220"
            show-overflow-tooltip
          />
          <el-table-column
            prop="acceptance"
            label="验收规范"
            min-width="200"
            show-overflow-tooltip
          >
            <template #default="{ row }">{{ row.acceptance || "-" }}</template>
          </el-table-column>
          <el-table-column
            prop="remark"
            label="备注"
            min-width="180"
            show-overflow-tooltip
          >
            <template #default="{ row }">{{ row.remark || "-" }}</template>
          </el-table-column>
          <el-table-column label="归属" min-width="160">
            <template #default="{ row }"
              >{{ row.customerName
              }}<span v-if="row.processName">
                / {{ row.processName }}</span
              ></template
            >
          </el-table-column>
          <el-table-column
            prop="ignoreReason"
            label="忽略原因"
            min-width="170"
            show-overflow-tooltip
          />
          <el-table-column label="忽略时间" width="166">
            <template #default="{ row }">{{
              formatApiUtcDateTime(row.ignoredAtUtc) || "-"
            }}</template>
          </el-table-column>
          <el-table-column label="版本" width="72" align="center">
            <template #default="{ row }">V{{ row.referenceVersion }}</template>
          </el-table-column>
        </el-table>

        <el-table
          v-else
          ref="quarantineTable"
          v-loading="loading"
          :data="quarantinedItems"
          row-key="id"
          height="100%"
          @selection-change="selectedQuarantinedItems = $event"
        >
          <el-table-column type="selection" width="46" reserve-selection />
          <el-table-column label="状态" width="110">
            <template #default="{ row }"
              ><el-tag
                :type="
                  isExpired(row.quarantineExpiresAtUtc) ? 'danger' : 'warning'
                "
                >{{
                  isExpired(row.quarantineExpiresAtUtc)
                    ? "可永久删除"
                    : "隔离中"
                }}</el-tag
              ></template
            >
          </el-table-column>
          <el-table-column
            prop="project"
            label="项目"
            min-width="160"
            show-overflow-tooltip
          />
          <el-table-column
            prop="specification"
            label="规格内容"
            min-width="220"
            show-overflow-tooltip
          />
          <el-table-column
            prop="acceptance"
            label="验收规范"
            min-width="200"
            show-overflow-tooltip
          >
            <template #default="{ row }">{{ row.acceptance || "-" }}</template>
          </el-table-column>
          <el-table-column
            prop="remark"
            label="备注"
            min-width="180"
            show-overflow-tooltip
          >
            <template #default="{ row }">{{ row.remark || "-" }}</template>
          </el-table-column>
          <el-table-column label="归属" min-width="150"
            ><template #default="{ row }"
              >{{ row.customerName
              }}<span v-if="row.processName">
                / {{ row.processName }}</span
              ></template
            ></el-table-column
          >
          <el-table-column
            prop="quarantineReason"
            label="隔离原因"
            min-width="150"
            show-overflow-tooltip
          />
          <el-table-column label="隔离时间" width="166"
            ><template #default="{ row }">{{
              formatApiUtcDateTime(row.quarantinedAtUtc)
            }}</template></el-table-column
          >
          <el-table-column label="可删除时间" width="166"
            ><template #default="{ row }">{{
              formatApiUtcDateTime(row.quarantineExpiresAtUtc)
            }}</template></el-table-column
          >
          <el-table-column label="版本" width="72" align="center"
            ><template #default="{ row }"
              >V{{ row.referenceVersion }}</template
            ></el-table-column
          >
        </el-table>
      </div>

      <footer class="pagination-bar">
        <el-pagination
          background
          layout="total, sizes, prev, pager, next"
          :total="total"
          :current-page="page"
          :page-size="pageSize"
          :page-sizes="[20, 50, 100]"
          @current-change="handlePageChange"
          @size-change="handlePageSizeChange"
        />
      </footer>
    </section>
  </div>
</template>

<style scoped>
.cleanup-page {
  display: flex;
  flex-direction: column;
  gap: 12px;
  height: calc(100vh - 104px);
  min-height: 560px;
  padding: 16px;
  overflow: hidden;
  color: var(--app-text-primary);
  background: var(--app-bg-page);
}

.cleanup-page.embedded {
  height: calc(94vh - 64px);
  min-height: 520px;
  padding: 0;
  background: var(--app-bg-card);
}

.cleanup-header,
.result-toolbar,
.scan-controls,
.threshold-control,
.title-group,
.bulk-actions,
.progress-meta,
.batch-result {
  display: flex;
  align-items: center;
}

.cleanup-header {
  gap: 16px;
  justify-content: space-between;
  min-height: 42px;
}

.title-group {
  gap: 10px;
  min-width: 180px;
}

.title-mark {
  display: grid;
  place-items: center;
  width: 34px;
  height: 34px;
  font-size: 19px;
  color: var(--app-primary);
  background: var(--app-info-bg);
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius-sm);
}

h1 {
  margin: 0;
  font-size: 18px;
  line-height: 22px;
  letter-spacing: 0;
}

.title-group span {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.scan-controls {
  flex-wrap: wrap;
  gap: 8px;
  justify-content: flex-end;
}

.threshold-control {
  gap: 8px;
}

.scan-controls label {
  font-size: 13px;
  color: var(--app-text-secondary);
}

.scan-controls :deep(.el-input-number) {
  width: 112px;
}

.scan-band {
  display: grid;
  grid-template-columns: minmax(260px, 1fr) auto;
  gap: 24px;
  align-items: center;
  padding: 12px 16px;
  background: var(--app-bg-card);
  border: 1px solid var(--app-border);
  border-left: 3px solid var(--app-border);
  border-radius: var(--app-radius-sm);
}

.scan-band.active {
  border-left-color: var(--app-primary);
}

.progress-block {
  min-width: 0;
}

.progress-meta {
  justify-content: space-between;
  margin-bottom: 7px;
  font-size: 13px;
}

.progress-meta strong {
  font-variant-numeric: tabular-nums;
}

.metrics {
  display: grid;
  grid-template-columns: repeat(3, minmax(112px, auto));
}

.metric {
  display: grid;
  grid-template-columns: auto auto;
  gap: 6px;
  align-items: baseline;
  padding: 0 14px;
  border-left: 1px solid var(--app-border);
}

.metric strong {
  font-size: 20px;
  font-variant-numeric: tabular-nums;
  line-height: 24px;
}

.metric span {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.metric.danger strong {
  color: var(--el-color-danger);
}

.metric.warning strong {
  color: var(--el-color-warning);
}

.metric.safe strong {
  color: var(--el-color-success);
}

.batch-result {
  gap: 8px;
  min-height: 34px;
  padding: 6px 12px;
  font-size: 13px;
  color: var(--el-color-success);
  background: var(--el-color-success-light-9);
  border: 1px solid var(--el-color-success-light-7);
  border-radius: var(--app-radius-sm);
}

.batch-result.warning {
  color: var(--el-color-warning-dark-2);
  background: var(--el-color-warning-light-9);
  border-color: var(--el-color-warning-light-7);
}

.failure-detail {
  overflow: hidden;
  text-overflow: ellipsis;
  color: var(--app-text-secondary);
  white-space: nowrap;
}

.result-section {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
  background: var(--app-bg-card);
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius-sm);
}

.result-toolbar {
  gap: 12px;
  justify-content: space-between;
  min-height: 50px;
  padding: 8px 12px;
  border-bottom: 1px solid var(--app-border);
}

.bulk-actions {
  gap: 8px;
  justify-content: flex-end;
}

.bulk-actions > span {
  font-size: 13px;
  color: var(--app-text-secondary);
}

.decision-tags {
  display: flex;
  gap: 4px;
  align-items: center;
  white-space: nowrap;
}

.table-shell {
  flex: 1;
  min-height: 0;
}

.pagination-bar {
  display: flex;
  justify-content: flex-end;
  min-height: 48px;
  padding: 8px 12px;
  border-top: 1px solid var(--app-border);
}

@media (width <= 900px) {
  .cleanup-page {
    height: calc(100vh - 88px);
    min-height: 520px;
    padding: 10px;
  }

  .cleanup-header {
    flex-direction: column;
    align-items: flex-start;
  }

  .scan-controls {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    justify-content: stretch;
    width: 100%;
  }

  .threshold-control {
    display: grid;
    grid-template-columns: auto minmax(72px, 1fr) auto;
    gap: 6px;
  }

  .scan-controls :deep(.el-input-number) {
    width: 100%;
  }

  .scan-controls > .el-button {
    grid-column: 1 / -1;
    margin-left: 0;
  }

  .scan-band {
    grid-template-columns: 1fr;
    gap: 12px;
  }

  .metrics {
    grid-template-columns: repeat(3, minmax(86px, 1fr));
  }

  .metric {
    grid-template-columns: 1fr;
    padding: 0 8px;
    text-align: center;
  }

  .metric span {
    white-space: nowrap;
  }

  .result-toolbar {
    flex-direction: column;
    align-items: flex-start;
  }

  .result-toolbar :deep(.el-segmented) {
    width: 100%;
  }

  .result-toolbar :deep(.el-segmented__group) {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    width: 100%;
  }

  .result-toolbar :deep(.el-segmented__item-selected) {
    display: none !important;
  }

  .result-toolbar :deep(.el-segmented__item.is-selected) {
    color: var(--el-color-white);
    background: var(--el-segmented-item-selected-bg-color);
  }

  .result-toolbar :deep(.el-segmented__item-label) {
    overflow: hidden;
    text-overflow: ellipsis;
    font-size: 12px;
    white-space: nowrap;
  }

  .bulk-actions {
    flex-wrap: wrap;
    justify-content: flex-start;
  }

  .pagination-bar {
    justify-content: flex-start;
    overflow-x: auto;
  }
}
</style>
