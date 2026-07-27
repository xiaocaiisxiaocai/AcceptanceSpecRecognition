import { reactive } from "vue";
import type { FormRules } from "element-plus";

/** 密码规则：4～200 位，不限制字符组合 */
export const REGEXP_PWD = /^.{4,200}$/;

/** 登录校验 */
const loginRules = reactive<FormRules>({
  password: [
    {
      validator: (rule, value, callback) => {
        if (value === "") {
          callback(new Error("请输入密码"));
        } else if (!REGEXP_PWD.test(value)) {
          callback(new Error("密码长度必须在4到200位之间"));
        } else {
          callback();
        }
      },
      trigger: "blur"
    }
  ]
});

export { loginRules };
