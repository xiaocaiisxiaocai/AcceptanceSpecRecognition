<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";
import { ElTree, type FormInstance, type FormRules } from "element-plus";
import type { AuthPermission } from "@/api/auth-permission";
import type {
  RoleFormModel,
  RoleFormOption,
  ScopeType
} from "../roleForm.types";
import {
  normalizeScopeOrgUnitIds,
  validateScopeOrgUnitIds
} from "../roleScope";
import { isProtectedBuiltInRole } from "../roleProtection";

const props = defineProps<{
  visible: boolean;
  mode: "create" | "edit";
  modelValue: RoleFormModel;
  permissions: AuthPermission[];
  scopeTypeOptions: RoleFormOption<ScopeType>[];
  orgUnitOptions: RoleFormOption[];
  submitting: boolean;
}>();

const emit = defineEmits<{
  (event: "update:visible", value: boolean): void;
  (event: "update:modelValue", value: RoleFormModel): void;
  (event: "submit"): void;
}>();

const formRef = ref<FormInstance>();
const treeRef = ref<InstanceType<typeof ElTree>>();
const permissionTypeLabels: Record<number, string> = {
  3: "菜单",
  0: "页面",
  1: "按钮",
  2: "接口"
};
const permissionTypeOrder = [3, 0, 1, 2];

const title = computed(() =>
  props.mode === "create" ? "创建角色" : "编辑角色"
);
const readOnly = computed(
  () => props.mode === "edit" && isProtectedBuiltInRole(props.modelValue)
);
const permissionCodes = computed(
  () => new Set(props.permissions.map(item => item.code))
);
const treeData = computed(() =>
  permissionTypeOrder
    .map(type => ({
      id: `group:${type}`,
      label: permissionTypeLabels[type],
      disabled: readOnly.value,
      children: props.permissions
        .filter(item => item.permissionType === type)
        .sort((a, b) => a.code.localeCompare(b.code))
        .map(item => ({
          id: item.code,
          label: item.name,
          code: item.code,
          disabled: readOnly.value
        }))
    }))
    .filter(group => group.children.length > 0)
);

const updateForm = (patch: Partial<RoleFormModel>) => {
  emit("update:modelValue", { ...props.modelValue, ...patch });
};

const syncCheckedKeys = () => {
  void nextTick(() =>
    treeRef.value?.setCheckedKeys(props.modelValue.permissionCodes)
  );
};

watch(
  () => [props.visible, props.permissions, props.modelValue.permissionCodes],
  syncCheckedKeys,
  { deep: true }
);

const handlePermissionCheck = (
  _data: unknown,
  state: { checkedKeys: Array<string | number> }
) => {
  if (readOnly.value) {
    syncCheckedKeys();
    return;
  }

  updateForm({
    permissionCodes: state.checkedKeys
      .map(String)
      .filter(code => permissionCodes.value.has(code))
  });
};

const selectAll = () => {
  updateForm({ permissionCodes: props.permissions.map(item => item.code) });
};

const clearAll = () => {
  updateForm({ permissionCodes: [] });
};

const setExpanded = (expanded: boolean) => {
  treeData.value.forEach(group => {
    const node = treeRef.value?.getNode(group.id);
    if (node) node.expanded = expanded;
  });
};

const needsSingleOrg = (scopeType: ScopeType) =>
  scopeType === 1 || scopeType === 2;
const needsOrgSelection = (scopeType: ScopeType) =>
  needsSingleOrg(scopeType) || scopeType === 3;

const formRules: FormRules<RoleFormModel> = {
  code: [
    {
      validator: (_rule, value: string, callback) => {
        if (props.mode !== "create") return callback();
        const code = value.trim().toLowerCase();
        if (!code) return callback(new Error("请输入角色编码"));
        if (!/^[a-z0-9._-]{2,64}$/.test(code)) {
          return callback(
            new Error("仅支持小写字母、数字、点、下划线、中划线，长度 2-64")
          );
        }
        callback();
      },
      trigger: "blur"
    }
  ],
  name: [{ required: true, message: "请输入角色名称", trigger: "blur" }],
  scopeOrgUnitIds: [
    {
      validator: (_rule, value: number[], callback) => {
        const message = validateScopeOrgUnitIds(
          props.modelValue.scopeType,
          value
        );
        if (message) return callback(new Error(message));
        callback();
      },
      trigger: "change"
    }
  ]
};

const handleScopeTypeChange = (scopeType: ScopeType) => {
  updateForm({
    scopeType,
    scopeOrgUnitIds: normalizeScopeOrgUnitIds(
      scopeType,
      props.modelValue.scopeOrgUnitIds
    )
  });
  void nextTick(() => formRef.value?.validateField("scopeOrgUnitIds"));
};

const handleSubmit = async () => {
  if (readOnly.value) return;
  const valid = await formRef.value?.validate().catch(() => false);
  if (valid) emit("submit");
};

const handleOpened = () => {
  syncCheckedKeys();
  formRef.value?.clearValidate();
};
</script>

<template>
  <el-dialog
    :model-value="visible"
    :title="title"
    width="min(860px, calc(100vw - 32px))"
    destroy-on-close
    @update:model-value="value => emit('update:visible', value)"
    @opened="handleOpened"
  >
    <el-alert
      v-if="readOnly"
      title="内置管理员角色受系统保护，不允许修改。"
      type="info"
      :closable="false"
      show-icon
      class="readonly-alert"
    />
    <el-form
      ref="formRef"
      :model="modelValue"
      :rules="formRules"
      label-width="110px"
      status-icon
    >
      <el-form-item label="角色编码" prop="code">
        <el-input
          :model-value="modelValue.code"
          :disabled="mode === 'edit'"
          maxlength="64"
          placeholder="例如 quality-reviewer"
          @update:model-value="code => updateForm({ code })"
        />
      </el-form-item>
      <el-form-item label="角色名称" prop="name">
        <el-input
          :model-value="modelValue.name"
          :disabled="readOnly"
          maxlength="100"
          @update:model-value="name => updateForm({ name })"
        />
      </el-form-item>
      <el-form-item label="角色描述">
        <el-input
          :model-value="modelValue.description"
          :disabled="readOnly"
          type="textarea"
          :rows="3"
          maxlength="500"
          show-word-limit
          @update:model-value="description => updateForm({ description })"
        />
      </el-form-item>
      <el-form-item label="状态">
        <el-switch
          :model-value="modelValue.isActive"
          :disabled="readOnly"
          @update:model-value="
            isActive => updateForm({ isActive: Boolean(isActive) })
          "
        />
      </el-form-item>
      <el-form-item>
        <template #label>
          权限
          <span class="permission-count"
            >({{ modelValue.permissionCodes.length }})</span
          >
        </template>
        <div class="permission-panel">
          <div class="permission-actions">
            <el-button text :disabled="readOnly" @click="selectAll"
              >全选</el-button
            >
            <el-button text :disabled="readOnly" @click="clearAll"
              >清空</el-button
            >
            <el-button text @click="setExpanded(true)">展开</el-button>
            <el-button text @click="setExpanded(false)">折叠</el-button>
          </div>
          <el-tree
            ref="treeRef"
            class="permission-tree"
            :data="treeData"
            node-key="id"
            show-checkbox
            default-expand-all
            :expand-on-click-node="false"
            @check="handlePermissionCheck"
          >
            <template #default="{ data }">
              <span class="permission-node">
                <span>{{ data.label }}</span>
                <code v-if="data.code">{{ data.code }}</code>
              </span>
            </template>
          </el-tree>
        </div>
      </el-form-item>
      <el-form-item label="验收规格范围" prop="scopeType" required>
        <el-select
          :model-value="modelValue.scopeType"
          :disabled="readOnly"
          class="dialog-select dialog-select--320"
          @update:model-value="handleScopeTypeChange"
        >
          <el-option
            v-for="option in scopeTypeOptions"
            :key="option.value"
            :label="option.label"
            :value="option.value"
          />
        </el-select>
      </el-form-item>
      <el-form-item
        v-if="needsOrgSelection(modelValue.scopeType)"
        label="组织节点"
        prop="scopeOrgUnitIds"
      >
        <el-select
          v-if="needsSingleOrg(modelValue.scopeType)"
          :model-value="modelValue.scopeOrgUnitIds[0] ?? null"
          clearable
          filterable
          :disabled="readOnly"
          class="dialog-select dialog-select--320"
          @update:model-value="
            value => updateForm({ scopeOrgUnitIds: value ? [value] : [] })
          "
        >
          <el-option
            v-for="option in orgUnitOptions"
            :key="option.value"
            :label="option.label"
            :value="option.value"
            :disabled="option.disabled"
          />
        </el-select>
        <el-select
          v-else
          :model-value="modelValue.scopeOrgUnitIds"
          multiple
          collapse-tags
          collapse-tags-tooltip
          filterable
          :disabled="readOnly"
          class="dialog-select dialog-select--320"
          placeholder="请选择一个或多个组织节点"
          @update:model-value="
            value => updateForm({ scopeOrgUnitIds: value as number[] })
          "
        >
          <el-option
            v-for="option in orgUnitOptions"
            :key="option.value"
            :label="option.label"
            :value="option.value"
            :disabled="option.disabled"
          />
        </el-select>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="emit('update:visible', false)">取消</el-button>
      <el-button
        type="primary"
        :loading="submitting"
        :disabled="readOnly"
        @click="handleSubmit"
      >
        {{ readOnly ? "不可保存" : mode === "create" ? "创建" : "保存" }}
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.readonly-alert {
  margin-bottom: 16px;
}

.permission-count {
  margin-left: 4px;
  color: var(--app-text-secondary);
}

.permission-panel {
  width: 100%;
  overflow: hidden;
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.permission-actions {
  display: flex;
  flex-wrap: wrap;
  padding: 4px 8px;
  background: var(--app-fill-light);
  border-bottom: 1px solid var(--app-border);
}

.permission-tree {
  max-height: 340px;
  padding: 8px 10px;
  overflow: auto;
}

.permission-node {
  display: flex;
  gap: 10px;
  align-items: center;
  min-width: 0;
}

.permission-node code {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 11px;
  color: var(--app-text-secondary);
  white-space: nowrap;
}
</style>
