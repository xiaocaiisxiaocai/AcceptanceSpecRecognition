export type TopMenuRoute = {
  path?: string;
  redirect?: string;
  children?: TopMenuRoute[];
  [key: string]: unknown;
};

export function handleTopMenuCandidate(route?: TopMenuRoute) {
  if (route?.children && route.children.length > 1) {
    if (route.redirect) {
      return route.children.find(cur => cur.path === route.redirect);
    }

    return route.children[0];
  }

  return route;
}

export function resolveTopMenuFromWholeMenus(wholeMenus?: TopMenuRoute[]) {
  return handleTopMenuCandidate(wholeMenus?.[0]?.children?.[0]);
}
