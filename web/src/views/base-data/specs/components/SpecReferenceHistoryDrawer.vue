<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ElMessage } from "element-plus";
import {
  getSpecReferenceHistory,
  type AcceptanceSpec,
  type SpecReferenceHistoryResponse,
  type SpecReferenceHistorySort
} from "@/api/spec";
import { formatApiUtcDateTime } from "@/utils/date-time";
import { getRequestErrorMessage } from "@/utils/error-message";

const props = defineProps<{
  modelValue: boolean;
  spec: AcceptanceSpec | null;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: boolean];
}>();

const visible = computed({
  get: () => props.modelValue,
  set: value => emit("update:modelValue", value)
});
const loading = ref(false);
const history = ref<SpecReferenceHistoryResponse | null>(null);
const page = ref(1);
const pageSize = ref(20);
const includePreviousVersions = ref(false);
const sort = ref<SpecReferenceHistorySort>("oldest");
let requestId = 0;

const loadHistory = async () => {
  if (!visible.value || !props.spec) return;

  const currentRequestId = ++requestId;
  loading.value = true;
  try {
    const response = await getSpecReferenceHistory(props.spec.id, {
      page: page.value,
      pageSize: pageSize.value,
      includePreviousVersions: includePreviousVersions.value,
      sort: sort.value
    });
    if (currentRequestId !== requestId) return;
    if (response.code !== 0) {
      ElMessage.error(response.message);
      return;
    }
    history.value = response.data;
  } catch (error) {
    if (currentRequestId !== requestId) return;
    ElMessage.error(getRequestErrorMessage(error, "加载引用时间失败"));
  } finally {
    if (currentRequestId === requestId) {
      loading.value = false;
    }
  }
};

watch(
  () => [visible.value, props.spec?.id] as const,
  ([isVisible]) => {
    if (!isVisible) return;
    page.value = 1;
    includePreviousVersions.value = false;
    sort.value = "oldest";
    history.value = null;
    loadHistory();
  }
);

watch([includePreviousVersions, sort], () => {
  if (!visible.value) return;
  page.value = 1;
  loadHistory();
});

const handlePageChange = (value: number) => {
  page.value = value;
  loadHistory();
};
</script>

<template>
  <el-drawer
    v-model="visible"
    :title="`引用时间 · ${spec?.project || ''}`"
    size="min(720px, 100vw)"
    destroy-on-close
  >
    <div v-loading="loading" class="reference-history">
      <el-descriptions v-if="history" :column="3" border>
        <el-descriptions-item label="当前引用次数">
          {{ history.currentReferenceCount }}
        </el-descriptions-item>
        <el-descriptions-item
          :label="
            history.includePreviousVersions
              ? '全部版本已记录'
              : '当前版本已记录'
          "
        >
          {{ history.recordedReferenceCount }}
        </el-descriptions-item>
        <el-descriptions-item
          :label="
            history.includePreviousVersions
              ? '全部版本不可追溯'
              : '当前版本不可追溯'
          "
        >
          {{ history.untrackedReferenceCount }}
        </el-descriptions-item>
      </el-descriptions>

      <el-alert
        v-if="history?.untrackedReferenceCount"
        class="history-alert"
        type="warning"
        :closable="false"
        :title="`所选版本中有 ${history.untrackedReferenceCount} 次引用发生在记录功能上线前，具体时间不可追溯。`"
      />

      <div class="history-toolbar">
        <el-radio-group v-model="includePreviousVersions" size="small">
          <el-radio-button :value="false">当前版本</el-radio-button>
          <el-radio-button :value="true">全部版本</el-radio-button>
        </el-radio-group>
        <el-radio-group v-model="sort" size="small">
          <el-radio-button value="oldest">最早在前</el-radio-button>
          <el-radio-button value="newest">最新在前</el-radio-button>
        </el-radio-group>
      </div>

      <el-table :data="history?.items || []" stripe>
        <el-table-column label="引用序号" width="110" align="center">
          <template #default="{ row }">
            <span v-if="row.referenceOrdinal"
              >第 {{ row.referenceOrdinal }} 次</span
            >
            <span v-else class="text-gray-400">按版本查看</span>
          </template>
        </el-table-column>
        <el-table-column label="内容版本" width="120" align="center">
          <template #default="{ row }">
            <el-tag
              :type="row.isCurrentVersion ? 'primary' : 'info'"
              effect="plain"
            >
              版本 {{ row.referenceVersion }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="引用时间" min-width="220">
          <template #default="{ row }">
            {{ formatApiUtcDateTime(row.referencedAtUtc) }}
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暂无可追溯的引用时间" :image-size="72" />
        </template>
      </el-table>

      <el-pagination
        v-if="history && history.total > history.pageSize"
        class="history-pagination"
        :current-page="page"
        :page-size="pageSize"
        :total="history.total"
        layout="total, prev, pager, next"
        @current-change="handlePageChange"
      />
    </div>
  </el-drawer>
</template>

<style scoped>
.reference-history {
  min-height: 240px;
}

.history-alert {
  margin-top: 16px;
}

.history-toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  justify-content: space-between;
  margin: 16px 0;
}

.history-pagination {
  justify-content: flex-end;
  margin-top: 16px;
}
</style>
