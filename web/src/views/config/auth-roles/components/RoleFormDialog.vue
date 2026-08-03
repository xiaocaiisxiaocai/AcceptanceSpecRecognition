<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";
import { type FormInstance, type FormRules } from "element-plus";
import type { AuthPermission } from "@/api/auth-permission";
import type {
  RoleFormModel,
  RoleFormOption,
  ScopeType
} from "../roleForm.types";
import {
  normalizeScopeOrgUnitIds,
  supportsDynamicPrimaryOrgSubtree,
  validateScopeOrgUnitIds
} from "../roleScope";
import { isProtectedBuiltInRole } from "../roleProtection";
import {
  buildPermissionEditorView,
  normalizePermissionCodes,
  permissionTypeDefinitions,
  replacePermissionGroupSelection,
  type PermissionResourceGroup,
  type PermissionTypeValue
} from "../permissionEditor";

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
const activePermissionType = ref<PermissionTypeValue>(1);
const permissionKeyword = ref("");
const selectedOnly = ref(false);

const title = computed(() =>
  props.mode === "create" ? "创建角色" : "编辑角色"
);
const readOnly = computed(
  () => props.mode === "edit" && isProtectedBuiltInRole(props.modelValue)
);
const allowDynamicPrimaryOrgSubtree = computed(
  () =>
    props.modelValue.scopeType === 2 &&
    supportsDynamicPrimaryOrgSubtree(props.modelValue)
);
const permissionEditorView = computed(() =>
  buildPermissionEditorView({
    permissions: props.permissions,
    selectedCodes: props.modelValue.permissionCodes,
    activeType: activePermissionType.value,
    keyword: permissionKeyword.value,
    selectedOnly: selectedOnly.value
  })
);
const selectedPermissionCount = computed(
  () => normalizePermissionCodes(props.modelValue.permissionCodes).length
);

const updateForm = (patch: Partial<RoleFormModel>) => {
  emit("update:modelValue", { ...props.modelValue, ...patch });
};

watch(
  () => props.visible,
  visible => {
    if (!visible) return;
    permissionKeyword.value = "";
    selectedOnly.value = false;
    if (
      !props.permissions.some(
        permission => permission.permissionType === activePermissionType.value
      )
    ) {
      activePermissionType.value =
        permissionTypeDefinitions.find(type =>
          props.permissions.some(
            permission => permission.permissionType === type.value
          )
        )?.value ?? 1;
    }
  }
);

const handlePermissionToggle = (code: string, selected: boolean) => {
  if (readOnly.value) return;
  updateForm({
    permissionCodes: replacePermissionGroupSelection(
      props.modelValue.permissionCodes,
      [code],
      selected
    )
  });
};

const handlePermissionGroupSelection = (
  group: PermissionResourceGroup,
  selected: boolean
) => {
  if (readOnly.value) return;
  updateForm({
    permissionCodes: replacePermissionGroupSelection(
      props.modelValue.permissionCodes,
      group.codes,
      selected
    )
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
          value,
          allowDynamicPrimaryOrgSubtree.value
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
  formRef.value?.clearValidate();
};
</script>

<template>
  <el-dialog
    :model-value="visible"
    :title="title"
    class="role-form-dialog"
    width="min(960px, calc(100vw - 32px))"
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
          :rows="2"
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
            >({{ selectedPermissionCount }}/{{ permissions.length }})</span
          >
        </template>
        <div class="permission-panel" data-testid="permission-editor">
          <div
            class="permission-type-tabs"
            role="tablist"
            aria-label="权限类型"
          >
            <button
              v-for="type in permissionEditorView.types"
              :key="type.value"
              type="button"
              role="tab"
              class="permission-type-tab"
              :class="{ 'is-active': activePermissionType === type.value }"
              :aria-selected="activePermissionType === type.value"
              :data-testid="`permission-type-${type.value}`"
              @click="activePermissionType = type.value"
            >
              <span>{{ type.label }}</span>
              <span class="permission-type-tab__count">
                {{ type.selectedCount }}/{{ type.totalCount }}
              </span>
            </button>
          </div>

          <div class="permission-toolbar">
            <el-input
              v-model="permissionKeyword"
              clearable
              class="permission-search"
              placeholder="搜索名称、编码、资源或动作"
              data-testid="permission-search"
            />
            <el-checkbox
              v-model="selectedOnly"
              data-testid="permission-selected-only"
            >
              仅看已选
            </el-checkbox>
          </div>

          <div class="permission-resource-list">
            <el-empty
              v-if="permissionEditorView.groups.length === 0"
              description="没有匹配的权限"
              :image-size="64"
            />
            <section
              v-for="group in permissionEditorView.groups"
              :key="group.resource"
              class="permission-resource-group"
              :data-testid="`permission-resource-${group.resource}`"
            >
              <header class="permission-resource-group__header">
                <div class="permission-resource-group__title">
                  <strong>{{ group.label }}</strong>
                  <code>{{ group.resource }}</code>
                  <span>{{ group.selectedCount }}/{{ group.totalCount }}</span>
                </div>
                <div class="permission-resource-group__actions">
                  <el-button
                    text
                    size="small"
                    :disabled="
                      readOnly || group.selectedCount === group.totalCount
                    "
                    @click="handlePermissionGroupSelection(group, true)"
                  >
                    全选
                  </el-button>
                  <el-button
                    text
                    size="small"
                    :disabled="readOnly || group.selectedCount === 0"
                    @click="handlePermissionGroupSelection(group, false)"
                  >
                    清空
                  </el-button>
                </div>
              </header>
              <div class="permission-option-grid">
                <el-checkbox
                  v-for="item in group.items"
                  :key="item.code"
                  :model-value="item.selected"
                  :disabled="readOnly"
                  class="permission-option"
                  :data-permission-code="item.code"
                  @change="
                    value => handlePermissionToggle(item.code, Boolean(value))
                  "
                >
                  <span class="permission-option__content">
                    <span class="permission-option__name">
                      {{ item.primaryLabel }}
                    </span>
                    <code>{{ item.secondaryLabel }}</code>
                  </span>
                </el-checkbox>
              </div>
            </section>
          </div>
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
        <div
          v-if="needsSingleOrg(modelValue.scopeType)"
          class="scope-org-editor"
        >
          <el-select
            :model-value="modelValue.scopeOrgUnitIds[0] ?? null"
            clearable
            filterable
            :disabled="readOnly"
            class="dialog-select dialog-select--320"
            :placeholder="
              allowDynamicPrimaryOrgSubtree
                ? '留空时按用户主组织及子树生效'
                : '请选择组织节点'
            "
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
          <span v-if="allowDynamicPrimaryOrgSubtree" class="scope-org-hint">
            留空表示按每个被分配用户的主组织及其子树动态确定范围
          </span>
        </div>
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
:global(.role-form-dialog) {
  display: flex;
  flex-direction: column;
  max-height: calc(100vh - 32px);
  margin-top: 16px;
  margin-bottom: 16px;
}

:global(.role-form-dialog .el-dialog__header),
:global(.role-form-dialog .el-dialog__footer) {
  flex: none;
}

:global(.role-form-dialog .el-dialog__body) {
  min-height: 0;
  padding: 10px 16px 0;
  overflow-y: auto;
}

:global(.role-form-dialog .el-dialog__header) {
  padding: 12px 16px 8px;
}

:global(.role-form-dialog .el-dialog__footer) {
  padding: 8px 16px 12px;
}

:global(.role-form-dialog .el-form-item) {
  margin-bottom: 12px;
}

.readonly-alert {
  margin-bottom: 12px;
}

.scope-org-editor {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.scope-org-hint {
  font-size: 12px;
  line-height: 1.5;
  color: var(--app-text-secondary);
}

.permission-count {
  margin-left: 4px;
  color: var(--app-text-secondary);
}

.permission-panel {
  width: 100%;
  overflow: hidden;
  background: var(--el-bg-color);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.permission-type-tabs {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 4px;
  padding: 4px;
  background: var(--app-fill-light);
  border-bottom: 1px solid var(--app-border);
}

.permission-type-tab {
  display: flex;
  gap: 6px;
  align-items: center;
  justify-content: center;
  min-width: 0;
  min-height: 30px;
  padding: 4px 10px;
  color: var(--app-text-secondary);
  cursor: pointer;
  background: transparent;
  border: 1px solid transparent;
  border-radius: 6px;
  transition:
    color 0.16s ease,
    background-color 0.16s ease,
    border-color 0.16s ease;
}

.permission-type-tab:hover {
  color: var(--el-color-primary);
  background: var(--el-color-primary-light-9);
}

.permission-type-tab.is-active {
  font-weight: 600;
  color: var(--el-color-primary);
  background: var(--el-bg-color);
  border-color: var(--el-color-primary-light-5);
}

.permission-type-tab__count {
  font-size: 12px;
  font-variant-numeric: tabular-nums;
  color: var(--app-text-secondary);
}

.permission-toolbar {
  display: flex;
  gap: 16px;
  align-items: center;
  justify-content: space-between;
  padding: 6px 8px;
  border-bottom: 1px solid var(--app-border);
}

.permission-search {
  width: min(420px, 100%);
}

.permission-resource-list {
  max-height: 300px;
  padding: 6px;
  overflow: auto;
  background: var(--app-fill-light);
}

.permission-resource-group {
  overflow: hidden;
  background: var(--el-bg-color);
  border: 1px solid var(--app-border);
  border-radius: 7px;
}

.permission-resource-group + .permission-resource-group {
  margin-top: 6px;
}

.permission-resource-group__header {
  display: flex;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
  min-height: 34px;
  padding: 3px 8px 3px 10px;
  background: var(--app-fill-light);
  border-bottom: 1px solid var(--app-border);
}

.permission-resource-group__title {
  display: flex;
  gap: 8px;
  align-items: baseline;
  min-width: 0;
}

.permission-resource-group__title strong {
  flex: none;
  color: var(--app-text-primary);
}

.permission-resource-group__title code,
.permission-resource-group__title span {
  font-size: 11px;
  color: var(--app-text-secondary);
}

.permission-resource-group__actions {
  display: flex;
  flex: none;
}

.permission-resource-group__actions :deep(.el-button + .el-button) {
  margin-left: 2px;
}

.permission-option-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 8px;
  padding: 2px 8px 4px;
}

.permission-option {
  width: 100%;
  height: auto;
  min-height: 40px;
  padding: 4px;
  margin-right: 0;
  white-space: normal;
}

.permission-option :deep(.el-checkbox__label) {
  min-width: 0;
  padding-left: 8px;
}

.permission-option__content {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  line-height: 1.35;
}

.permission-option__name {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 13px;
  font-weight: 500;
  color: var(--app-text-primary);
  white-space: nowrap;
}

.permission-option code {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 11px;
  color: var(--app-text-secondary);
  white-space: nowrap;
}

@media (width <= 720px) {
  .permission-type-tabs {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .permission-toolbar {
    flex-direction: column;
    gap: 8px;
    align-items: flex-start;
  }

  .permission-search,
  .permission-option-grid {
    width: 100%;
  }

  .permission-option-grid {
    grid-template-columns: 1fr;
  }
}
</style>
