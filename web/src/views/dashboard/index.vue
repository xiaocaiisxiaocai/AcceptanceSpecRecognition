<script setup lang="ts">
import { onMounted, ref } from "vue";
import { ElMessage } from "element-plus";
import { getCustomerList } from "@/api/customer";
import { getProcessList } from "@/api/process";
import { getSpecList } from "@/api/spec";
import { getFileList } from "@/api/document";
import { getHistoryList, OperationType, type OperationHistory } from "@/api/history";

defineOptions({
  name: "Dashboard"
});

const loading = ref(false);

const customerTotal = ref(0);
const processTotal = ref(0);
const specTotal = ref(0);
const fileTotal = ref(0);

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
    const [c, p, s, f, h] = await Promise.all([
      getCustomerList({ page: 1, pageSize: 1 }),
      getProcessList({ page: 1, pageSize: 1 }),
      getSpecList({ page: 1, pageSize: 1 }),
      getFileList({ page: 1, pageSize: 1 }),
      getHistoryList({ page: 1, pageSize: 10 })
    ]);

    if (c.code === 0) customerTotal.value = c.data.total;
    if (p.code === 0) processTotal.value = p.data.total;
    if (s.code === 0) specTotal.value = s.data.total;
    if (f.code === 0) fileTotal.value = f.data.total;
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
  <div class="main">
    <el-row :gutter="16">
      <el-col :xs="24" :sm="12" :md="6">
        <el-card v-loading="loading">
          <div class="stat">
            <div class="stat-title">客户</div>
            <div class="stat-value">{{ customerTotal }}</div>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="12" :md="6">
        <el-card v-loading="loading">
          <div class="stat">
            <div class="stat-title">制程</div>
            <div class="stat-value">{{ processTotal }}</div>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="12" :md="6">
        <el-card v-loading="loading">
          <div class="stat">
            <div class="stat-title">验收规格</div>
            <div class="stat-value">{{ specTotal }}</div>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="12" :md="6">
        <el-card v-loading="loading">
          <div class="stat">
            <div class="stat-title">已上传文件</div>
            <div class="stat-value">{{ fileTotal }}</div>
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
        <el-table-column prop="targetFile" label="目标文件" min-width="220" />
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
.main {
  padding: 20px;
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
}
</style>

