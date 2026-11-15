export interface AuditPreviewField {
  label: string
  value: string
}

function normalize(value: unknown, depth: number): unknown {
  if (depth >= 8) return value
  if (typeof value === 'string') {
    const text = value.trim()
    if (text.startsWith('{') || text.startsWith('[')) {
      try { return normalize(JSON.parse(text), depth + 1) } catch { return value }
    }
    return value
  }
  if (Array.isArray(value)) return value.map(item => normalize(item, depth + 1))
  if (value && typeof value === 'object') {
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, normalize(item, depth + 1)]))
  }
  return value
}

export function parseAuditDetails(details: string): unknown {
  try { return normalize(JSON.parse(details), 0) } catch { return { raw: details } }
}

function fieldLabel(key: string) {
  const label = key.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_-]+/g, ' ')
    .replace(/\b(id|url)\b/gi, value => value.toUpperCase())
  return label.charAt(0).toUpperCase() + label.slice(1)
}

export function auditPreviewFields(value: unknown): AuditPreviewField[] {
  const fields: AuditPreviewField[] = []
  function collect(current: unknown, depth: number) {
    if (depth > 3 || !current || typeof current !== 'object' || Array.isArray(current)) return
    const entries = Object.entries(current)
    const priority = ['reason', 'operation', 'message']
    const ordered = [
      ...entries.filter(([key]) => priority.includes(key.toLowerCase())),
      ...entries.filter(([key]) => !priority.includes(key.toLowerCase())),
    ]
    for (const [key, item] of ordered) {
      if (fields.length >= 4) return
      if (item === null || item === undefined || item === '') continue
      if (Array.isArray(item)) {
        fields.push({ label: fieldLabel(key), value: `${item.length} item${item.length === 1 ? '' : 's'}` })
      } else if (typeof item === 'object') {
        collect(item, depth + 1)
      } else {
        const text = String(item)
        fields.push({ label: fieldLabel(key), value: text.length > 120 ? `${text.slice(0, 120)}…` : text })
      }
    }
  }
  if (Array.isArray(value)) return [{ label: 'Items', value: String(value.length) }]
  if (value === null || typeof value !== 'object') return [{ label: 'Value', value: String(value) }]
  collect(value, 0)
  return fields
}
