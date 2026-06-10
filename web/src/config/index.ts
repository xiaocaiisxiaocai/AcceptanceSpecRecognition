import axios from "axios";
import type { App } from "vue";

type ConfigValue =
  | PlatformConfigs
  | PlatformConfigs[keyof PlatformConfigs]
  | null;

let config: PlatformConfigs = {};
const { VITE_PUBLIC_PATH } = import.meta.env;

const setConfig = (cfg?: PlatformConfigs) => {
  config = Object.assign(config, cfg);
};

function getConfig(): PlatformConfigs;
function getConfig(key: string): ConfigValue;
function getConfig(key?: string): ConfigValue {
  if (typeof key === "string") {
    const arr = key.split(".");
    if (arr && arr.length) {
      let data: unknown = config;
      arr.forEach(v => {
        if (
          data &&
          typeof data === "object" &&
          v in data &&
          typeof (data as Record<string, unknown>)[v] !== "undefined"
        ) {
          data = (data as Record<string, unknown>)[v];
        } else {
          data = null;
        }
      });
      return data as ConfigValue;
    }
  }
  return config;
}

/** 获取项目动态全局配置 */
export const getPlatformConfig = async (app: App): Promise<PlatformConfigs> => {
  app.config.globalProperties.$config = getConfig() as PlatformConfigs;
  return axios({
    method: "get",
    url: `${VITE_PUBLIC_PATH}platform-config.json`
  })
    .then(({ data: config }) => {
      let $config = app.config.globalProperties.$config;
      // 自动注入系统配置
      if (app && $config && typeof config === "object") {
        $config = Object.assign($config, config);
        app.config.globalProperties.$config = $config;
        // 设置全局配置
        setConfig($config);
      }
      return $config;
    })
    .catch(() => {
      throw "请在public文件夹下添加platform-config.json配置文件";
    });
};

/** 本地响应式存储的命名空间 */
const responsiveStorageNameSpace = () => getConfig().ResponsiveStorageNameSpace;

export { getConfig, setConfig, responsiveStorageNameSpace };
