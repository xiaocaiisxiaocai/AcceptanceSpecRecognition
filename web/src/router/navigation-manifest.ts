import manifest from "../../../shared/navigation/navigation-manifest.json";

type NavigationItem = {
  id: string;
  code: string;
  title: string;
  resource: string;
  action: string;
  path: string;
};

type NavigationManifest = {
  menus: NavigationItem[];
  pages: NavigationItem[];
};

const navigationManifest = manifest as NavigationManifest;

function resolveItem(items: NavigationItem[], id: string) {
  const item = items.find(entry => entry.id === id);
  if (!item) {
    throw new Error(`未找到导航清单项: ${id}`);
  }

  return item;
}

export function getMenuPermission(id: string) {
  return [resolveItem(navigationManifest.menus, id).code];
}

export function getMenuTitle(id: string) {
  return resolveItem(navigationManifest.menus, id).title;
}

export function getPagePermission(id: string) {
  return [resolveItem(navigationManifest.pages, id).code];
}

export function getPageTitle(id: string) {
  return resolveItem(navigationManifest.pages, id).title;
}
