<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  createSmartStructureRoutingRule,
  deleteSmartStructureRoutingRule,
  getSmartStructureRoutingRules,
  updateSmartStructureRoutingRule,
  type SmartStructureRoutingMatchMode,
  type SmartStructureRoutingMatchScope,
  type SmartStructureRoutingRecommendation,
  type SmartStructureRoutingRule,
  type SmartStructureRoutingRuleSource
} from "@/api/smart-structure-routing-rules";
import { hasPerms } from "@/utils/auth";
import { getRequestErrorMessage } from "@/utils/error-message";
import { ensurePermission } from "@/utils/permission-guard";

defineOptions({
  name: "SmartStructureRoutingRules"
});

const loading = ref(false);
const rules = ref<SmartStructureRoutingRule[]>([]);
const keyword = ref("");

const matchScopeOptions = [
  { label: "任意位置", value: "Any" },
  { label: "表名", value: "TableName" },
  { label: "表头", value: "Headers" },
  { label: "样例行", value: "SampleRows" }
] as const;

const matchModeOptions = [
  { label: "包含", value: "Contains" },
  { label: "相等", value: "Equals" },
  { label: "正则", value: "Regex" }
] as const;

const recommendationOptions = [
  { label: "推荐导入", value: "Recommended", type: "success" },
  { label: "需要确认", value: "NeedConfirm", type: "warning" },
  { label: "可选", value: "Optional", type: "info" },
  { label: "建议跳过", value: "Skip", type: "danger" }
] as const;

const tableKindOptions = [
  "AcceptanceSpec",
  "SafetySpec",
  "EnvironmentalSpec",
  "SecsSpec",
  "Utility",
  "Quotation",
  "Layout",
  "BomOrSpareParts",
  "SignatureOrCover",
  "Unknown"
];

const sourceOptions = [
  { label: "内置种子", value: "BuiltinSeed", type: "info" },
  { label: "手动", value: "Manual", type: "primary" },
  { label: "学习", value: "Learned", type: "success" },
  { label: "AI 建议", value: "AiSuggested", type: "warning" }
] as const;

const canCreate = computed(() =>
  hasPerms("btn:smart-structure-routing-rule:create")
);
const canUpdate = computed(() =>
  hasPerms("btn:smart-structure-routing-rule:update")
);
const canDelete = computed(() =>
  hasPerms("btn:smart-structure-routing-rule:delete")
);
const canSubmit = computed(() =>
  isEdit.value ? canUpdate.value : canCreate.value
);
const hasOperationActions = computed(() => canUpdate.value || canDelete.value);

const getMatchScopeLabel = (value: SmartStructureRoutingMatchScope) =>
  matchScopeOptions.find(option => option.value === value)?.label ?? value;

const getMatchModeLabel = (value: SmartStructureRoutingMatchMode) =>
  matchModeOptions.find(option => option.value === value)?.label ?? value;

const getRecommendationOption = (
  value?: SmartStructureRoutingRecommendation
) =>
  recommendationOptions.find(option => option.value === value) ??
  recommendationOptions[1];

const getSourceOption = (value?: SmartStructureRoutingRuleSource) =>
  sourceOptions.find(option => option.value === value) ?? sourceOptions[1];

const filteredRules = computed(() => {
  const normalizedKeyword = keyword.value.trim().toLowerCase();
  return rules.value
    .filter(rule => {
      if (!normalizedKeyword) {
        return true;
      }

      return [
        rule.name,
        rule.tableKind,
        rule.recommendation,
        rule.pattern,
        getMatchScopeLabel(rule.matchScope),
        getMatchModeLabel(rule.matchMode),
        getSourceOption(rule.source).label,
        rule.customerId ? `客户 ${rule.customerId}` : "全局"
      ]
        .join(" ")
        .toLowerCase()
        .includes(normalizedKeyword);
    })
    .sort(
      (a, b) =>
        (b.priority ?? 0) - (a.priority ?? 0) ||
        (b.weight ?? 0) - (a.weight ?? 0) ||
        a.id - b.id
    );
});

const load = async () => {
  loading.value = true;
  try {
    const res = await getSmartStructureRoutingRules();
    if (res.code === 0) {
      rules.value = res.data || [];
    } else {
      ElMessage.error(res.message || "加载规则失败");
    }
  } catch (error: unknown) {
    ElMessage.error(getRequestErrorMessage(error, "加载规则失败"));
  } finally {
    loading.value = false;
  }
};

const dialogVisible = ref(false);
const dialogTitle = ref("新增规则");
const isEdit = ref(false);

const form = reactive({
  id: 0,
  name: "",
  tableKind: "AcceptanceSpec",
  recommendation: "NeedConfirm" as SmartStructureRoutingRecommendation,
  matchScope: "TableName" as SmartStructureRoutingMatchScope,
  matchMode: "Contains" as SmartStructureRoutingMatchMode,
  pattern: "",
  weight: 1,
  priority: 0,
  enabled: true,
  source: "Manual" as SmartStructureRoutingRuleSource,
  customerId: undefined as number | undefined
});

const resetForm = () => {
  form.id = 0;
  form.name = "";
  form.tableKind = "AcceptanceSpec";
  form.recommendation = "NeedConfirm";
  form.matchScope = "TableName";
  form.matchMode = "Contains";
  form.pattern = "";
  form.weight = 1;
  form.priority = 0;
  form.enabled = true;
  form.source = "Manual";
  form.customerId = undefined;
};

const openAdd = () => {
  if (
    !ensurePermission(
      "btn:smart-structure-routing-rule:create",
      "权限不足，无法新增智能结构路由规则"
    )
  ) {
    return;
  }

  isEdit.value = false;
  dialogTitle.value = "新增规则";
  resetForm();
  dialogVisible.value = true;
};

const openEdit = (row: SmartStructureRoutingRule) => {
  if (
    !ensurePermission(
      "btn:smart-structure-routing-rule:update",
      "权限不足，无法编辑智能结构路由规则"
    )
  ) {
    return;
  }

  isEdit.value = true;
  dialogTitle.value = "编辑规则";
  form.id = row.id;
  form.name = row.name;
  form.tableKind = row.tableKind;
  form.recommendation = row.recommendation;
  form.matchScope = row.matchScope;
  form.matchMode = row.matchMode;
  form.pattern = row.pattern;
  form.weight = row.weight ?? 1;
  form.priority = row.priority ?? 0;
  form.enabled = row.enabled;
  form.source = row.source ?? "Manual";
  form.customerId = row.customerId ?? undefined;
  dialogVisible.value = true;
};

const buildPayload = () => {
  const name = form.name.trim();
  const tableKind = form.tableKind.trim();
  const pattern = form.pattern.trim();
  if (!name || !tableKind || !pattern) {
    return null;
  }

  return {
    name,
    tableKind,
    recommendation: form.recommendation,
    matchScope: form.matchScope,
    matchMode: form.matchMode,
    pattern,
    weight: Number(form.weight) || 1,
    priority: Number(form.priority) || 0,
    enabled: form.enabled,
    source: form.source,
    customerId: form.customerId
  };
};

const submit = async () => {
  if (
    !ensurePermission(
      isEdit.value
        ? "btn:smart-structure-routing-rule:update"
        : "btn:smart-structure-routing-rule:create",
      isEdit.value ? "权限不足，无法保存规则" : "权限不足，无法新增规则"
    )
  ) {
    return;
  }

  const payload = buildPayload();
  if (!payload) {
    ElMessage.warning("请填写规则名称、表格类型和匹配词");
    return;
  }

  try {
    const res = isEdit.value
      ? await updateSmartStructureRoutingRule(form.id, payload)
      : await createSmartStructureRoutingRule(payload);

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

const persistRow = async (row: SmartStructureRoutingRule) => {
  if (
    !ensurePermission(
      "btn:smart-structure-routing-rule:update",
      "权限不足，无法更新智能结构路由规则"
    )
  ) {
    await load();
    return;
  }

  try {
    const res = await updateSmartStructureRoutingRule(row.id, {
      name: row.name,
      tableKind: row.tableKind,
      recommendation: row.recommendation,
      matchScope: row.matchScope,
      matchMode: row.matchMode,
      pattern: row.pattern,
      weight: row.weight ?? 1,
      priority: row.priority ?? 0,
      enabled: row.enabled,
      source: row.source ?? "Manual",
      customerId: row.customerId ?? undefined
    });
    if (res.code !== 0) {
      ElMessage.error(res.message || "更新失败");
      await load();
    }
  } catch (error: unknown) {
    ElMessage.error(getRequestErrorMessage(error, "更新失败"));
    await load();
  }
};

const remove = async (row: SmartStructureRoutingRule) => {
  if (
    !ensurePermission(
      "btn:smart-structure-routing-rule:delete",
      "权限不足，无法删除智能结构路由规则"
    )
  ) {
    return;
  }

  try {
    await ElMessageBox.confirm(`确定删除规则 "${row.name}" 吗？`, "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });
    const res = await deleteSmartStructureRoutingRule(row.id);
    if (res.code === 0) {
      ElMessage.success("删除成功");
      await load();
    } else {
      ElMessage.error(res.message || "删除失败");
    }
  } catch {
    // 用户取消
  }
};

onMounted(load);
</script>

<template>
  <div class="page routing-rules config-page">
    <div class="page-header">
      <div>
        <div class="page-title">智能结构路由规则</div>
      </div>
    </div>

    <el-card>
      <template #header>
        <div class="flex justify-between items-center">
          <el-button v-if="canCreate" type="primary" @click="openAdd">
            新增规则
          </el-button>
        </div>
      </template>

      <div class="rule-toolbar">
        <el-input
          v-model="keyword"
          class="rule-search-input"
          placeholder="搜索名称 / 类型 / 推荐结果 / 匹配词 / 客户"
          clearable
        />
      </div>

      <el-table v-loading="loading" :data="filteredRules" stripe border>
        <el-table-column prop="name" label="规则名称" min-width="180" />
        <el-table-column prop="tableKind" label="表格类型" min-width="150" />
        <el-table-column label="推荐结果" width="120">
          <template #default="{ row }">
            <el-tag
              :type="getRecommendationOption(row.recommendation).type"
              effect="plain"
              size="small"
            >
              {{ getRecommendationOption(row.recommendation).label }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="匹配范围" width="110">
          <template #default="{ row }">
            {{ getMatchScopeLabel(row.matchScope) }}
          </template>
        </el-table-column>
        <el-table-column label="匹配方式" width="110">
          <template #default="{ row }">
            {{ getMatchModeLabel(row.matchMode) }}
          </template>
        </el-table-column>
        <el-table-column prop="pattern" label="匹配词" min-width="180" />
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
        <el-table-column label="客户域" width="120">
          <template #default="{ row }">
            <el-tag
              :type="row.customerId ? 'success' : 'info'"
              effect="plain"
              size="small"
            >
              {{ row.customerId ? `客户 ${row.customerId}` : "全局" }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="权重" width="130">
          <template #default="{ row }">
            <el-input-number
              v-model="row.weight"
              class="table-number-input"
              size="small"
              :disabled="!canUpdate"
              :min="0"
              :max="10"
              :step="0.1"
              controls-position="right"
              @change="() => persistRow(row)"
            />
          </template>
        </el-table-column>
        <el-table-column label="优先级" width="130">
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
              @change="() => persistRow(row)"
            />
          </template>
        </el-table-column>
        <el-table-column
          v-if="hasOperationActions"
          label="操作"
          width="140"
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
    </el-card>

    <el-dialog
      v-model="dialogVisible"
      :title="dialogTitle"
      width="min(560px, calc(100vw - 32px))"
    >
      <el-form label-width="96px">
        <el-form-item label="规则名称" required>
          <el-input v-model="form.name" placeholder="例如：报价单跳过" />
        </el-form-item>
        <el-form-item label="表格类型" required>
          <el-select
            v-model="form.tableKind"
            filterable
            allow-create
            default-first-option
            popper-class="config-select-popper"
          >
            <el-option
              v-for="kind in tableKindOptions"
              :key="kind"
              :label="kind"
              :value="kind"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="推荐结果">
          <el-select
            v-model="form.recommendation"
            filterable
            allow-create
            default-first-option
            popper-class="config-select-popper"
          >
            <el-option
              v-for="option in recommendationOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="匹配范围">
          <el-select
            v-model="form.matchScope"
            popper-class="config-select-popper"
          >
            <el-option
              v-for="option in matchScopeOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="匹配方式">
          <el-select
            v-model="form.matchMode"
            popper-class="config-select-popper"
          >
            <el-option
              v-for="option in matchModeOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="匹配词" required>
          <el-input v-model="form.pattern" placeholder="例如：報價 / Layout" />
        </el-form-item>
        <el-form-item label="权重">
          <el-input-number
            v-model="form.weight"
            :min="0"
            :max="10"
            :step="0.1"
          />
        </el-form-item>
        <el-form-item label="优先级">
          <el-input-number v-model="form.priority" :min="-999" :max="999" />
        </el-form-item>
        <el-form-item label="来源">
          <el-select v-model="form.source" popper-class="config-select-popper">
            <el-option
              v-for="option in sourceOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="客户域">
          <el-input-number
            v-model="form.customerId"
            :min="1"
            :controls="false"
            placeholder="留空为全局规则"
            clearable
          />
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
.rule-toolbar {
  margin-bottom: 16px;
}

.rule-search-input {
  width: 100%;
  max-width: 420px;
}

.table-number-input {
  width: 100%;
}
</style>
