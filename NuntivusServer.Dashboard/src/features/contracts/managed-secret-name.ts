const secretReferencePattern = /^secret(?:\.[a-zA-Z0-9_-]+)+$/

export function toManagedSecretReference(input: string): string | null {
  const name = input.trim()
  const reference = name.startsWith('secret.') ? name : `secret.${name}`
  return reference.length <= 256 && secretReferencePattern.test(reference) ? reference : null
}
