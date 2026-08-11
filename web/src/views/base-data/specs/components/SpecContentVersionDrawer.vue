<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  getSpecContentVersion,
  getSpecContentVersions,
  restoreSpecContentVersion,
  type AcceptanceSpec,
  type SpecContentVersionDetail,
  type SpecContentVersionHistory
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
const versionDetails = ref<SpecContentVersionDetail[]>([]);
const loading = ref(false);
const restoringVersion = ref<number | null>(null);
const errorMessage = ref("");
let requestId = 0;

const sourceLabels: Record<string, string> = {
  create: "新建",
  "manual-update": "手工编辑",
  "document-import": "文档导入",
  "smart-fill-backfill": "智能填充",
  "remark-replace": "备注替换",
  restore: "版本恢复",
  "migration-baseline": "上线基线"
};
const fieldLabels = {
  project: "项目",
  specification: "规格内容",
  acceptance: "验收规范",
  remark: "备注"
} as const;
const contentFields = [
  "project",
  "specification",
  "acceptance",
  "remark"
] as const;

const sourceLabel = (source: string) => sourceLabels[source] || source;
const fieldLabel = (field: (typeof contentFields)[number]) =>
  fieldLabels[field];
const isFieldChanged = (
  versionIndex: number,
  field: (typeof contentFields)[number]
) => {
  if (versionIndex === 0) return false;
  const current = versionDetails.value[versionIndex]?.[field] ?? "";
  const previous = versionDetails.value[versionIndex - 1]?.[field] ?? "";
  return current !== previous;
};

const loadHistory = async () => {
  if (!visible.value || !props.spec) return;
  const activeRequestId = ++requestId;
  loading.value = true;
  errorMessage.value = "";
  versionDetails.value = [];

  try {
    const historyResponse = await getSpecContentVersions(props.spec.id, {
      page: 1,
      pageSize: 20,
      sort: "oldest"
    });
    if (activeRequestId !== requestId) return;
    if (historyResponse.code !== 0) throw new Error(historyResponse.message);

    history.value = historyResponse.data;
    const versionNumbers: number[] = [];
    for (
      let version = historyResponse.data.earliestAvailableVersion;
      version <= historyResponse.data.currentVersion;
      version += 1
    ) {
      versionNumbers.push(version);
    }

    const detailResponses = await Promise.all(
      versionNumbers.map(version =>
        getSpecContentVersion(props.spec!.id, version)
      )
    );
    if (activeRequestId !== requestId) return;
    const failedResponse = detailResponses.find(
      response => response.code !== 0
    );
    if (failedResponse) throw new Error(failedResponse.message);

    versionDetails.value = detailResponses
      .map(response => response.data)
      .sort((left, right) => left.version - right.version);
  } catch (error) {
    if (activeRequestId !== requestId) return;
    history.value = null;
    versionDetails.value = [];
    errorMessage.value = getRequestErrorMessage(error, "加载内容版本失败");
  } finally {
    if (activeRequestId === requestId) loading.value = false;
  }
};

const restoreVersion = async (sourceVersion: number) => {
  if (
    !props.spec ||
    !history.value ||
    sourceVersion === history.value.currentVersion
  )
    return;

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
    restoringVersion.value = sourceVersion;
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
    await loadHistory();
  } catch (error) {
    if (isMessageBoxCancel(error)) return;
    const message = getRequestErrorMessage(error, "恢复版本失败");
    ElMessage.error(message);
    if (message.includes("已被更新")) await loadHistory();
  } finally {
    restoringVersion.value = null;
  }
};

watch(
  () => [visible.value, props.spec?.id] as const,
  ([isVisible]) => {
    requestId += 1;
    history.value = null;
    versionDetails.value = [];
    errorMessage.value = "";
    if (isVisible) loadHistory();
  }
);
</script>

<template>
  <el-drawer
    v-model="visible"
    :title="`版本记录 · ${spec?.project || ''}`"
    size="min(1120px, 100vw)"
    destroy-on-close
  >
    <div v-loading="loading" class="version-workbench">
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

      <div v-if="versionDetails.length" class="matrix-scroll">
        <table class="version-matrix">
          <thead>
            <tr>
              <th class="field-heading" scope="col">内容</th>
              <th
                v-for="version in versionDetails"
                :key="`heading-${version.version}`"
                class="version-heading"
                scope="col"
              >
                <div class="version-title">
                  <strong>V{{ version.version }}</strong>
                  <el-tag
                    v-if="version.version === history?.currentVersion"
                    size="small"
                    type="success"
                    effect="plain"
                  >
                    当前
                  </el-tag>
                </div>
                <div class="version-meta">
                  <span>{{ sourceLabel(version.changeSource) }}</span>
                  <span>{{ version.changedByNameSnapshot || "系统" }}</span>
                  <span>{{ formatApiUtcDateTime(version.changedAtUtc) }}</span>
                  <span v-if="version.restoredFromVersion">
                    由 V{{ version.restoredFromVersion }} 恢复
                  </span>
                  <span v-if="version.changeReason" class="change-reason">
                    {{ version.changeReason }}
                  </span>
                </div>
                <el-button
                  v-if="
                    canRestore && version.version !== history?.currentVersion
                  "
                  class="restore-button"
                  type="warning"
                  link
                  :loading="restoringVersion === version.version"
                  @click="restoreVersion(version.version)"
                >
                  恢复此版本
                </el-button>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="field in contentFields" :key="field">
              <th class="field-heading" scope="row">{{ fieldLabel(field) }}</th>
              <td
                v-for="(version, versionIndex) in versionDetails"
                :key="`${version.version}-${field}`"
                :class="{
                  'is-changed': isFieldChanged(versionIndex, field)
                }"
              >
                <span
                  v-if="isFieldChanged(versionIndex, field)"
                  class="change-mark"
                >
                  已变更
                </span>
                <p>{{ version[field] || "-" }}</p>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <el-empty
        v-else-if="!loading && !errorMessage"
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

.history-gap {
  margin-bottom: 12px;
}

.matrix-scroll {
  width: 100%;
  overflow: auto;
  border: 1px solid var(--el-border-color-light);
}

.version-matrix {
  width: max-content;
  min-width: 100%;
  table-layout: fixed;
  border-spacing: 0;
  border-collapse: separate;
}

.version-matrix th,
.version-matrix td {
  min-width: 260px;
  padding: 14px 16px;
  vertical-align: top;
  text-align: left;
  border-right: 1px solid var(--el-border-color-lighter);
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.version-matrix tr:last-child th,
.version-matrix tr:last-child td {
  border-bottom: 0;
}

.version-matrix th:last-child,
.version-matrix td:last-child {
  border-right: 0;
}

.version-matrix .field-heading {
  position: sticky;
  left: 0;
  z-index: 2;
  width: 108px;
  min-width: 108px;
  font-size: 13px;
  background: var(--el-fill-color-lighter);
}

.version-matrix thead .field-heading {
  z-index: 3;
}

.version-heading {
  height: 116px;
  background: var(--el-bg-color);
}

.version-title {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-bottom: 8px;
}

.version-title strong {
  font-size: 16px;
}

.version-meta {
  display: grid;
  gap: 3px;
  font-size: 12px;
  font-weight: 400;
  color: var(--el-text-color-secondary);
}

.change-reason {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.restore-button {
  padding: 0;
  margin-top: 8px;
}

.version-matrix td {
  position: relative;
  background: var(--el-bg-color);
}

.version-matrix td.is-changed {
  background: var(--el-color-warning-light-9);
  box-shadow: inset 3px 0 0 var(--el-color-warning);
}

.change-mark {
  display: inline-block;
  margin-bottom: 6px;
  font-size: 12px;
  color: var(--el-color-warning-dark-2);
}

.version-matrix p {
  margin: 0;
  line-height: 1.65;
  overflow-wrap: anywhere;
  white-space: pre-wrap;
}

@media (width <= 720px) {
  .version-matrix th,
  .version-matrix td {
    min-width: 220px;
    padding: 12px;
  }

  .version-matrix .field-heading {
    width: 88px;
    min-width: 88px;
  }
}
</style>
