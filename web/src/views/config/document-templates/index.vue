<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import dayjs from "dayjs";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  deleteDocumentTemplate,
  getDocumentTemplate,
  getDocumentTemplates,
  type DocumentTemplateDetail,
  type DocumentTemplateListItem
} from "@/api/document-templates";
import { getCustomerList, type Customer } from "@/api/customer";
import { hasPerms } from "@/utils/auth";
import {
  getRequestErrorMessage,
  isGloballyHandledAuthError
} from "@/utils/error-message";
import { isMessageBoxCancel } from "@/utils/message-box";
import { ensurePermission } from "@/utils/permission-guard";
import {
  formatTemplateDataRange,
  formatTemplateHeaderRange,
  getTemplateRegionRanges
} from "./document-template-display";

defineOptions({ name: "DocumentTemplates" });

const loading = ref(false);
const detailLoading = ref(false);
const rows = ref<DocumentTemplateListItem[]>([]);
const customers = ref<Customer[]>([]);
const detailVisible = ref(false);
const detail = ref<DocumentTemplateDetail>();

const filters = reactive({
  customerId: undefined as number | undefined,
  keyword: ""
});

const pagination = reactive({
  page: 1,
  pageSize: 20,
  total: 0
});

const canDelete = computed(() => hasPerms("btn:document-template:delete"));

const tableKindLabels: Record<string, string> = {
  AcceptanceSpec: "验收规格",
  ManualAuxiliary: "辅助表",
  Quotation: "报价表",
  Layout: "Layout",
  Utility: "Utility",
  BomOrSpareParts: "BOM / 备品",
  Unknown: "待判断"
};

const recommendationLabels: Record<string, string> = {
  Recommended: "推荐",
  NeedConfirm: "待确认",
  Optional: "可选",
  Skip: "跳过"
};

const formatDate = (value?: string | null) =>
  value ? dayjs(value).format("YYYY-MM-DD HH:mm") : "-";

const getTableKindLabel = (value: string) =>
  tableKindLabels[value] ?? value ?? "待判断";

const getRecommendationLabel = (value: string) =>
  recommendationLabels[value] ?? value ?? "待确认";

const loadCustomers = async () => {
  try {
    const response = await getCustomerList({ page: 1, pageSize: 100 });
    if (response.code === 0) customers.value = response.data.items;
  } catch (error: unknown) {
    if (!isGloballyHandledAuthError(error)) {
      ElMessage.error(getRequestErrorMessage(error, "加载客户列表失败"));
    }
  }
};

const load = async () => {
  loading.value = true;
  try {
    const response = await getDocumentTemplates({
      page: pagination.page,
      pageSize: pagination.pageSize,
      customerId: filters.customerId,
      keyword: filters.keyword.trim() || undefined
    });
    if (response.code !== 0) {
      ElMessage.error(response.message || "加载结构模板失败");
      return;
    }
    rows.value = response.data.items;
    pagination.total = response.data.total;
  } catch (error: unknown) {
    if (!isGloballyHandledAuthError(error)) {
      ElMessage.error(getRequestErrorMessage(error, "加载结构模板失败"));
    }
  } finally {
    loading.value = false;
  }
};

const search = async () => {
  pagination.page = 1;
  await load();
};

const resetFilters = async () => {
  filters.customerId = undefined;
  filters.keyword = "";
  pagination.page = 1;
  await load();
};

const openDetail = async (row: DocumentTemplateListItem) => {
  detailVisible.value = true;
  detail.value = undefined;
  detailLoading.value = true;
  try {
    const response = await getDocumentTemplate(row.id);
    if (response.code !== 0) {
      ElMessage.error(response.message || "加载模板详情失败");
      detailVisible.value = false;
      return;
    }
    detail.value = response.data;
  } catch (error: unknown) {
    detailVisible.value = false;
    if (!isGloballyHandledAuthError(error)) {
      ElMessage.error(getRequestErrorMessage(error, "加载模板详情失败"));
    }
  } finally {
    detailLoading.value = false;
  }
};

const remove = async (row: DocumentTemplateListItem) => {
  if (
    !ensurePermission(
      "btn:document-template:delete",
      "权限不足，无法删除结构模板"
    )
  ) {
    return;
  }

  try {
    await ElMessageBox.confirm(
      `确定删除客户“${row.customerName}”的结构模板“${row.templateName}”吗？该模板包含 ${row.regionCount} 个区域，删除后后续文件将重新识别。`,
      "删除结构模板",
      {
        confirmButtonText: "删除模板",
        cancelButtonText: "取消",
        type: "warning",
        confirmButtonClass: "el-button--danger"
      }
    );
    const response = await deleteDocumentTemplate(row.id);
    if (response.code !== 0) {
      ElMessage.error(response.message || "删除结构模板失败");
      return;
    }
    ElMessage.success("结构模板已删除");
    if (rows.value.length === 1 && pagination.page > 1) pagination.page -= 1;
    await load();
  } catch (error: unknown) {
    if (isMessageBoxCancel(error) || isGloballyHandledAuthError(error)) return;
    ElMessage.error(getRequestErrorMessage(error, "删除结构模板失败"));
  }
};

onMounted(async () => {
  await Promise.all([loadCustomers(), load()]);
});
</script>

<template>
  <div class="page document-templates-page config-page">
    <el-card class="full-height-table-wrapper template-list-card">
      <template #header>
        <div class="template-heading">
          <div>
            <div class="template-heading__title">结构模板</div>
            <div class="template-heading__description">
              查看智能识别确认后按客户保存的行列结构；范围修正请回到真实文件重新确认。
            </div>
          </div>
          <div class="template-heading__count">
            <span>{{ pagination.total }}</span>
            <small>个已学习模板</small>
          </div>
        </div>
      </template>

      <div class="template-toolbar">
        <el-select
          v-model="filters.customerId"
          class="customer-filter"
          placeholder="全部客户"
          clearable
          filterable
          @change="search"
        >
          <el-option
            v-for="customer in customers"
            :key="customer.id"
            :label="customer.name"
            :value="customer.id"
          />
        </el-select>
        <el-input
          v-model="filters.keyword"
          class="keyword-filter"
          placeholder="搜索模板名称或客户"
          clearable
          @keyup.enter="search"
          @clear="search"
        />
        <el-button type="primary" @click="search">查询</el-button>
        <el-button @click="resetFilters">重置</el-button>
      </div>

      <el-alert
        class="template-note"
        type="info"
        :closable="false"
        show-icon
        title="结构模板保存已确认的表头、数据行和字段列；表格路由规则仅用于人工覆盖整张表的推荐或跳过结果，两者互不替代。"
      />

      <el-table
        v-loading="loading"
        :data="rows"
        stripe
        border
        empty-text="暂无结构模板，确认一次智能识别结果后会自动生成"
      >
        <el-table-column
          prop="templateName"
          label="模板 / 工作表"
          min-width="190"
        >
          <template #default="{ row }">
            <button
              class="template-name-button"
              type="button"
              @click="openDetail(row)"
            >
              <span>{{ row.templateName }}</span>
              <small>#{{ row.id }}</small>
            </button>
          </template>
        </el-table-column>
        <el-table-column prop="customerName" label="客户" min-width="150" />
        <el-table-column label="识别类型" width="120">
          <template #default="{ row }">
            <el-tag size="small" effect="plain">
              {{ getTableKindLabel(row.tableKind) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="区域" width="90" align="center">
          <template #default="{ row }">
            <span class="region-count">{{ row.regionCount }}</span>
          </template>
        </el-table-column>
        <el-table-column label="确认状态" width="110">
          <template #default="{ row }">
            {{ getRecommendationLabel(row.recommendation) }}
          </template>
        </el-table-column>
        <el-table-column
          prop="usageCount"
          label="命中次数"
          width="100"
          align="center"
        />
        <el-table-column label="最近更新" width="170">
          <template #default="{ row }">{{
            formatDate(row.updatedAt)
          }}</template>
        </el-table-column>
        <el-table-column label="最后命中" width="170">
          <template #default="{ row }">{{
            formatDate(row.lastUsedAt)
          }}</template>
        </el-table-column>
        <el-table-column label="操作" width="150" fixed="right">
          <template #default="{ row }">
            <el-button type="primary" link @click="openDetail(row)"
              >查看</el-button
            >
            <el-button v-if="canDelete" type="danger" link @click="remove(row)">
              删除
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <div class="template-pagination">
        <el-pagination
          v-model:current-page="pagination.page"
          v-model:page-size="pagination.pageSize"
          :page-sizes="[20, 50, 100]"
          :total="pagination.total"
          layout="total, sizes, prev, pager, next, jumper"
          @current-change="load"
          @size-change="search"
        />
      </div>
    </el-card>

    <el-drawer
      v-model="detailVisible"
      class="template-detail-drawer"
      size="min(760px, 92vw)"
      destroy-on-close
    >
      <template #header>
        <div class="drawer-heading">
          <div class="drawer-heading__eyebrow">结构模板详情</div>
          <div class="drawer-heading__title">
            {{ detail?.templateName || "正在加载…" }}
          </div>
        </div>
      </template>

      <div v-loading="detailLoading" class="template-detail-body">
        <template v-if="detail">
          <div class="detail-summary">
            <div>
              <span>客户</span><strong>{{ detail.customerName }}</strong>
            </div>
            <div>
              <span>模板编号</span><strong>#{{ detail.id }}</strong>
            </div>
            <div>
              <span>区域数量</span><strong>{{ detail.regions.length }}</strong>
            </div>
            <div>
              <span>命中次数</span><strong>{{ detail.usageCount }}</strong>
            </div>
            <div>
              <span>最近确认</span
              ><strong>{{ formatDate(detail.confirmedAt) }}</strong>
            </div>
            <div>
              <span>最近更新</span
              ><strong>{{ formatDate(detail.updatedAt) }}</strong>
            </div>
          </div>

          <el-alert
            class="coordinate-note"
            type="warning"
            :closable="false"
            show-icon
            title="这里展示保存的模板相对坐标。模板区域不可直接编辑；如识别有误，请上传对应文件，在“调整范围”中修正后确认并学习。"
          />

          <div class="region-stack">
            <section
              v-for="region in detail.regions"
              :key="region.regionIndex"
              class="region-panel"
            >
              <header class="region-panel__header">
                <div class="region-index">{{ region.regionIndex + 1 }}</div>
                <div>
                  <h3>区域 {{ region.regionIndex + 1 }}</h3>
                  <p>
                    表头 {{ formatTemplateHeaderRange(region) }} · 数据
                    {{ formatTemplateDataRange(region) }}
                  </p>
                </div>
                <el-tag
                  v-if="region.isSpecificationOnly"
                  type="warning"
                  effect="plain"
                >
                  仅规格表
                </el-tag>
              </header>

              <div class="coordinate-grid">
                <div
                  v-for="item in getTemplateRegionRanges(region)"
                  :key="item.key"
                  class="coordinate-item"
                >
                  <span>{{ item.label }}</span>
                  <code :class="{ muted: item.value === '-' }">{{
                    item.value
                  }}</code>
                </div>
              </div>

              <div class="header-preview">
                <span class="header-preview__label">表头</span>
                <div class="header-preview__tags">
                  <el-tag
                    v-for="(header, index) in region.headers"
                    :key="`${region.regionIndex}-${index}`"
                    size="small"
                    type="info"
                    effect="plain"
                  >
                    [{{ index + 1 }}] {{ header || "空列" }}
                  </el-tag>
                </div>
              </div>
            </section>
          </div>
        </template>
      </div>
    </el-drawer>
  </div>
</template>

<style scoped>
.template-list-card {
  min-height: calc(100vh - 132px);
}

.template-heading,
.template-toolbar,
.drawer-heading,
.region-panel__header {
  display: flex;
  align-items: center;
}

.template-heading {
  gap: 24px;
  justify-content: space-between;
}

.template-heading__title {
  font-size: 18px;
  font-weight: 650;
  color: var(--el-text-color-primary);
}

.template-heading__description {
  margin-top: 5px;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.template-heading__count {
  display: flex;
  gap: 6px;
  align-items: baseline;
  color: var(--el-color-primary);
  white-space: nowrap;
}

.template-heading__count span {
  font-size: 26px;
  font-weight: 700;
  letter-spacing: -0.04em;
}

.template-heading__count small {
  color: var(--el-text-color-secondary);
}

.template-toolbar {
  flex-wrap: wrap;
  gap: 10px;
  margin-bottom: 14px;
}

.customer-filter {
  width: 220px;
}

.keyword-filter {
  width: min(360px, 100%);
}

.template-note {
  margin-bottom: 16px;
}

.template-name-button {
  display: inline-flex;
  gap: 7px;
  align-items: baseline;
  padding: 0;
  font: inherit;
  color: var(--el-color-primary);
  text-align: left;
  cursor: pointer;
  background: transparent;
  border: 0;
}

.template-name-button span {
  font-weight: 600;
}

.template-name-button small {
  color: var(--el-text-color-placeholder);
}

.region-count {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  font-weight: 700;
  color: var(--el-color-primary);
  background: var(--el-color-primary-light-9);
  border-radius: 50%;
}

.template-pagination {
  display: flex;
  justify-content: flex-end;
  padding-top: 18px;
}

.drawer-heading {
  flex-direction: column;
  gap: 3px;
  align-items: flex-start;
}

.drawer-heading__eyebrow {
  font-size: 12px;
  font-weight: 700;
  color: var(--el-color-primary);
  letter-spacing: 0.12em;
}

.drawer-heading__title {
  font-size: 20px;
  font-weight: 650;
  color: var(--el-text-color-primary);
}

.template-detail-body {
  min-height: 320px;
}

.detail-summary {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 1px;
  overflow: hidden;
  background: var(--el-border-color-lighter);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
}

.detail-summary > div {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 14px 16px;
  background: var(--el-bg-color);
}

.detail-summary span {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.detail-summary strong {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 14px;
  color: var(--el-text-color-primary);
  white-space: nowrap;
}

.coordinate-note {
  margin: 16px 0;
}

.region-stack {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.region-panel {
  padding: 18px;
  background: var(--el-fill-color-extra-light);
  border: 1px solid var(--el-border-color-light);
  border-radius: 10px;
}

.region-panel__header {
  gap: 12px;
}

.region-panel__header h3,
.region-panel__header p {
  margin: 0;
}

.region-panel__header h3 {
  font-size: 15px;
  color: var(--el-text-color-primary);
}

.region-panel__header p {
  margin-top: 4px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.region-panel__header .el-tag {
  margin-left: auto;
}

.region-index {
  display: grid;
  flex: 0 0 32px;
  place-items: center;
  width: 32px;
  height: 32px;
  font-weight: 700;
  color: white;
  background: var(--el-color-primary);
  border-radius: 50%;
}

.coordinate-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  margin-top: 16px;
}

.coordinate-item {
  display: flex;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
  padding: 10px 12px;
  background: var(--el-bg-color);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 7px;
}

.coordinate-item span {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.coordinate-item code {
  font-family: Consolas, SFMono-Regular, monospace;
  font-size: 13px;
  font-weight: 650;
  color: var(--el-color-primary-dark-2);
}

.coordinate-item code.muted {
  color: var(--el-text-color-placeholder);
}

.header-preview {
  display: flex;
  gap: 12px;
  padding-top: 14px;
  margin-top: 14px;
  border-top: 1px dashed var(--el-border-color);
}

.header-preview__label {
  flex: 0 0 auto;
  padding-top: 3px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.header-preview__tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

@media (width <= 720px) {
  .template-heading {
    align-items: flex-start;
  }

  .template-heading__count {
    display: none;
  }

  .customer-filter,
  .keyword-filter {
    width: 100%;
  }

  .detail-summary,
  .coordinate-grid {
    grid-template-columns: 1fr;
  }
}
</style>
