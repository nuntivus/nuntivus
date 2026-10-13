function normalizeConfigurationKey(key: string): string {
  const words = key
    .replace(/[_\s-]+/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .trim()
    .split(' ')
    .filter((word) => word.length > 0)

  if (words.length === 0) return key
  return words.map((word, index) => index === 0
    ? word.toLowerCase()
    : word.charAt(0).toUpperCase() + word.slice(1).toLowerCase()).join('')
}

export function normalizeConfiguration(value: unknown, parentKey?: string): unknown {
  if (Array.isArray(value)) return value.map((entry) => normalizeConfiguration(entry))
  if (value !== null && typeof value === 'object') {
    if (parentKey === 'headers' || parentKey === 'secretHeaders' || parentKey === 'protectedHeaders') return value
    return Object.fromEntries(Object.entries(value).map(([key, entry]) => [
      normalizeConfigurationKey(key), normalizeConfiguration(entry, normalizeConfigurationKey(key)),
    ]))
  }
  return value
}
