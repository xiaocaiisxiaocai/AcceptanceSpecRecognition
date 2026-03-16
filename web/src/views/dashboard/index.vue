<script setup lang="ts">
import { onMounted, ref } from "vue";
import { ElMessage } from "element-plus";
import { getCustomerList } from "@/api/customer";
import { getProcessList } from "@/api/process";
import { getSpecList } from "@/api/spec";
import { getHistoryList, OperationType, type OperationHistory } from "@/api/history";

defineOptions({
  name: "Dashboard"
});

const loading = ref(false);

const customerTotal = ref(0);
const processTotal = ref(0);
const specTotal = ref(0);

const recentHistory = ref<OperationHistory[]>([]);

const typeLabel = (t: OperationType) => {
  if (t === OperationType.Import) return "导入";
  if (t === OperationType.Fill) return "填充";
  if (t === OperationType.Delete) return "删除";
  return String(t);
};

const load = async () => {
  loading.value = true;
  try {
    const [c, p, s, h] = await Promise.all([
      getCustomerList({ page: 1, pageSize: 1 }),
      getProcessList({ page: 1, pageSize: 1 }),
      getSpecList({ page: 1, pageSize: 1 }),
      getHistoryList({ page: 1, pageSize: 10 })
    ]);

    if (c.code === 0) customerTotal.value = c.data.total;
    if (p.code === 0) processTotal.value = p.data.total;
    if (s.code === 0) specTotal.value = s.data.total;
    if (h.code === 0) recentHistory.value = h.data.items;
  } catch {
    ElMessage.error("加载仪表盘数据失败");
  } finally {
    loading.value = false;
  }
};

onMounted(load);
</script>

<template>
  <div class="page dashboard">
    <div class="page-header">
      <div>
        <div class="page-title">系统概览</div>
        <div class="page-subtitle">关键指标与最近操作概览</div>
      </div>
    </div>
    <el-row :gutter="16">
      <el-col :xs="24" :sm="12" :md="8">
        <el-card v-loading="loading" class="stat-card">
          <div class="stat">
            <div class="stat-title">客户</div>
            <div class="stat-value">{{ customerTotal }}</div>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="12" :md="8">
        <el-card v-loading="loading" class="stat-card">
          <div class="stat">
            <div class="stat-title">制程</div>
            <div class="stat-value">{{ processTotal }}</div>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="12" :md="8">
        <el-card v-loading="loading" class="stat-card">
          <div class="stat">
            <div class="stat-title">验收规格</div>
            <div class="stat-value">{{ specTotal }}</div>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <el-card class="mt-4" v-loading="loading">
      <template #header>
        <div class="flex justify-between items-center">
          <span>最近操作（Top 10）</span>
          <el-button size="small" @click="load">刷新</el-button>
        </div>
      </template>

      <el-table :data="recentHistory" stripe>
        <el-table-column prop="id" label="ID" width="80" />
        <el-table-column prop="operationType" label="类型" width="90">
          <template #default="{ row }">{{ typeLabel(row.operationType) }}</template>
        </el-table-column>
        <el-table-column prop="details" label="详情" min-width="320" />
        <el-table-column prop="createdAt" label="时间" width="180">
          <template #default="{ row }">
            {{ new Date(row.createdAt).toLocaleString() }}
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<style scoped>
.page {
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.stat-card {
  border: 1px solid var(--el-card-border-color);
  background: linear-gradient(180deg, #ffffff 0%, #fbf9ff 100%);
}
.stat {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.stat-title {
  color: #6b7280;
  font-size: 14px;
}
.stat-value {
  font-size: 28px;
  font-weight: 600;
  color: var(--color-text);
}
</style>
