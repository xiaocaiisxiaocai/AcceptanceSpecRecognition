<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import { DocumentCopy, RefreshLeft } from "@element-plus/icons-vue";
import {
  getSpecContentVersion,
  getSpecContentVersionDiff,
  getSpecContentVersions,
  restoreSpecContentVersion,
  type AcceptanceSpec,
  type SpecContentVersionDetail,
  type SpecContentVersionDiff,
  type SpecContentVersionHistory,
  type SpecContentVersionItem
} from "@/api/spec";
import { hasPerms } from "@/utils/auth";
import { formatApiUtcDateTime } from "@/utils/date-time";
import { getRequestErrorMessage } from "@/utils/error-message";
import { isMessageBoxCancel } from "@/utils/message-box";

const props = defineProps<{
  modelValue: boolean;
  spec: AcceptanceSpec | null;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: boolean];
  restored: [spec: AcceptanceSpec];
}>();

const visible = computed({
  get: () => props.modelValue,
  set: value => emit("update:modelValue", value)
});
const canRestore = computed(() => hasPerms("btn:spec:restore-version"));
const history = ref<SpecContentVersionHistory | null>(null);
const selectedVersion = ref<number | null>(null);
const detail = ref<SpecContentVersionDetail | null>(null);
const diff = ref<SpecContentVersionDiff | null>(null);
const mode = ref<"snapshot" | "diff">("snapshot");
const fromVersion = ref<number | null>(null);
const toVersion = ref<number | null>(null);
const loadingHistory = ref(false);
const loadingContent = ref(false);
const restoring = ref(false);
const errorMessage = ref("");
const historyPage = ref(1);
const historyPageSize = 20;
let historyRequestId = 0;
let contentRequestId = 0;

const selectedItem = computed(() =>
  history.value?.items.find(item => item.version === selectedVersion.value)
);

const sourceLabels: Record<string, string> = {
  create: "新建",
  "manual-update": "手工编辑",
  "document-import": "文档导入",
  "smart-fill-backfill": "智能填充",
  "remark-replace": "备注替换",
  restore: "版本恢复",
  "migration-baseline": "上线基线"
};
const fieldLabels: Record<string, string> = {
  project: "项目",
  specification: "规格内容",
  acceptance: "验收标准",
  remark: "备注"
};
const contentFields = [
  "project",
  "specification",
  "acceptance",
  "remark"
] as const;
const sourceLabel = (source: string) => sourceLabels[source] || source;
const fieldLabel = (field: string) => fieldLabels[field] || field;

const loadDetail = async (version: number) => {
  if (!props.spec) return;
  const requestId = ++contentRequestId;
  loadingContent.value = true;
  errorMessage.value = "";
  try {
    const response = await getSpecContentVersion(props.spec.id, version);
    if (requestId !== contentRequestId) return;
    if (response.code !== 0) throw new Error(response.message);
    detail.value = response.data;
  } catch (error) {
    if (requestId !== contentRequestId) return;
    detail.value = null;
    errorMessage.value = getRequestErrorMessage(error, "加载版本正文失败");
  } finally {
    if (requestId === contentRequestId) loadingContent.value = false;
  }
};

const loadHistory = async (preferredVersion?: number, page = 1) => {
  if (!visible.value || !props.spec) return;
  const requestId = ++historyRequestId;
  loadingHistory.value = true;
  errorMessage.value = "";
  try {
    const response = await getSpecContentVersions(props.spec.id, {
      page,
      pageSize: historyPageSize,
      sort: "newest"
    });
    if (requestId !== historyRequestId) return;
    if (response.code !== 0) throw new Error(response.message);
    history.value = response.data;
    historyPage.value = response.data.page;
    const versions = response.data.items.map(item => item.version);
    const nextVersion =
      preferredVersion && versions.includes(preferredVersion)
        ? preferredVersion
        : response.data.currentVersion;
    selectedVersion.value = versions.includes(nextVersion)
      ? nextVersion
      : (versions[0] ?? null);
    fromVersion.value =
      response.data.total > 1 ? response.data.earliestAvailableVersion : null;
    toVersion.value =
      response.data.total > 1 ? response.data.currentVersion : null;
    if (selectedVersion.value != null) await loadDetail(selectedVersion.value);
  } catch (error) {
    if (requestId !== historyRequestId) return;
    history.value = null;
    detail.value = null;
    errorMessage.value = getRequestErrorMessage(error, "加载内容版本失败");
  } finally {
    if (requestId === historyRequestId) loadingHistory.value = false;
  }
};

const changeHistoryPage = (page: number) => {
  loadHistory(undefined, page);
};

const selectVersion = (item: SpecContentVersionItem) => {
  mode.value = "snapshot";
  diff.value = null;
  selectedVersion.value = item.version;
  loadDetail(item.version);
};

const compareVersions = async () => {
  if (!props.spec || fromVersion.value == null || toVersion.value == null)
    return;
  if (fromVersion.value === toVersion.value) {
    ElMessage.warning("请选择两个不同版本");
    return;
  }
  const requestId = ++contentRequestId;
  loadingContent.value = true;
  errorMessage.value = "";
  try {
    const response = await getSpecContentVersionDiff(
      props.spec.id,
      fromVersion.value,
      toVersion.value
    );
    if (requestId !== contentRequestId) return;
    if (response.code !== 0) throw new Error(response.message);
    diff.value = response.data;
  } catch (error) {
    if (requestId !== contentRequestId) return;
    diff.value = null;
    errorMessage.value = getRequestErrorMessage(error, "比较版本失败");
  } finally {
    if (requestId === contentRequestId) loadingContent.value = false;
  }
};

const restoreVersion = async () => {
  if (
    !props.spec ||
    !history.value ||
    selectedVersion.value == null ||
    selectedVersion.value === history.value.currentVersion
  )
    return;

  const sourceVersion = selectedVersion.value;
  const nextVersion = history.value.currentVersion + 1;
  try {
    const result = await ElMessageBox.prompt(
      `将 V${sourceVersion} 的正文恢复为新的 V${nextVersion}，现有历史不会被覆盖。`,
      "恢复内容版本",
      {
        confirmButtonText: `创建 V${nextVersion}`,
        cancelButtonText: "取消",
        inputPlaceholder: "恢复原因（可选）",
        inputValidator: value =>
          value.length <= 500 || "恢复原因不能超过 500 个字符"
      }
    );
    restoring.value = true;
    const response = await restoreSpecContentVersion(
      props.spec.id,
      sourceVersion,
      {
        expectedCurrentVersion: history.value.currentVersion,
        reason: result.value.trim() || undefined
      }
    );
    if (response.code !== 0) throw new Error(response.message);
    ElMessage.success(`已创建 V${response.data.referenceVersion}`);
    emit("restored", response.data);
    await loadHistory(response.data.referenceVersion);
  } catch (error) {
    if (isMessageBoxCancel(error)) return;
    const message = getRequestErrorMessage(error, "恢复版本失败");
    ElMessage.error(message);
    if (message.includes("已被更新")) await loadHistory();
  } finally {
    restoring.value = false;
  }
};

watch(
  () => [visible.value, props.spec?.id] as const,
  ([isVisible]) => {
    if (!isVisible) return;
    history.value = null;
    detail.value = null;
    diff.value = null;
    mode.value = "snapshot";
    loadHistory();
  }
);
</script>

<template>
  <el-drawer
    v-model="visible"
    :title="`内容版本 · ${spec?.project || ''}`"
    size="min(960px, 100vw)"
    destroy-on-close
  >
    <div v-loading="loadingHistory" class="version-workbench">
      <header v-if="history" class="version-summary">
        <div>
          <span class="summary-label">当前版本</span>
          <strong>V{{ history.currentVersion }}</strong>
        </div>
        <div>
          <span class="summary-label">最早可用</span>
          <strong>V{{ history.earliestAvailableVersion }}</strong>
        </div>
        <div>
          <span class="summary-label">已保存版本</span>
          <strong>{{ history.total }}</strong>
        </div>
      </header>

      <el-alert
        v-if="history?.hasUnavailableEarlierVersions"
        class="history-gap"
        type="warning"
        :closable="false"
        title="版本记录功能上线前的正文不可追溯，系统不会用当前内容伪造旧版本。"
      />

      <el-alert
        v-if="errorMessage"
        class="history-gap"
        type="error"
        :closable="false"
        :title="errorMessage"
      />

      <div v-if="history?.items.length" class="workbench-grid">
        <aside class="version-rail" aria-label="内容版本列表">
          <div class="version-list">
            <button
              v-for="item in history.items"
              :key="item.version"
              type="button"
              class="version-entry"
              :class="{ 'is-active': selectedVersion === item.version }"
              @click="selectVersion(item)"
            >
              <span class="version-marker" aria-hidden="true" />
              <span class="version-entry-main">
                <span class="version-entry-title">
                  <strong>V{{ item.version }}</strong>
                  <el-tag
                    v-if="item.version === history.currentVersion"
                    size="small"
                    type="success"
                    effect="plain"
                  >
                    当前
                  </el-tag>
                </span>
                <span>{{ sourceLabel(item.changeSource) }}</span>
                <span class="version-meta">
                  {{ item.changedByNameSnapshot || "系统" }} ·
                  {{ formatApiUtcDateTime(item.changedAtUtc) }}
                </span>
                <span v-if="item.changeReason" class="version-reason">
                  {{ item.changeReason }}
                </span>
              </span>
            </button>
          </div>
          <el-pagination
            v-if="history.total > historyPageSize"
            class="version-pagination"
            small
            layout="prev, pager, next"
            :current-page="historyPage"
            :page-size="historyPageSize"
            :total="history.total"
            @current-change="changeHistoryPage"
          />
        </aside>

        <main class="version-content">
          <div class="content-toolbar">
            <el-radio-group v-model="mode" size="small">
              <el-radio-button value="snapshot">版本正文</el-radio-button>
              <el-radio-button value="diff" :disabled="history.total < 2">
                版本对比
              </el-radio-button>
            </el-radio-group>
            <el-button
              v-if="
                mode === 'snapshot' &&
                canRestore &&
                selectedVersion !== history.currentVersion
              "
              type="warning"
              :icon="RefreshLeft"
              :loading="restoring"
              @click="restoreVersion"
            >
              恢复为新版本
            </el-button>
          </div>

          <div v-loading="loadingContent" class="content-body">
            <template v-if="mode === 'snapshot' && detail">
              <div
                v-if="selectedItem?.restoredFromVersion"
                class="restore-origin"
              >
                此版本由 V{{ selectedItem.restoredFromVersion }} 恢复生成
              </div>
              <section
                v-for="field in contentFields"
                :key="field"
                class="content-field"
              >
                <h3>{{ fieldLabel(field) }}</h3>
                <p>{{ detail[field] || "-" }}</p>
              </section>
            </template>

            <template v-else-if="mode === 'diff'">
              <div class="diff-controls">
                <el-input-number
                  v-model="fromVersion"
                  aria-label="起始版本"
                  :min="history.earliestAvailableVersion"
                  :max="history.currentVersion"
                  :step="1"
                  step-strictly
                  controls-position="right"
                />
                <span class="diff-arrow">至</span>
                <el-input-number
                  v-model="toVersion"
                  aria-label="目标版本"
                  :min="history.earliestAvailableVersion"
                  :max="history.currentVersion"
                  :step="1"
                  step-strictly
                  controls-position="right"
                />
                <el-button
                  type="primary"
                  :icon="DocumentCopy"
                  @click="compareVersions"
                >
                  比较
                </el-button>
              </div>
              <template v-if="diff">
                <section
                  v-for="field in contentFields"
                  :key="`diff-${field}`"
                  class="diff-field"
                  :class="{ 'is-unchanged': !diff.fields[field].changed }"
                >
                  <h3>
                    {{ fieldLabel(field) }}
                    <el-tag
                      size="small"
                      :type="diff.fields[field].changed ? 'warning' : 'info'"
                      effect="plain"
                    >
                      {{ diff.fields[field].changed ? "已变化" : "未变化" }}
                    </el-tag>
                  </h3>
                  <div class="diff-values">
                    <div>
                      <span>原值</span>
                      <p>{{ diff.fields[field].before || "-" }}</p>
                    </div>
                    <div>
                      <span>新值</span>
                      <p>{{ diff.fields[field].after || "-" }}</p>
                    </div>
                  </div>
                </section>
              </template>
              <el-empty
                v-else
                description="选择两个版本后开始比较"
                :image-size="72"
              />
            </template>
          </div>
        </main>
      </div>

      <el-empty
        v-else-if="!loadingHistory && !errorMessage"
        description="暂无可用内容版本"
        :image-size="84"
      />
    </div>
  </el-drawer>
</template>

<style scoped>
.version-workbench {
  min-height: 360px;
}

.version-summary {
  display: flex;
  gap: 32px;
  align-items: center;
  min-height: 58px;
  padding: 10px 16px;
  color: var(--el-text-color-primary);
  background: var(--el-fill-color-light);
  border-left: 3px solid var(--el-color-primary);
}

.version-summary > div {
  display: grid;
  gap: 2px;
}

.summary-label,
.version-meta,
.diff-values span {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.history-gap {
  margin-top: 12px;
}

.workbench-grid {
  display: grid;
  grid-template-columns: minmax(210px, 250px) minmax(0, 1fr);
  width: 100%;
  min-width: 0;
  min-height: 520px;
  margin-top: 16px;
  border: 1px solid var(--el-border-color-light);
}

.version-rail {
  display: flex;
  flex-direction: column;
  min-width: 0;
  max-height: calc(100vh - 220px);
  background: var(--el-fill-color-lighter);
  border-right: 1px solid var(--el-border-color-light);
}

.version-list {
  flex: 1;
  width: 100%;
  min-height: 0;
  overflow-y: auto;
}

.version-pagination {
  flex: 0 0 auto;
  justify-content: center;
  padding: 10px 4px;
  background: var(--el-bg-color);
  border-top: 1px solid var(--el-border-color-light);
}

.version-entry {
  position: relative;
  display: flex;
  gap: 12px;
  width: 100%;
  min-height: 104px;
  padding: 14px 14px 14px 18px;
  color: inherit;
  text-align: left;
  cursor: pointer;
  background: transparent;
  border: 0;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.version-entry:hover,
.version-entry.is-active {
  background: var(--el-bg-color);
}

.version-entry.is-active {
  box-shadow: inset 3px 0 0 var(--el-color-primary);
}

.version-marker {
  flex: 0 0 8px;
  width: 8px;
  height: 8px;
  margin-top: 6px;
  background: var(--el-color-primary);
  border-radius: 50%;
}

.version-entry-main {
  display: grid;
  gap: 5px;
  min-width: 0;
}

.version-entry-title {
  display: flex;
  gap: 8px;
  align-items: center;
}

.version-reason {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 12px;
  color: var(--el-text-color-regular);
  white-space: nowrap;
}

.version-content {
  min-width: 0;
  background: var(--el-bg-color);
}

.content-toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
  min-height: 58px;
  padding: 10px 16px;
  border-bottom: 1px solid var(--el-border-color-light);
}

.content-body {
  min-height: 430px;
  padding: 18px;
}

.restore-origin {
  padding: 8px 12px;
  margin-bottom: 16px;
  color: var(--el-color-warning-dark-2);
  background: var(--el-color-warning-light-9);
  border-left: 3px solid var(--el-color-warning);
}

.content-field,
.diff-field {
  padding: 0 0 16px;
  margin-bottom: 16px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.content-field h3,
.diff-field h3 {
  display: flex;
  gap: 8px;
  align-items: center;
  margin: 0 0 8px;
  font-size: 14px;
}

.content-field p,
.diff-values p {
  margin: 0;
  line-height: 1.7;
  overflow-wrap: anywhere;
  white-space: pre-wrap;
}

.diff-controls {
  display: grid;
  grid-template-columns: minmax(100px, 1fr) auto minmax(100px, 1fr) auto;
  gap: 10px;
  align-items: center;
  margin-bottom: 20px;
}

.diff-arrow {
  color: var(--el-text-color-secondary);
}

.diff-values {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

.diff-values > div {
  min-width: 0;
  padding: 10px 12px;
  background: var(--el-fill-color-light);
  border-left: 2px solid var(--el-border-color);
}

.diff-field:not(.is-unchanged) .diff-values > div:last-child {
  border-left-color: var(--el-color-warning);
}

.is-unchanged {
  opacity: 0.75;
}

@media (width <= 720px) {
  .version-summary {
    gap: 16px;
    justify-content: space-between;
  }

  .workbench-grid {
    grid-template-columns: 1fr;
  }

  .version-rail {
    max-height: 180px;
    border-right: 0;
    border-bottom: 1px solid var(--el-border-color-light);
  }

  .version-list {
    display: flex;
    overflow-x: auto;
  }

  .version-entry {
    flex: 0 0 210px;
    border-right: 1px solid var(--el-border-color-lighter);
  }

  .diff-controls,
  .diff-values {
    grid-template-columns: 1fr;
  }

  .diff-arrow {
    display: none;
  }
}
</style>
