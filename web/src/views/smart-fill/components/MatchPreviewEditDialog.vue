<script setup lang="ts">
import type { MatchPreviewItem } from "@/api/matching";
import type { MatchPreviewEditForm } from "./matchPreviewTable.types";

defineProps<{
  item: MatchPreviewItem | null;
  form: MatchPreviewEditForm;
}>();

const visible = defineModel<boolean>("visible", { required: true });

defineEmits<{
  (e: "closed"): void;
  (e: "save"): void;
}>();
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
        修改仅本次导出使用，执行填充前可选择是否回填到验收规格。
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
            v-model="form.overrideAcceptance"
            type="textarea"
            :rows="3"
            placeholder="请输入本次导出的验收标准"
          />
        </el-form-item>
        <el-form-item label="备注">
          <el-input
            v-model="form.overrideRemark"
            type="textarea"
            :rows="3"
            placeholder="请输入本次导出的备注"
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
