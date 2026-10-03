const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function deliveryIdError(value: string, label: string): string | null {
  return value.trim() && !guidPattern.test(value.trim()) ? `${label} must be a valid GUID.` : null
}
