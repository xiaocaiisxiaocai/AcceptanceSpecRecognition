#!/bin/sh
set -eu

registry="${1:-${NPM_REGISTRY:-}}"
newline='
'

case "$registry" in
  ""|*"$newline"*|*[[:space:]]*|*[[:cntrl:]]*)
    echo "ERROR: NPM_REGISTRY 必须是无空白和控制字符的 HTTP(S) URL" >&2
    exit 1
    ;;
  http://*)
    authority=${registry#http://}
    ;;
  https://*)
    authority=${registry#https://}
    ;;
  *)
    echo "ERROR: NPM_REGISTRY 只允许 http:// 或 https:// URL" >&2
    exit 1
    ;;
esac

authority=${authority%%/*}
authority=${authority%%\?*}
authority=${authority%%\#*}
case "$authority" in
  ""|*@*)
    echo "ERROR: NPM_REGISTRY 必须包含主机且不得携带 userinfo 或凭据" >&2
    exit 1
    ;;
esac

exit 0
