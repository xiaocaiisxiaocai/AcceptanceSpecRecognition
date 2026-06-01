import { defineStore } from "pinia";
import { type setType, store, getConfig } from "../utils";

export const useSettingStore = defineStore("pure-setting", {
  state: (): setType => ({
    title: getConfig().Title ?? "",
    fixedHeader: getConfig().FixedHeader ?? true,
    hiddenSideBar: getConfig().HiddenSideBar ?? false
  }),
  getters: {
    getTitle(state) {
      return state.title;
    },
    getFixedHeader(state) {
      return state.fixedHeader;
    },
    getHiddenSideBar(state) {
      return state.hiddenSideBar;
    }
  },
  actions: {
    CHANGE_SETTING<T extends keyof setType>({
      key,
      value
    }: {
      key: T;
      value: setType[T];
    }) {
      if (Reflect.has(this, key)) {
        (this.$state as setType)[key] = value;
      }
    },
    changeSetting<T extends keyof setType>(data: {
      key: T;
      value: setType[T];
    }) {
      this.CHANGE_SETTING(data);
    }
  }
});

export function useSettingStoreHook() {
  return useSettingStore(store);
}
