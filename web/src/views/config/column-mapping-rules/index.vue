<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import {
  ElMessage,
  ElMessageBox,
  type FormInstance,
  type FormRules
} from "element-plus";
import {
  ColumnMappingMatchMode,
  ColumnMappingRuleSource,
  ColumnMappingTargetField,
  createColumnMappingRule,
  deleteColumnMappingRule,
  getColumnMappingRules,
  restoreColumnMappingRuleDefaults,
  updateColumnMappingRule,
  type ColumnMappingRule
} from "@/api/column-mapping-rules";
import { getCustomerList, type Customer } from "@/api/customer";
import { hasPerms } from "@/utils/auth";
import {
  getRequestErrorMessage,
  isGloballyHandledAuthError
} from "@/utils/error-message";
import { isMessageBoxCancel } from "@/utils/message-box";
import { ensurePermission } from "@/utils/permission-guard";
import {
  requiredSelectionRule,
  requiredTrimmedRule,
  validateForm
} from "@/utils/form-rules";
import { loadAllPagedItems } from "@/utils/paged-options";

defineOptions({
  name: "ColumnMappingRules"
});

const loading = ref(false);
const customerLoading = ref(false);
const rules = ref<ColumnMappingRule[]>([]);
const customers = ref<Customer[]>([]);

const activeTarget = ref<ColumnMappingTargetField>(
  ColumnMappingTargetField.Project
);

const targetOptions = [
  { label: "项目", value: ColumnMappingTargetField.Project },
  { label: "规格内容", value: ColumnMappingTargetField.Specification },
  { label: "验收标准", value: ColumnMappingTargetField.Acceptance },
  { label: "备注", value: ColumnMappingTargetField.Remark }
];

const matchModeOptions = [
  { label: "相等", value: ColumnMappingMatchMode.Equals },
  { label: "包含", value: ColumnMappingMatchMode.Contains },
  { label: "正则", value: ColumnMappingMatchMode.Regex }
];

const sourceOptions = [
  { label: "内置", value: ColumnMappingRuleSource.Builtin, type: "info" },
  { label: "手动", value: ColumnMappingRuleSource.Manual, type: "primary" },
  { label: "学习", value: ColumnMappingRuleSource.Learned, type: "success" }
] as const;

const tabKeywords = reactive({
  [ColumnMappingTargetField.Project]: "",
  [ColumnMappingTargetField.Specification]: "",
  [ColumnMappingTargetField.Acceptance]: "",
  [ColumnMappingTargetField.Remark]: ""
});

const pageSizes = [20, 50, 100, 200];

const pageState = reactive<
  Record<ColumnMappingTargetField, { page: number; pageSize: number }>
>({
  [ColumnMappingTargetField.Project]: { page: 1, pageSize: 20 },
  [ColumnMappingTargetField.Specification]: { page: 1, pageSize: 20 },
  [ColumnMappingTargetField.Acceptance]: { page: 1, pageSize: 20 },
  [ColumnMappingTargetField.Remark]: { page: 1, pageSize: 20 }
});

const canCreate = computed(() => hasPerms("btn:column-mapping-rule:create"));
const canUpdate = computed(() => hasPerms("btn:column-mapping-rule:update"));
const canDelete = computed(() => hasPerms("btn:column-mapping-rule:delete"));
const canSubmit = computed(() =>
  isEdit.value ? canUpdate.value : canCreate.value
);
const hasOperationActions = computed(() => canUpdate.value || canDelete.value);

const getMatchModeLabel = (matchMode: ColumnMappingMatchMode) =>
  matchModeOptions
    .find(option => option.value === matchMode)
    ?.label.toLowerCase() ?? "";

const getSourceOption = (source?: ColumnMappingRuleSource) =>
  sourceOptions.find(option => option.value === source) ?? sourceOptions[1];

const customerNameById = computed(
  () =>
    new Map(
      customers.value.map(customer => [customer.id, customer.name] as const)
    )
);

const formatCustomerScope = (customerId?: number) => {
  if (customerId === undefined) return "全局";

  const customerName = customerNameById.value.get(customerId);
  return customerName
    ? `${customerName}（ID: ${customerId}）`
    : `未知客户（ID: ${customerId}）`;
};

const filteredRulesByTarget = computed(() => {
  const result = {} as Record<ColumnMappingTargetField, ColumnMappingRule[]>;

  targetOptions.forEach(target => {
    const keyword = tabKeywords[target.value].trim().toLowerCase();

    result[target.value] = rules.value
      .filter(rule => rule.targetField === target.value)
      .filter(rule => {
        if (!keyword) {
          return true;
        }

        const matchModeLabel = getMatchModeLabel(rule.matchMode);
        const sourceLabel = getSourceOption(rule.source).label.toLowerCase();
        return (
          rule.pattern.toLowerCase().includes(keyword) ||
          matchModeLabel.includes(keyword) ||
          sourceLabel.includes(keyword) ||
          formatCustomerScope(rule.customerId).toLowerCase().includes(keyword)
        );
      })
      .sort((a, b) => (b.priority ?? 0) - (a.priority ?? 0) || a.id - b.id);
  });

  return result;
});

const pagedRulesByTarget = computed(() => {
  const result = {} as Record<ColumnMappingTargetField, ColumnMappingRule[]>;

  targetOptions.forEach(target => {
    const state = pageState[target.value];
    const filteredRules = filteredRulesByTarget.value[target.value];
    const start = (state.page - 1) * state.pageSize;
    result[target.value] = filteredRules.slice(start, start + state.pageSize);
  });

  return result;
});

const resetPage = (targetField?: ColumnMappingTargetField) => {
  const targets = targetField
    ? [targetField]
    : targetOptions.map(target => target.value);
  targets.forEach(target => {
    pageState[target].page = 1;
  });
};

const load = async () => {
  loading.value = true;
  try {
    const res = await getColumnMappingRules();
    if (res.code === 0) {
      rules.value = res.data || [];
      resetPage();
    } else {
      ElMessage.error(res.message || "加载规则失败");
    }
  } catch {
    ElMessage.error("加载规则失败");
  } finally {
    loading.value = false;
  }
};

const loadCustomers = async () => {
  customerLoading.value = true;
  try {
    customers.value = await loadAllPagedItems(
      (page, pageSize, signal) =>
        getCustomerList({ page, pageSize }, { signal }),
      { getKey: customer => customer.id }
    );
  } catch (error: unknown) {
    if (!isGloballyHandledAuthError(error)) {
      ElMessage.error(
        getRequestErrorMessage(
          error,
          "加载客户列表失败，客户域将暂时显示客户 ID"
        )
      );
    }
  } finally {
    customerLoading.value = false;
  }
};

watch(tabKeywords, () => resetPage(), { deep: true });

const dialogVisible = ref(false);
const dialogTitle = ref("新增规则");
const isEdit = ref(false);

const form = reactive({
  id: 0,
  targetField: ColumnMappingTargetField.Project,
  matchMode: ColumnMappingMatchMode.Equals,
  pattern: "",
  priority: 0,
  enabled: true,
  source: ColumnMappingRuleSource.Manual,
  customerId: undefined as number | undefined
});
const formRef = ref<FormInstance>();
const formRules: FormRules<typeof form> = {
  targetField: [requiredSelectionRule("请选择目标字段")],
  pattern: [requiredTrimmedRule("请输入匹配词")]
};

const openAdd = () => {
  if (
    !ensurePermission(
      "btn:column-mapping-rule:create",
      "权限不足，无法新增列映射规则"
    )
  ) {
    return;
  }

  isEdit.value = false;
  dialogTitle.value = "新增规则";
  form.id = 0;
  form.targetField = activeTarget.value;
  form.matchMode = ColumnMappingMatchMode.Equals;
  form.pattern = "";
  form.priority = 0;
  form.enabled = true;
  form.source = ColumnMappingRuleSource.Manual;
  form.customerId = undefined;
  dialogVisible.value = true;
};

const openEdit = (row: ColumnMappingRule) => {
  if (
    !ensurePermission(
      "btn:column-mapping-rule:update",
      "权限不足，无法编辑列映射规则"
    )
  ) {
    return;
  }

  isEdit.value = true;
  dialogTitle.value = "编辑规则";
  form.id = row.id;
  form.targetField = row.targetField;
  form.matchMode = row.matchMode;
  form.pattern = row.pattern;
  form.priority = row.priority ?? 0;
  form.enabled = row.enabled;
  form.source = row.source ?? ColumnMappingRuleSource.Manual;
  form.customerId = row.customerId;
  dialogVisible.value = true;
};

const submit = async () => {
  if (
    !ensurePermission(
      isEdit.value
        ? "btn:column-mapping-rule:update"
        : "btn:column-mapping-rule:create",
      isEdit.value
        ? "权限不足，无法保存列映射规则"
        : "权限不足，无法新增列映射规则"
    )
  ) {
    return;
  }

  if (!(await validateForm(formRef.value))) return;

  const pattern = form.pattern.trim();

  try {
    const payload = {
      targetField: form.targetField,
      matchMode: form.matchMode,
      pattern,
      priority: Number(form.priority) || 0,
      enabled: form.enabled,
      source: form.source,
      customerId: form.customerId
    };

    const res = isEdit.value
      ? await updateColumnMappingRule(form.id, payload)
      : await createColumnMappingRule(payload);

    if (res.code === 0) {
      ElMessage.success(isEdit.value ? "更新成功" : "创建成功");
      dialogVisible.value = false;
      await load();
    } else {
      ElMessage.error(res.message || "保存失败");
    }
  } catch (error: unknown) {
    ElMessage.error(getRequestErrorMessage(error, "保存失败"));
  }
};

const persistRow = async (row: ColumnMappingRule) => {
  if (
    !ensurePermission(
      "btn:column-mapping-rule:update",
      "权限不足，无法更新列映射规则"
    )
  ) {
    await load();
    return;
  }

  try {
    const res = await updateColumnMappingRule(row.id, {
      targetField: row.targetField,
      matchMode: row.matchMode,
      pattern: row.pattern,
      priority: row.priority ?? 0,
      enabled: row.enabled,
      source: row.source ?? ColumnMappingRuleSource.Manual,
      customerId: row.customerId
    });
    if (res.code !== 0) {
      ElMessage.error(res.message || "更新失败");
      await load();
    }
  } catch {
    ElMessage.error("更新失败");
    await load();
  }
};

const toggleEnabled = async (row: ColumnMappingRule) => {
  await persistRow(row);
};

const remove = async (row: ColumnMappingRule) => {
  if (
    !ensurePermission(
      "btn:column-mapping-rule:delete",
      "权限不足，无法删除列映射规则"
    )
  ) {
    return;
  }

  try {
    await ElMessageBox.confirm(`确定删除匹配词 "${row.pattern}" 吗？`, "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });
    const res = await deleteColumnMappingRule(row.id);
    if (res.code === 0) {
      ElMessage.success("删除成功");
      await load();
    } else {
      ElMessage.error(res.message || "删除失败");
    }
  } catch (error) {
    if (isMessageBoxCancel(error) || isGloballyHandledAuthError(error)) return;
    ElMessage.error(getRequestErrorMessage(error, "删除失败"));
  }
};

const restoreDefaults = async () => {
  if (
    !ensurePermission(
      "btn:column-mapping-rule:create",
      "权限不足，无法恢复默认词"
    )
  ) {
    return;
  }

  try {
    await ElMessageBox.confirm(
      "将补齐缺失的内置全局默认词，不会改动手动、学习或客户级规则。重启只在某字段内置词全空时兜底补齐。",
      "恢复默认词",
      {
        confirmButtonText: "恢复",
        cancelButtonText: "取消",
        type: "warning"
      }
    );
    const res = await restoreColumnMappingRuleDefaults(activeTarget.value);
    if (res.code === 0) {
      ElMessage.success(`默认词已恢复，新增 ${res.data?.added ?? 0} 条`);
      await load();
    } else {
      ElMessage.error(res.message || "恢复默认词失败");
    }
  } catch (error: unknown) {
    if (isMessageBoxCancel(error) || isGloballyHandledAuthError(error)) return;
    ElMessage.error(getRequestErrorMessage(error, "恢复默认词失败"));
  }
};

onMounted(() => {
  void load();
  void loadCustomers();
});
</script>

<template>
  <div class="page page--fill column-rules config-page">
    <el-card class="column-rules-card table-card">
      <template #header>
        <div class="list-card-toolbar">
          <div class="list-card-toolbar__right">
            <el-button v-if="canCreate" type="primary" @click="openAdd">
              新增规则
            </el-button>
            <el-button v-if="canCreate" @click="restoreDefaults">
              恢复默认词
            </el-button>
          </div>
        </div>
      </template>

      <div class="mb-3 text-sm text-gray-500">
        用于 Word 导入和智能填充时自动识别表头列，例如将“工艺流程 / 项目 /
        项目管理”都映射到“项目”列。删除单个内置词后可通过“恢复默认词”补齐；重启只在某字段内置词全空时兜底补齐。
      </div>

      <el-tabs v-model="activeTarget" class="column-rules-tabs" type="card">
        <el-tab-pane
          v-for="target in targetOptions"
          :key="target.value"
          :name="target.value"
          :label="target.label"
        >
          <div class="rule-pane">
            <div class="rule-toolbar">
              <el-input
                v-model="tabKeywords[target.value]"
                class="rule-search-input"
                placeholder="搜索当前字段的匹配词 / 匹配模式 / 来源 / 客户"
                clearable
              />
            </div>

            <div class="rule-table-wrap table-region">
              <el-table
                v-loading="loading"
                :data="pagedRulesByTarget[target.value]"
                stripe
                border
                height="100%"
              >
                <el-table-column
                  prop="pattern"
                  label="匹配词"
                  min-width="200"
                />
                <el-table-column label="匹配模式" min-width="160">
                  <template #default="{ row }">
                    <el-select
                      v-model="row.matchMode"
                      size="small"
                      :disabled="!canUpdate"
                      class="table-select"
                      popper-class="config-select-popper"
                      @change="() => persistRow(row)"
                    >
                      <el-option
                        v-for="mode in matchModeOptions"
                        :key="mode.value"
                        :label="mode.label"
                        :value="mode.value"
                      />
                    </el-select>
                  </template>
                </el-table-column>
                <el-table-column label="来源" width="110">
                  <template #default="{ row }">
                    <el-tag
                      :type="getSourceOption(row.source).type"
                      effect="plain"
                      size="small"
                    >
                      {{ getSourceOption(row.source).label }}
                    </el-tag>
                  </template>
                </el-table-column>
                <el-table-column label="客户域" min-width="180">
                  <template #default="{ row }">
                    <el-tag
                      :type="row.customerId ? 'success' : 'info'"
                      effect="plain"
                      size="small"
                      :title="formatCustomerScope(row.customerId)"
                    >
                      {{ formatCustomerScope(row.customerId) }}
                    </el-tag>
                  </template>
                </el-table-column>
                <el-table-column label="优先级" width="140">
                  <template #default="{ row }">
                    <el-input-number
                      v-model="row.priority"
                      class="table-number-input"
                      size="small"
                      :disabled="!canUpdate"
                      :min="-999"
                      :max="999"
                      controls-position="right"
                      @change="() => persistRow(row)"
                    />
                  </template>
                </el-table-column>
                <el-table-column label="启用" width="90">
                  <template #default="{ row }">
                    <el-switch
                      v-model="row.enabled"
                      :disabled="!canUpdate"
                      @change="() => toggleEnabled(row)"
                    />
                  </template>
                </el-table-column>
                <el-table-column
                  v-if="hasOperationActions"
                  label="操作"
                  width="150"
                  fixed="right"
                >
                  <template #default="{ row }">
                    <el-button
                      v-if="canUpdate"
                      type="primary"
                      link
                      @click="openEdit(row)"
                    >
                      编辑
                    </el-button>
                    <el-button
                      v-if="canDelete"
                      type="danger"
                      link
                      @click="remove(row)"
                    >
                      删除
                    </el-button>
                  </template>
                </el-table-column>
              </el-table>
            </div>

            <div class="rule-pagination">
              <el-pagination
                v-model:current-page="pageState[target.value].page"
                v-model:page-size="pageState[target.value].pageSize"
                :page-sizes="pageSizes"
                :total="filteredRulesByTarget[target.value].length"
                background
                layout="total, sizes, prev, pager, next, jumper"
                @size-change="() => resetPage(target.value)"
              />
            </div>
          </div>
        </el-tab-pane>
      </el-tabs>
    </el-card>

    <el-dialog
      v-model="dialogVisible"
      :title="dialogTitle"
      width="min(480px, calc(100vw - 32px))"
    >
      <el-form
        ref="formRef"
        :model="form"
        :rules="formRules"
        label-width="90px"
        status-icon
      >
        <el-form-item label="目标字段" prop="targetField">
          <el-select
            v-model="form.targetField"
            popper-class="config-select-popper"
          >
            <el-option
              v-for="target in targetOptions"
              :key="target.value"
              :label="target.label"
              :value="target.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="匹配模式">
          <el-select
            v-model="form.matchMode"
            popper-class="config-select-popper"
          >
            <el-option
              v-for="mode in matchModeOptions"
              :key="mode.value"
              :label="mode.label"
              :value="mode.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="匹配词" prop="pattern">
          <el-input v-model="form.pattern" placeholder="例如：项目管理" />
        </el-form-item>
        <el-form-item label="优先级">
          <el-input-number v-model="form.priority" :min="-999" :max="999" />
        </el-form-item>
        <el-form-item label="来源">
          <el-select v-model="form.source" popper-class="config-select-popper">
            <el-option
              v-for="source in sourceOptions"
              :key="source.value"
              :label="source.label"
              :value="source.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="客户域">
          <el-select
            v-model="form.customerId"
            filterable
            clearable
            :loading="customerLoading"
            placeholder="全局（所有客户）"
            popper-class="config-select-popper"
          >
            <el-option
              v-if="form.customerId && !customerNameById.has(form.customerId)"
              :label="formatCustomerScope(form.customerId)"
              :value="form.customerId"
              disabled
            />
            <el-option
              v-for="customer in customers"
              :key="customer.id"
              :label="formatCustomerScope(customer.id)"
              :value="customer.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.enabled" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button v-if="canSubmit" type="primary" @click="submit">
          保存
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.column-rules {
  min-height: 0;
  overflow: hidden;
}

.column-rules-card {
  min-height: 0;
}

.column-rules-card :deep(.el-card__body) {
  overflow: hidden;
}

.column-rules-tabs {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.column-rules-tabs :deep(.el-tabs__content) {
  flex: 1;
  min-height: 0;
}

.column-rules-tabs :deep(.el-tab-pane) {
  height: 100%;
}

.rule-pane {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}

.rule-toolbar {
  flex-shrink: 0;
  margin-bottom: 12px;
}

.rule-search-input {
  width: 100%;
  max-width: 360px;
}

.rule-table-wrap {
  min-height: 160px;
}

.table-select {
  display: block;
  width: 100%;
}

:deep(.table-select .el-select__wrapper) {
  min-width: 0;
}

.table-number-input {
  width: 100%;
}

.rule-pagination {
  display: flex;
  flex-shrink: 0;
  justify-content: flex-end;
  padding-top: 12px;
  overflow-x: auto;
}
</style>
