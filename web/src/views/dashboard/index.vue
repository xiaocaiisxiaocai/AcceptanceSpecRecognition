<script setup lang="ts">
import { onMounted, ref } from "vue";
import { ElMessage } from "element-plus";
import { getCustomerList } from "@/api/customer";
import { getProcessList } from "@/api/process";
import { getSpecList } from "@/api/spec";

defineOptions({
  name: "Dashboard"
});

const loading = ref(false);

const customerTotal = ref(0);
const processTotal = ref(0);
const specTotal = ref(0);

const load = async () => {
  loading.value = true;
  try {
    const [c, p, s] = await Promise.all([
      getCustomerList({ page: 1, pageSize: 1 }),
      getProcessList({ page: 1, pageSize: 1 }),
      getSpecList({ page: 1, pageSize: 1 })
    ]);

    if (c.code === 0) customerTotal.value = c.data.total;
    if (p.code === 0) processTotal.value = p.data.total;
    if (s.code === 0) specTotal.value = s.data.total;
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
        <div class="page-subtitle">关键指标概览</div>
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
