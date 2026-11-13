#!/bin/sh
set -eu

: "${NUNTIVUS_API_BASE_URL:?Set NUNTIVUS_API_BASE_URL to the browser-reachable API origin}"

if ! jq -en --arg url "$NUNTIVUS_API_BASE_URL" '$url | test("^https?://(\\[[0-9A-Fa-f:]+\\]|[A-Za-z0-9.-]+)(:[0-9]+)?/?$")' > /dev/null; then
	echo "NUNTIVUS_API_BASE_URL must be an HTTP(S) origin without credentials, a path, query, or fragment." >&2
	exit 1
fi

umask 022
{
	printf 'window.__NUNTIVUS_CONFIG__ = '
	jq -cn \
		--arg apiBaseUrl "$NUNTIVUS_API_BASE_URL" \
		--arg runtimeEnvironment "${NUNTIVUS_APP_ENV:-production}" \
		--arg featureFlags "${NUNTIVUS_FEATURE_FLAGS:-}" \
		'{apiBaseUrl: $apiBaseUrl, runtimeEnvironment: $runtimeEnvironment, featureFlags: $featureFlags}'
	printf ';\n'
} > /tmp/runtime-config.js

exec "$@"
