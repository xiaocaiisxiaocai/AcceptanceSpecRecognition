<script setup lang="ts">
import { computed } from "vue";
import type { MatchPreviewItem } from "@/api/matching";
import type { MatchPreviewEditForm } from "./matchPreviewTable.types";

const props = defineProps<{
  item: MatchPreviewItem | null;
  form: MatchPreviewEditForm;
}>();

const visible = defineModel<boolean>("visible", { required: true });

defineEmits<{
  (e: "closed"): void;
  (e: "save"): void;
}>();

// 父组件以引用方式传入编辑表单，子组件通过该代理直接编辑其字段；
// mutation 经同一引用回传父组件，行为与直接改 prop 一致，同时规避 vue/no-mutating-props。
const editForm = computed(() => props.form);

const selectExistingValue = (event: FocusEvent) => {
  const target = event.target;
  if (
    target instanceof HTMLInputElement ||
    target instanceof HTMLTextAreaElement
  ) {
    target.select();
  }
};
</script>

<template>
  <el-dialog
    v-model="visible"
    title="编辑本次导出内容"
    width="640px"
    @closed="$emit('closed')"
  >
    <div v-if="item" class="edit-dialog">
      <div class="edit-dialog__hint">
        输入新内容会完整替换原值，不会与旧值叠加；修改仅本次导出使用，执行填充前可选择是否回填到验收规格。
      </div>
      <el-form label-position="top">
        <el-form-item label="项目">
          <el-input :model-value="item.sourceProject" readonly />
        </el-form-item>
        <el-form-item label="规格">
          <el-input
            :model-value="item.sourceSpecification"
            readonly
            type="textarea"
            :rows="2"
          />
        </el-form-item>
        <el-form-item label="验收标准">
          <el-input
            v-model="editForm.overrideAcceptance"
            type="textarea"
            :rows="3"
            placeholder="请输入本次导出的验收标准"
            @focus="selectExistingValue"
          />
        </el-form-item>
        <el-form-item label="备注">
          <el-input
            v-model="editForm.overrideRemark"
            type="textarea"
            :rows="3"
            placeholder="请输入本次导出的备注"
            @focus="selectExistingValue"
          />
        </el-form-item>
      </el-form>
    </div>
    <template #footer>
      <div class="edit-dialog__footer">
        <el-button @click="visible = false">取消</el-button>
        <el-button type="primary" @click="$emit('save')">
          保存并采用
        </el-button>
      </div>
    </template>
  </el-dialog>
</template>
