import type {
  LocationQueryRaw,
  RouteLocationRaw,
  RouteParamsRaw,
  RouteRecordName
} from "vue-router";

export type cacheType = {
  mode: string;
  name?: RouteRecordName;
};

export type positionType = {
  startIndex?: number;
  length?: number;
};

export type appType = {
  sidebar: {
    opened: boolean;
    withoutAnimation: boolean;
    // 判断是否手动点击Collapse
    isClickCollapse: boolean;
  };
  layout: string;
  device: string;
  viewportSize: { width: number; height: number };
};

export type multiType = {
  path: string;
  name?: RouteRecordName;
  meta: any;
  query?: LocationQueryRaw;
  params?: RouteParamsRaw;
  children?: multiType[];
  parentId?: number | string;
};

export type setType = {
  title: string;
  fixedHeader: boolean;
  hiddenSideBar: boolean;
};

export type userType = {
  avatar: string;
  username: string;
  nickname: string;
  roleCode: string;
  permissions: Array<string>;
  isRemembered: boolean;
  loginDay: number;
};

export type LoginRequestPayload = {
  username: string;
  password: string;
};

export type LoginRouteTarget = RouteLocationRaw;
