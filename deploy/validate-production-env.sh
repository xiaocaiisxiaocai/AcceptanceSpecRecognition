#!/bin/sh
set -eu

env_file="${1:-.env}"
if [ ! -f "$env_file" ]; then
  echo "ERROR: 环境变量文件不存在: $env_file" >&2
  exit 1
fi

read_env_value() {
  key="$1"
  awk -v expected_key="$key" '
    index($0, expected_key "=") == 1 {
      count++
      value = substr($0, length(expected_key) + 2)
    }
    END {
      if (count != 1) {
        exit 2
      }
      printf "%s", value
    }
  ' "$env_file"
}

is_placeholder() {
  normalized_value=$(printf "%s" "$1" | tr '[:upper:]' '[:lower:]')
  case "$normalized_value" in
    replace_with_*|changethis*|*change_me*|__required*|your_*|devonly_*|example_*|sample_*|password|secret)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

validation_failed=0
for requirement in \
  "MYSQL_ROOT_PASSWORD:1" \
  "MYSQL_PASSWORD:1" \
  "JWT_SIGNING_KEY:32" \
  "AUTH_SEED_ADMIN_PASSWORD:4" \
  "AUTH_SEED_COMMON_PASSWORD:4"
do
  key=${requirement%%:*}
  minimum_length=${requirement##*:}

  if ! value=$(read_env_value "$key"); then
    echo "ERROR: $key 必须且只能配置一次" >&2
    validation_failed=1
    continue
  fi

  if [ -z "$value" ] || is_placeholder "$value"; then
    echo "ERROR: $key 未配置或仍为已知占位符" >&2
    validation_failed=1
    continue
  fi

  if [ "${#value}" -lt "$minimum_length" ]; then
    echo "ERROR: $key 长度不满足最低要求" >&2
    validation_failed=1
  fi

  case "$key" in
    AUTH_SEED_ADMIN_PASSWORD|AUTH_SEED_COMMON_PASSWORD)
      if [ "${#value}" -gt 200 ]; then
        echo "ERROR: $key 长度不能超过 200 位" >&2
        validation_failed=1
      fi
      ;;
  esac
done

unset value normalized_value

validate_image_reference() {
  image_reference="$1"
  image_tail=${image_reference##*/}

  if [ -z "$image_reference" ] || [ -z "$image_tail" ]; then
    return 1
  fi

  case "$image_reference" in
    *@sha256:*)
      image_digest=${image_reference##*@sha256:}
      case "$image_digest" in
        ""|*[!0-9A-Fa-f]*) return 1 ;;
      esac
      [ "${#image_digest}" -eq 64 ] || return 1
      return 0
      ;;
  esac

  case "$image_tail" in
    *:*) image_tag=${image_tail##*:} ;;
    *) return 1 ;;
  esac

  if [ -z "$image_tag" ] || is_placeholder "$image_tag"; then
    return 1
  fi

  normalized_image_tag=$(printf "%s" "$image_tag" | tr '[:upper:]' '[:lower:]')
  [ "$normalized_image_tag" != "latest" ]
}

for image_key in API_IMAGE WEB_IMAGE
do
  if ! image_reference=$(read_env_value "$image_key"); then
    echo "ERROR: $image_key 必须且只能配置一次" >&2
    validation_failed=1
    continue
  fi

  if ! validate_image_reference "$image_reference"; then
    echo "ERROR: $image_key 必须使用非 latest 的明确版本标签或 sha256 digest" >&2
    validation_failed=1
  fi
done

unset image_key image_reference image_tail image_tag image_digest normalized_image_tag normalized_value

read_auth_value() {
  auth_key="$1"
  if ! auth_value=$(read_env_value "$auth_key"); then
    echo "ERROR: $auth_key 必须且只能配置一次" >&2
    validation_failed=1
    return 1
  fi
  return 0
}

if read_auth_value "CORS_ALLOWED_ORIGIN"; then
  cors_allowed_origin=$auth_value
else
  cors_allowed_origin=""
fi
if read_auth_value "BROWSER_AUTH_ALLOW_INSECURE_HTTP"; then
  allow_insecure_http=$auth_value
else
  allow_insecure_http=""
fi
if read_auth_value "BROWSER_AUTH_REFRESH_COOKIE_NAME"; then
  refresh_cookie_name=$auth_value
else
  refresh_cookie_name=""
fi
if read_auth_value "BROWSER_AUTH_COOKIE_SECURE"; then
  cookie_secure=$auth_value
else
  cookie_secure=""
fi
if read_auth_value "BROWSER_AUTH_COOKIE_SAME_SITE"; then
  cookie_same_site=$auth_value
else
  cookie_same_site=""
fi
if read_auth_value "BROWSER_AUTH_COOKIE_DOMAIN"; then
  cookie_domain=$auth_value
else
  cookie_domain=""
fi

case "$allow_insecure_http" in
  true|false) ;;
  *)
    echo "ERROR: BROWSER_AUTH_ALLOW_INSECURE_HTTP 必须为 true 或 false" >&2
    validation_failed=1
    ;;
esac

case "$cookie_secure" in
  true|false) ;;
  *)
    echo "ERROR: BROWSER_AUTH_COOKIE_SECURE 必须为 true 或 false" >&2
    validation_failed=1
    ;;
esac

validate_origin_authority() {
  candidate="$1"
  case "$candidate" in
    http://*) authority=${candidate#http://} ;;
    https://*) authority=${candidate#https://} ;;
    *) return 1 ;;
  esac

  case "$authority" in
    ""|*'*'*|*/*|*\?*|*\#*|*@*|*[[:space:]]*) return 1 ;;
  esac

  origin_host=""
  origin_port=""
  case "$authority" in
    \[*\]*)
      origin_host=${authority%%]*}
      origin_host=${origin_host#\[}
      remainder=${authority#*]}
      case "$remainder" in
        "") ;;
        :*) origin_port=${remainder#:}; [ -n "$origin_port" ] || return 1 ;;
        *) return 1 ;;
      esac
      case "$origin_host" in
        ""|*[!0-9A-Fa-f:.%_-]*) return 1 ;;
      esac
      ;;
    *:*)
      origin_host=${authority%:*}
      origin_port=${authority##*:}
      [ -n "$origin_port" ] || return 1
      case "$origin_host" in *:*) return 1 ;; esac
      ;;
    *) origin_host=$authority ;;
  esac

  case "$origin_host" in
    ""|.*|*..*|*.|*[!A-Za-z0-9._-]*) return 1 ;;
  esac

  if [ -n "$origin_port" ]; then
    case "$origin_port" in *[!0-9]*|"") return 1 ;; esac
    [ "${#origin_port}" -le 5 ] || return 1
    if [ "$origin_port" -lt 1 ] || [ "$origin_port" -gt 65535 ]; then
      return 1
    fi
  fi

  return 0
}

if ! validate_origin_authority "$cors_allowed_origin"; then
  echo "ERROR: CORS_ALLOWED_ORIGIN 必须是包含合法主机和端口的精确 HTTP(S) Origin" >&2
  validation_failed=1
fi

if [ "$cookie_same_site" != "Strict" ]; then
  echo "ERROR: BROWSER_AUTH_COOKIE_SAME_SITE 必须为 Strict" >&2
  validation_failed=1
fi

if [ -n "$cookie_domain" ]; then
  echo "ERROR: BROWSER_AUTH_COOKIE_DOMAIN 必须留空以保持 host-only Cookie" >&2
  validation_failed=1
fi

if [ "$allow_insecure_http" = "false" ]; then
  if [ "$cookie_secure" != "true" ]; then
    echo "ERROR: HTTPS 模式必须启用 BROWSER_AUTH_COOKIE_SECURE" >&2
    validation_failed=1
  fi
  case "$cors_allowed_origin" in
    https://*) ;;
    *)
      echo "ERROR: 默认安全模式仅允许 HTTPS CORS_ALLOWED_ORIGIN" >&2
      validation_failed=1
      ;;
  esac
  case "$refresh_cookie_name" in
    __Host-?*) ;;
    *)
      echo "ERROR: HTTPS 模式的 BROWSER_AUTH_REFRESH_COOKIE_NAME 必须使用 __Host- 前缀" >&2
      validation_failed=1
      ;;
  esac
elif [ "$allow_insecure_http" = "true" ]; then
  if [ "$cookie_secure" != "false" ]; then
    echo "ERROR: 内网 HTTP 模式必须关闭 BROWSER_AUTH_COOKIE_SECURE" >&2
    validation_failed=1
  fi
  case "$cors_allowed_origin" in
    http://*) ;;
    *)
      echo "ERROR: 内网 HTTP 模式只允许精确 HTTP CORS_ALLOWED_ORIGIN" >&2
      validation_failed=1
      ;;
  esac
  if [ "$refresh_cookie_name" != "acceptance-refresh" ]; then
    echo "ERROR: 内网 HTTP 模式必须使用 BROWSER_AUTH_REFRESH_COOKIE_NAME=acceptance-refresh" >&2
    validation_failed=1
  fi
fi

unset auth_key auth_value cors_allowed_origin allow_insecure_http refresh_cookie_name
unset cookie_secure cookie_same_site cookie_domain candidate authority origin_host origin_port remainder

if [ "$validation_failed" -ne 0 ]; then
  exit 1
fi

echo "生产环境敏感配置校验通过（未回显任何配置值）"
