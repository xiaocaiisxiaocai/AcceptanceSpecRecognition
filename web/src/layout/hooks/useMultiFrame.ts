import type { Component } from "vue";

const MAP = new Map<string, Component>();

export const useMultiFrame = () => {
  function setMap(path: string, Comp: Component) {
    MAP.set(path, Comp);
  }

  function getMap(path?: string) {
    if (path) {
      return MAP.get(path);
    }
    return [...MAP.entries()];
  }

  function delMap(path: string) {
    MAP.delete(path);
  }

  return {
    setMap,
    getMap,
    delMap,
    MAP
  };
};
