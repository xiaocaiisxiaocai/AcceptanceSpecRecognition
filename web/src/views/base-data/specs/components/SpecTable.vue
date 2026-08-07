<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import {
  ElMessage,
  ElMessageBox,
  type FormInstance,
  type FormRules
} from "element-plus";
import {
  getSpecList,
  detectSpecDuplicateGroups,
  createSpec,
  updateSpec,
  deleteSpec,
  batchDeleteSpecs,
  type AcceptanceSpec,
  type SpecSemanticSearchItem,
  type SpecSemanticSearchRequest,
  type SpecListRequest,
  type SpecDuplicateDetectionResult
} from "@/api/spec";
import type { BusinessOrgOption } from "@/api/org-unit";
import { hasPerms } from "@/utils/auth";
import { formatApiUtcDateTime } from "@/utils/date-time";
import {
  requiredSelectionRule,
  requiredTrimmedRule,
  validateForm
} from "@/utils/form-rules";
import {
  getRequestErrorMessage,
  isGloballyHandledAuthError
} from "@/utils/error-message";
import { isMessageBoxCancel } from "@/utils/message-box";
import SpecDuplicateDialog from "./SpecDuplicateDialog.vue";
import SpecReferenceHistoryDrawer from "./SpecReferenceHistoryDrawer.vue";
import SpecRemarkReplaceDialog from "./SpecRemarkReplaceDialog.vue";
import SpecSemanticSearchDialog from "./SpecSemanticSearchDialog.vue";

const props = withDefaults(
  defineProps<{
    customerId?: number;
    machineModelId?: number;
    processId?: number;
    customerName?: string;
    machineModelName?: string;
    processName?: string;
    orgUnitId?: number;
    businessOrgOptions?: BusinessOrgOption[];
    requiresBusinessOrgSelection?: boolean;
    currentOrgUnitId?: number;
    scopeLabel?: string;
  }>(),
  {
    businessOrgOptions: () => [],
    requiresBusinessOrgSelection: false,
    scopeLabel: "当前范围"
  }
);

const emit = defineEmits<{
  "data-change": [];
}>();

const tableData = ref<AcceptanceSpec[]>([]);
const loading = ref(false);
const total = ref(0);
const selectedRows = ref<AcceptanceSpec[]>([]);
let latestLoadRequestId = 0;

const queryParams = reactive({
  page: 1,
  pageSize: 100,
  keyword: "",
  globalSearch: props.customerId == null
});

const dialogVisible = ref(false);
const dialogTitle = ref("");
const isEdit = ref(false);
const formData = reactive({
  id: 0,
  businessOrgUnitId: null as number | null,
  project: "",
  specification: "",
  acceptance: "",
  remark: ""
});
const formRef = ref<FormInstance>();
const formRules = computed<FormRules<typeof formData>>(() => ({
  project: [requiredTrimmedRule("请输入项目名称")],
  specification: [requiredTrimmedRule("请输入规格内容")],
  ...(!isEdit.value && props.requiresBusinessOrgSelection
    ? {
        businessOrgUnitId: [requiredSelectionRule("请选择所属部门")]
      }
    : {})
}));

const detailDialogVisible = ref(false);
const detailData = ref<AcceptanceSpec | null>(null);
const referenceHistoryVisible = ref(false);
const referenceHistorySpec = ref<AcceptanceSpec | null>(null);
const duplicateDialogVisible = ref(false);
const duplicateLoading = ref(false);
const duplicateResult = ref<SpecDuplicateDetectionResult | null>(null);
const semanticSearchDialogVisible = ref(false);
const remarkReplaceDialogVisible = ref(false);
const semanticSearchDialogRef = ref<InstanceType<
  typeof SpecSemanticSearchDialog
> | null>(null);

const canCreate = computed(() => hasPerms("btn:spec:create"));
const canUpdate = computed(() => hasPerms("btn:spec:update"));
const canDelete = computed(() => hasPerms("btn:spec:delete"));
const canBatchDelete = computed(() => hasPerms("btn:spec:delete-batch"));
const canSemanticSearch = computed(() => hasPerms("btn:spec:semantic-search"));
const canInspectDuplicates = computed(() => hasPerms("api:spec:read"));
const canRemarkReplace = computed(() => hasPerms("btn:spec:remark-replace"));
const canSubmit = computed(() =>
  isEdit.value ? canUpdate.value : canCreate.value
);
const hasSelectedGroup = computed(() => props.customerId != null);
const effectiveOperationOrgUnitId = computed(
  () => props.orgUnitId ?? props.currentOrgUnitId
);
const showOwnerOrgUnit = computed(
  () => props.requiresBusinessOrgSelection && props.orgUnitId == null
);
const orgUnitNameMap = computed(
  () => new Map(props.businessOrgOptions.map(item => [item.id, item.name]))
);
const formatOrgUnitName = (orgUnitId?: number | null) =>
  orgUnitId ? orgUnitNameMap.value.get(orgUnitId) || `部门 ${orgUnitId}` : "-";
const showToolbarRight = computed(
  () =>
    canCreate.value ||
    canBatchDelete.value ||
    canInspectDuplicates.value ||
    canSemanticSearch.value ||
    canRemarkReplace.value
);
const actionColumnWidth = computed(() => {
  const visibleActionCount =
    1 + Number(canUpdate.value) + Number(canDelete.value);
  if (visibleActionCount <= 1) return 90;
  if (visibleActionCount === 2) return 130;
  return 170;
});

const buildRequestParams = (): SpecListRequest => {
  const params: SpecListRequest = {
    page: queryParams.page,
    pageSize: queryParams.pageSize
  };
  if (props.orgUnitId != null) {
    params.orgUnitId = props.orgUnitId;
  }

  if (queryParams.keyword) {
    params.keyword = queryParams.keyword;
  }

  if (queryParams.globalSearch) {
    return params;
  }

  params.customerId = props.customerId;

  if (props.machineModelId != null) {
    params.machineModelId = props.machineModelId;
  } else {
    params.machineModelIdIsNull = true;
  }

  if (props.processId != null) {
    params.processId = props.processId;
  } else {
    params.processIdIsNull = true;
  }

  return params;
};

const buildGroupRequestParams = (): SpecListRequest => {
  const params: SpecListRequest = {
    page: queryParams.page,
    pageSize: queryParams.pageSize
  };
  if (props.orgUnitId != null) {
    params.orgUnitId = props.orgUnitId;
  }

  if (queryParams.keyword) {
    params.keyword = queryParams.keyword;
  }

  params.customerId = props.customerId;

  if (props.machineModelId != null) {
    params.machineModelId = props.machineModelId;
  } else {
    params.machineModelIdIsNull = true;
  }

  if (props.processId != null) {
    params.processId = props.processId;
  } else {
    params.processIdIsNull = true;
  }

  return params;
};

const loadData = async () => {
  const requestId = ++latestLoadRequestId;
  loading.value = true;
  try {
    const res = await getSpecList(buildRequestParams());
    if (requestId !== latestLoadRequestId) return;
    if (res.code === 0) {
      tableData.value = res.data.items;
      total.value = res.data.total;
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    if (requestId !== latestLoadRequestId) return;
    ElMessage.error("加载数据失败");
  } finally {
    if (requestId === latestLoadRequestId) {
      loading.value = false;
    }
  }
};

const reloadSemanticSearchIfNeeded = async () => {
  if (!semanticSearchDialogVisible.value) return;
  await semanticSearchDialogRef.value?.reloadLastSearch();
};

watch(
  () => [
    props.customerId,
    props.machineModelId,
    props.processId,
    props.orgUnitId
  ],
  () => {
    queryParams.page = 1;
    queryParams.keyword = "";
    queryParams.globalSearch = props.customerId == null;
    selectedRows.value = [];
    duplicateDialogVisible.value = false;
    duplicateResult.value = null;
    semanticSearchDialogVisible.value = false;
    remarkReplaceDialogVisible.value = false;
    referenceHistoryVisible.value = false;
    referenceHistorySpec.value = null;
    loadData();
  },
  { immediate: true }
);

const handleSearch = () => {
  queryParams.page = 1;
  loadData();
};

const handleReset = () => {
  queryParams.keyword = "";
  queryParams.globalSearch = props.customerId == null;
  queryParams.page = 1;
  loadData();
};

const handleGlobalSearchChange = () => {
  if (!hasSelectedGroup.value) {
    queryParams.globalSearch = true;
  }
  queryParams.page = 1;
  loadData();
};

const openCreateDialog = () => {
  dialogTitle.value = "新增验收规格";
  isEdit.value = false;
  formData.id = 0;
  formData.businessOrgUnitId =
    props.orgUnitId ?? props.currentOrgUnitId ?? null;
  formData.project = "";
  formData.specification = "";
  formData.acceptance = "";
  formData.remark = "";
  dialogVisible.value = true;
};

const handleAdd = () => {
  if (!canCreate.value) {
    ElMessage.error("权限不足，无法新增规格");
    return;
  }
  if (props.customerId == null) {
    ElMessage.warning("请先在左侧选择分组后再新增规格");
    return;
  }
  openCreateDialog();
};

const openEditDialog = (row: AcceptanceSpec) => {
  dialogTitle.value = "编辑验收规格";
  isEdit.value = true;
  formData.id = row.id;
  formData.businessOrgUnitId = row.ownerOrgUnitId ?? null;
  formData.project = row.project;
  formData.specification = row.specification;
  formData.acceptance = row.acceptance || "";
  formData.remark = row.remark || "";
  dialogVisible.value = true;
};

const handleEdit = (row: AcceptanceSpec) => {
  if (!canUpdate.value) {
    ElMessage.error("权限不足，无法编辑规格");
    return;
  }
  openEditDialog(row);
};

const openDetailDialog = (row: AcceptanceSpec) => {
  detailData.value = row;
  detailDialogVisible.value = true;
};

const handleView = (row: AcceptanceSpec) => {
  openDetailDialog(row);
};

const handleReferenceHistory = (row: AcceptanceSpec) => {
  if (row.referenceCount <= 0 && row.referenceVersion <= 1) return;
  referenceHistorySpec.value = row;
  referenceHistoryVisible.value = true;
};

const handleDelete = async (row: AcceptanceSpec) => {
  if (!canDelete.value) {
    ElMessage.error("权限不足，无法删除规格");
    return;
  }

  try {
    await ElMessageBox.confirm(
      `确定要删除项目"${row.project}"的验收规格吗？`,
      "提示",
      { confirmButtonText: "确定", cancelButtonText: "取消", type: "warning" }
    );

    const res = await deleteSpec(row.id);
    if (res.code === 0) {
      ElMessage.success("删除成功");
      await loadData();
      await reloadSemanticSearchIfNeeded();
      emit("data-change");
    } else {
      ElMessage.error(res.message);
    }
  } catch (error) {
    if (isMessageBoxCancel(error) || isGloballyHandledAuthError(error)) return;
    ElMessage.error(getRequestErrorMessage(error, "删除失败"));
  }
};

const handleBatchDelete = async () => {
  if (!canBatchDelete.value) {
    ElMessage.error("权限不足，无法批量删除规格");
    return;
  }
  if (selectedRows.value.length === 0) {
    ElMessage.warning("请先选择要删除的规格");
    return;
  }

  const ids = selectedRows.value.map(row => row.id).sort((a, b) => a - b);
  const deleteCount = ids.length;

  try {
    await ElMessageBox.confirm(
      `即将永久删除选中的 ${deleteCount} 条规格，删除后无法恢复。是否继续？`,
      "批量删除确认",
      {
        confirmButtonText: "继续",
        cancelButtonText: "取消",
        type: "warning"
      }
    );

    await ElMessageBox.prompt(
      `请输入数字 ${deleteCount}，确认永久删除这些规格。`,
      "二次确认",
      {
        confirmButtonText: "永久删除",
        cancelButtonText: "取消",
        type: "error",
        inputPlaceholder: `请输入 ${deleteCount}`,
        inputValidator: value =>
          value.trim() === String(deleteCount) ||
          `请输入数字 ${deleteCount} 以确认删除`
      }
    );

    const currentIds = selectedRows.value
      .map(row => row.id)
      .sort((a, b) => a - b);
    if (
      currentIds.length !== ids.length ||
      currentIds.some((id, index) => id !== ids[index])
    ) {
      ElMessage.warning("选择内容已变化，请重新执行批量删除");
      return;
    }

    const res = await batchDeleteSpecs(ids);
    if (res.code === 0) {
      ElMessage.success("删除成功");
      selectedRows.value = [];
      await loadData();
      await reloadSemanticSearchIfNeeded();
      emit("data-change");
    } else {
      ElMessage.error(res.message);
    }
  } catch (error) {
    if (isMessageBoxCancel(error) || isGloballyHandledAuthError(error)) return;
    ElMessage.error(getRequestErrorMessage(error, "批量删除失败"));
  }
};

const handleInspectDuplicates = async () => {
  if (!canInspectDuplicates.value) {
    ElMessage.error("权限不足，无法执行重复排查");
    return;
  }
  if (!hasSelectedGroup.value) {
    ElMessage.warning("请先在左侧选择分组后再执行重复排查");
    return;
  }

  duplicateDialogVisible.value = true;
  duplicateLoading.value = true;
  duplicateResult.value = null;

  try {
    const res = await detectSpecDuplicateGroups({
      ...buildGroupRequestParams(),
      maxGroups: 30
    });
    if (res.code === 0) {
      duplicateResult.value = res.data;
    } else {
      duplicateResult.value = null;
      ElMessage.error(res.message);
    }
  } catch {
    duplicateResult.value = null;
    ElMessage.error("重复排查失败");
  } finally {
    duplicateLoading.value = false;
  }
};

const handleOpenSemanticSearch = () => {
  if (!canSemanticSearch.value) {
    ElMessage.error("权限不足，无法执行AI搜索");
    return;
  }
  if (!hasSelectedGroup.value) {
    ElMessage.warning("请先在左侧选择分组后再执行AI搜索");
    return;
  }
  semanticSearchDialogVisible.value = true;
};

const handleOpenRemarkReplace = () => {
  if (!canRemarkReplace.value) {
    ElMessage.error("权限不足，无法批量替换备注");
    return;
  }
  if (!effectiveOperationOrgUnitId.value) {
    ElMessage.warning("请先在上方数据范围选择具体部门");
    return;
  }
  remarkReplaceDialogVisible.value = true;
};

const handleRemarkReplaceSuccess = async () => {
  duplicateDialogVisible.value = false;
  duplicateResult.value = null;
  await loadData();
  await reloadSemanticSearchIfNeeded();
  emit("data-change");
};

const handleSemanticSearchView = (row: SpecSemanticSearchItem) => {
  openDetailDialog(row);
};

const handleSemanticSearchEdit = ({
  row
}: {
  row: SpecSemanticSearchItem;
  scope: Readonly<SpecSemanticSearchRequest>;
}) => {
  if (!canUpdate.value) {
    ElMessage.error("权限不足，无法编辑规格");
    return;
  }
  openEditDialog(row);
};

const handleSelectionChange = (rows: AcceptanceSpec[]) => {
  selectedRows.value = rows;
};

const handleSubmit = async () => {
  if (!canSubmit.value) {
    ElMessage.error("权限不足，无法提交当前操作");
    return;
  }
  if (!(await validateForm(formRef.value))) return;

  try {
    const res = isEdit.value
      ? await updateSpec(formData.id, {
          project: formData.project,
          specification: formData.specification,
          acceptance: formData.acceptance || undefined,
          remark: formData.remark || undefined
        })
      : await createSpec({
          businessOrgUnitId: formData.businessOrgUnitId ?? undefined,
          customerId: props.customerId!,
          processId: props.processId,
          machineModelId: props.machineModelId,
          project: formData.project,
          specification: formData.specification,
          acceptance: formData.acceptance || undefined,
          remark: formData.remark || undefined
        });

    if (res.code === 0) {
      ElMessage.success(isEdit.value ? "更新成功" : "创建成功");
      dialogVisible.value = false;
      await loadData();
      await reloadSemanticSearchIfNeeded();
      emit("data-change");
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("操作失败");
  }
};

const handlePageChange = (page: number) => {
  queryParams.page = page;
  loadData();
};

const handleSizeChange = (size: number) => {
  queryParams.pageSize = size;
  queryParams.page = 1;
  loadData();
};

const groupLabel = () => {
  const prefix = props.scopeLabel ? `${props.scopeLabel} / ` : "";
  if (props.customerId == null) {
    return `${prefix}全局搜索`;
  }
  const parts = [props.customerName];
  parts.push(props.machineModelName || "未指定机型");
  parts.push(props.processName || "未指定制程");
  return `${prefix}${parts.join(" / ")}`;
};

const scopeBreadcrumbItems = computed(() =>
  queryParams.globalSearch
    ? [props.scopeLabel, "验收规格", "全局搜索"]
    : [
        props.scopeLabel,
        props.customerName || "未选择客户",
        props.machineModelName || "未指定机型",
        props.processName || "未指定制程"
      ]
);
</script>

<template>
  <div class="spec-table">
    <nav class="scope-breadcrumb" aria-label="当前验收规格范围">
      <el-breadcrumb separator="/">
        <el-breadcrumb-item
          v-for="(item, index) in scopeBreadcrumbItems"
          :key="`${index}-${item}`"
        >
          <span
            :class="{ 'is-current': index === scopeBreadcrumbItems.length - 1 }"
          >
            {{ item }}
          </span>
        </el-breadcrumb-item>
      </el-breadcrumb>
    </nav>

    <div class="toolbar">
      <div class="toolbar-left">
        <el-input
          v-model="queryParams.keyword"
          placeholder="项目/规格/验收标准/备注"
          clearable
          style="width: 260px"
          @keyup.enter="handleSearch"
        />
        <el-checkbox
          v-model="queryParams.globalSearch"
          :disabled="!hasSelectedGroup"
          @change="handleGlobalSearchChange"
        >
          全局搜索
        </el-checkbox>
        <el-button type="primary" @click="handleSearch">搜索</el-button>
        <el-button @click="handleReset">重置</el-button>
      </div>
      <div v-if="showToolbarRight" class="toolbar-right">
        <el-button v-if="canInspectDuplicates" @click="handleInspectDuplicates">
          重复排查
        </el-button>
        <el-button v-if="canSemanticSearch" @click="handleOpenSemanticSearch">
          AI搜索
        </el-button>
        <el-button v-if="canRemarkReplace" @click="handleOpenRemarkReplace">
          批量替换备注
        </el-button>
        <el-button v-if="canCreate" type="primary" @click="handleAdd">
          新增规格
        </el-button>
        <el-button
          v-if="canBatchDelete"
          type="danger"
          :disabled="selectedRows.length === 0"
          @click="handleBatchDelete"
        >
          批量删除
        </el-button>
      </div>
    </div>

    <div class="table-main">
      <el-table
        v-loading="loading"
        :data="tableData"
        stripe
        height="100%"
        @selection-change="handleSelectionChange"
      >
        <el-table-column v-if="canBatchDelete" type="selection" width="50" />
        <el-table-column
          v-if="showOwnerOrgUnit"
          label="所属部门"
          width="140"
          show-overflow-tooltip
        >
          <template #default="{ row }">
            {{ formatOrgUnitName(row.ownerOrgUnitId) }}
          </template>
        </el-table-column>
        <el-table-column
          prop="project"
          label="项目"
          width="140"
          show-overflow-tooltip
        >
          <template #default="{ row }">
            <span class="line-clamp-1" :title="row.project">{{
              row.project
            }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="specification" label="规格内容" min-width="260">
          <template #default="{ row }">
            <div class="specification-multiline" :title="row.specification">
              {{ row.specification }}
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="acceptance" label="验收标准" min-width="150">
          <template #default="{ row }">
            <span
              v-if="row.acceptance"
              class="line-clamp-1"
              :title="row.acceptance"
              >{{ row.acceptance }}</span
            >
            <span v-else class="text-gray-400">-</span>
          </template>
        </el-table-column>
        <el-table-column prop="remark" label="备注" min-width="150">
          <template #default="{ row }">
            <span v-if="row.remark" class="line-clamp-1" :title="row.remark">{{
              row.remark
            }}</span>
            <span v-else class="text-gray-400">-</span>
          </template>
        </el-table-column>
        <el-table-column label="引用次数" width="88" align="center">
          <template #default="{ row }">
            <el-button
              v-if="row.referenceCount > 0 || row.referenceVersion > 1"
              type="primary"
              link
              @click="handleReferenceHistory(row)"
            >
              {{ row.referenceCount }}
            </el-button>
            <span v-else>0</span>
          </template>
        </el-table-column>
        <el-table-column label="更新时间" width="180">
          <template #default="{ row }">
            {{ formatApiUtcDateTime(row.updatedAt ?? row.importedAt) }}
          </template>
        </el-table-column>
        <el-table-column label="最近引用时间" width="180">
          <template #default="{ row }">
            {{
              row.lastReferencedAtUtc
                ? formatApiUtcDateTime(row.lastReferencedAtUtc)
                : "-"
            }}
          </template>
        </el-table-column>
        <el-table-column label="操作" :width="actionColumnWidth" fixed="right">
          <template #default="{ row }">
            <el-button type="primary" link @click="handleView(row)">
              查看
            </el-button>
            <el-button
              v-if="canUpdate"
              type="primary"
              link
              @click="handleEdit(row)"
            >
              编辑
            </el-button>
            <el-button
              v-if="canDelete"
              type="danger"
              link
              @click="handleDelete(row)"
            >
              删除
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </div>

    <div class="pagination">
      <el-pagination
        v-model:current-page="queryParams.page"
        v-model:page-size="queryParams.pageSize"
        :page-sizes="[100, 200, 500]"
        :total="total"
        layout="total, sizes, prev, pager, next, jumper"
        @size-change="handleSizeChange"
        @current-change="handlePageChange"
      />
    </div>

    <el-dialog
      v-model="dialogVisible"
      :title="dialogTitle"
      width="min(640px, calc(100vw - 32px))"
    >
      <el-form
        ref="formRef"
        :model="formData"
        :rules="formRules"
        label-width="100px"
        status-icon
      >
        <el-form-item
          v-if="!isEdit && requiresBusinessOrgSelection"
          label="所属部门"
          prop="businessOrgUnitId"
        >
          <el-select
            v-model="formData.businessOrgUnitId"
            placeholder="请选择所属部门"
            filterable
            :disabled="orgUnitId != null"
            style="width: 100%"
          >
            <el-option
              v-for="option in businessOrgOptions"
              :key="option.id"
              :label="option.name"
              :value="option.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="项目名称" prop="project">
          <el-input
            v-model="formData.project"
            placeholder="请输入项目名称"
            maxlength="500"
          />
        </el-form-item>
        <el-form-item label="规格内容" prop="specification">
          <el-input
            v-model="formData.specification"
            type="textarea"
            :rows="3"
            placeholder="请输入规格内容"
          />
        </el-form-item>
        <el-form-item label="验收标准">
          <el-input
            v-model="formData.acceptance"
            type="textarea"
            :rows="2"
            placeholder="请输入验收标准（可选）"
          />
        </el-form-item>
        <el-form-item label="备注">
          <el-input
            v-model="formData.remark"
            type="textarea"
            :rows="2"
            placeholder="请输入备注（可选）"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button v-if="canSubmit" type="primary" @click="handleSubmit">
          确定
        </el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="detailDialogVisible"
      title="规格详情"
      width="min(640px, calc(100vw - 32px))"
    >
      <el-descriptions v-if="detailData" :column="1" border>
        <el-descriptions-item label="ID">{{
          detailData.id
        }}</el-descriptions-item>
        <el-descriptions-item label="客户">{{
          detailData.customerName || customerName
        }}</el-descriptions-item>
        <el-descriptions-item label="机型">{{
          detailData.machineModelName || machineModelName || "-"
        }}</el-descriptions-item>
        <el-descriptions-item label="制程">{{
          detailData.processName || processName || "-"
        }}</el-descriptions-item>
        <el-descriptions-item
          v-if="requiresBusinessOrgSelection"
          label="所属部门"
        >
          {{ formatOrgUnitName(detailData.ownerOrgUnitId) }}
        </el-descriptions-item>
        <el-descriptions-item label="项目">{{
          detailData.project
        }}</el-descriptions-item>
        <el-descriptions-item label="规格内容">
          <div class="whitespace-pre-wrap">{{ detailData.specification }}</div>
        </el-descriptions-item>
        <el-descriptions-item label="验收标准">
          <div class="whitespace-pre-wrap">
            {{ detailData.acceptance || "-" }}
          </div>
        </el-descriptions-item>
        <el-descriptions-item label="备注">
          <div class="whitespace-pre-wrap">
            {{ detailData.remark || "-" }}
          </div>
        </el-descriptions-item>
        <el-descriptions-item label="引用次数">
          <el-button
            v-if="
              detailData.referenceCount > 0 || detailData.referenceVersion > 1
            "
            type="primary"
            link
            @click="handleReferenceHistory(detailData)"
          >
            {{ detailData.referenceCount }}
          </el-button>
          <span v-else>0</span>
        </el-descriptions-item>
        <el-descriptions-item label="导入时间">{{
          formatApiUtcDateTime(detailData.importedAt)
        }}</el-descriptions-item>
      </el-descriptions>
      <template #footer>
        <el-button @click="detailDialogVisible = false">关闭</el-button>
      </template>
    </el-dialog>

    <SpecDuplicateDialog
      v-model="duplicateDialogVisible"
      :loading="duplicateLoading"
      :result="duplicateResult"
      :group-label="groupLabel()"
    />

    <SpecSemanticSearchDialog
      ref="semanticSearchDialogRef"
      v-model="semanticSearchDialogVisible"
      :group-label="groupLabel()"
      :customer-id="customerId ?? 0"
      :machine-model-id="machineModelId"
      :process-id="processId"
      :org-unit-id="orgUnitId"
      :allow-edit="canUpdate"
      @view="handleSemanticSearchView"
      @edit="handleSemanticSearchEdit"
    />

    <SpecRemarkReplaceDialog
      v-if="effectiveOperationOrgUnitId"
      v-model="remarkReplaceDialogVisible"
      :org-unit-id="effectiveOperationOrgUnitId"
      :scope-label="scopeLabel"
      @success="handleRemarkReplaceSuccess"
    />

    <SpecReferenceHistoryDrawer
      v-model="referenceHistoryVisible"
      :spec="referenceHistorySpec"
    />
  </div>
</template>

<style scoped>
.spec-table {
  display: flex;
  flex-direction: column;
  gap: 12px;
  height: 100%;
  min-height: 0;
}

.scope-breadcrumb {
  display: flex;
  align-items: center;
  min-height: 32px;
  padding: 0 2px;
  margin-bottom: 4px;
}

.scope-breadcrumb :deep(.el-breadcrumb__inner) {
  font-weight: 400;
  color: var(--app-text-secondary);
}

.scope-breadcrumb .is-current {
  font-weight: 600;
  color: var(--app-text-primary);
}

.toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  justify-content: space-between;
}

.toolbar-left,
.toolbar-right {
  display: flex;
  gap: 8px;
  align-items: center;
}

.table-main {
  flex: 1;
  min-height: 0;
}

.pagination {
  display: flex;
  flex-shrink: 0;
  justify-content: flex-end;
  margin-top: 12px;
}

.line-clamp-1 {
  display: -webkit-box;
  overflow: hidden;
  -webkit-line-clamp: 1;
  -webkit-box-orient: vertical;
}

.specification-multiline {
  line-height: 1.6;
  overflow-wrap: anywhere;
  white-space: pre-wrap;
}
</style>
