import type { FormInstance, FormItemRule } from "element-plus";

export const requiredTrimmedRule = (message: string): FormItemRule => ({
  validator: (_rule, value: unknown, callback) => {
    if (typeof value === "string" && value.trim()) {
      callback();
      return;
    }
    callback(new Error(message));
  },
  trigger: ["blur", "change"]
});

export const requiredSelectionRule = (message: string): FormItemRule => ({
  required: true,
  message,
  trigger: "change"
});

export const validateForm = async (form: FormInstance | undefined) =>
  Boolean(await form?.validate().catch(() => false));
