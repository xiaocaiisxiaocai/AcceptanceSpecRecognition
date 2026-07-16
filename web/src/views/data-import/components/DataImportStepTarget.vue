<script setup lang="ts">
import { computed } from "vue";
import type { FormRules } from "element-plus";
import type { Customer } from "@/api/customer";
import type { MachineModel } from "@/api/machine-model";
import type { Process } from "@/api/process";

const props = defineProps<{
  customers: Customer[];
  processes: Process[];
  machineModels: MachineModel[];
  selectedCustomerId: number | undefined;
  selectedProcessId: number | undefined;
  selectedMachineModelId: number | undefined;
  loadingCustomers: boolean;
  loadingProcesses: boolean;
  loadingMachineModels: boolean;
  compact?: boolean;
}>();

const formModel = computed(() => ({
  customerId: props.selectedCustomerId,
  processId: props.selectedProcessId,
  machineModelId: props.selectedMachineModelId
}));
const formRules: FormRules = {
  customerId: [
    { required: true, message: "请选择客户", trigger: ["blur", "change"] }
  ]
};

const emit = defineEmits<{
  (e: "update:selectedCustomerId", value: number | undefined): void;
  (e: "update:selectedProcessId", value: number | undefined): void;
  (e: "update:selectedMachineModelId", value: number | undefined): void;
}>();
</script>

<template>
  <div :class="['step-panel', { 'step-panel--compact': compact }]">
    <el-form
      :model="formModel"
      :rules="formRules"
      label-width="100px"
      class="target-form"
      status-icon
    >
      <el-form-item label="选择客户" prop="customerId">
        <el-select
          :model-value="selectedCustomerId"
          placeholder="请选择客户"
          :loading="loadingCustomers"
          filterable
          class="dialog-select dialog-select--320"
          popper-class="app-select-popper"
          @update:model-value="
            value => emit('update:selectedCustomerId', value)
          "
        >
          <el-option
            v-for="customer in customers"
            :key="customer.id"
            :label="customer.name"
            :value="customer.id"
          />
        </el-select>
      </el-form-item>

      <el-form-item label="选择制程">
        <el-select
          :model-value="selectedProcessId"
          placeholder="请选择制程（可选）"
          :loading="loadingProcesses"
          filterable
          class="dialog-select dialog-select--320"
          popper-class="app-select-popper"
          @update:model-value="value => emit('update:selectedProcessId', value)"
        >
          <el-option
            v-for="process in processes"
            :key="process.id"
            :label="process.name"
            :value="process.id"
          />
        </el-select>
      </el-form-item>
      <el-form-item label="选择机型">
        <el-select
          :model-value="selectedMachineModelId"
          placeholder="请选择机型（可选）"
          :loading="loadingMachineModels"
          filterable
          class="dialog-select dialog-select--320"
          popper-class="app-select-popper"
          @update:model-value="
            value => emit('update:selectedMachineModelId', value)
          "
        >
          <el-option
            v-for="model in machineModels"
            :key="model.id"
            :label="model.name"
            :value="model.id"
          />
        </el-select>
      </el-form-item>
    </el-form>
  </div>
</template>
