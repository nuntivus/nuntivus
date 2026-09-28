import { useQueries } from '@tanstack/react-query'
import { getSchemaVersion, listSchemaVersions } from '../../services/api/event-definitions-api'

export type SchemaFieldOption = {
    path: string
    type: string
    scope: string
    common: boolean
}

function asObject(value: unknown): Record<string, unknown> | null {
    return value !== null && typeof value === 'object' && !Array.isArray(value)
        ? value as Record<string, unknown>
        : null
}

function resolveLocalReference(root: Record<string, unknown>, reference: string) {
    if (!reference.startsWith('#/')) return null

    return reference
        .slice(2)
        .split('/')
        .map((part) => part.replace(/~1/g, '/').replace(/~0/g, '~'))
        .reduce<unknown>((current, part) => asObject(current)?.[part], root)
}

function describeSchemaType(schema: Record<string, unknown>) {
    const type = Array.isArray(schema.type)
        ? schema.type.filter((item): item is string => typeof item === 'string').join(' | ')
        : typeof schema.type === 'string' ? schema.type : 'value'
    return typeof schema.format === 'string' ? `${type} (${schema.format})` : type
}

function describeJsonValue(value: unknown) {
    if (value === null) return 'null'
    if (Array.isArray(value)) return 'array'
    if (typeof value === 'string' && ['string', 'number', 'integer', 'boolean', 'object', 'array', 'null'].includes(value.toLowerCase())) {
        return value.toLowerCase()
    }
    return typeof value
}

function extractSchemaFields(schemaText: string) {
    const fields = new Map<string, string>()

    try {
        const root = asObject(JSON.parse(schemaText))
        if (!root) return fields

        const visit = (value: unknown, path: string, references: Set<string>) => {
            const schema = asObject(value)
            if (!schema) return

            if (typeof schema.$ref === 'string' && !references.has(schema.$ref)) {
                const resolved = resolveLocalReference(root, schema.$ref)
                if (resolved) visit(resolved, path, new Set(references).add(schema.$ref))
            }

            for (const keyword of ['allOf', 'anyOf', 'oneOf']) {
                if (Array.isArray(schema[keyword])) {
                    schema[keyword].forEach((entry) => visit(entry, path, references))
                }
            }

            const properties = asObject(schema.properties)
            if (properties) {
                Object.entries(properties).forEach(([key, child]) => {
                    visit(child, path ? `${path}.${key}` : key, references)
                })
                return
            }

            if (path) fields.set(path, describeSchemaType(schema))

            if (schema.items) visit(schema.items, path, references)
        }

        visit(root, '', new Set())

        if (fields.size === 0 && Array.isArray(root.fields)) {
            const visitAvroFields = (entries: unknown[], parentPath: string) => {
                entries.forEach((entry) => {
                    const field = asObject(entry)
                    if (!field || typeof field.name !== 'string') return
                    const path = parentPath ? `${parentPath}.${field.name}` : field.name
                    const fieldType = asObject(field.type)
                    if (fieldType && Array.isArray(fieldType.fields)) {
                        visitAvroFields(fieldType.fields, path)
                        return
                    }
                    const type = Array.isArray(field.type)
                        ? field.type.map((item) => typeof item === 'string' ? item : asObject(item)?.type).filter(Boolean).join(' | ')
                        : typeof field.type === 'string' ? field.type : asObject(field.type)?.type
                    fields.set(path, typeof type === 'string' ? type : 'value')
                })
            }
            visitAvroFields(root.fields, '')
        }

        if (fields.size === 0) {
            const visitJsonDocument = (value: unknown, path: string) => {
                if (Array.isArray(value)) {
                    if (value.length > 0) visitJsonDocument(value[0], path)
                    else if (path) fields.set(path, 'array')
                    return
                }

                const object = asObject(value)
                if (object) {
                    const entries = Object.entries(object)
                    if (entries.length === 0 && path) fields.set(path, 'object')
                    entries.forEach(([key, child]) => visitJsonDocument(child, path ? `${path}.${key}` : key))
                    return
                }

                if (path) fields.set(path, describeJsonValue(value))
            }
            visitJsonDocument(root, '')
        }
    } catch {
        return fields
    }

    return fields
}

export function useSchemaFieldOptions(eventTypes: string[]) {
    const versionQueries = useQueries({
        queries: eventTypes.map((eventType) => ({
            queryKey: ['event-definitions', 'detail', eventType, 'schemas', 'payload-fields'],
            queryFn: ({ signal }: { signal: AbortSignal }) =>
                listSchemaVersions(eventType, 1, 100, { signal }),
            enabled: eventType.length > 0,
        })),
    })
    const selectedVersions = versionQueries.map((query) => {
        const items = query.data?.items ?? []
        const active = items.find((item) => {
            const state = item.state as unknown
            return String(state).toLowerCase() === 'active' || state === 1
        })
        return active?.version ?? [...items].sort((left, right) => right.version - left.version)[0]?.version
    })
    const schemaQueries = useQueries({
        queries: eventTypes.map((eventType, index) => ({
            queryKey: ['event-definitions', 'detail', eventType, 'schema', selectedVersions[index]],
            queryFn: ({ signal }: { signal: AbortSignal }) =>
                getSchemaVersion(eventType, selectedVersions[index] ?? 0, { signal }),
            enabled: eventType.length > 0 && typeof selectedVersions[index] === 'number',
        })),
    })

    const fields = new Map<string, { types: Set<string>; eventTypes: Set<string> }>()
    let loadedSchemaCount = 0
    schemaQueries.forEach((query, index) => {
        const schema = query.data
        if (!schema?.schema) return
        loadedSchemaCount += 1
        extractSchemaFields(schema.schema).forEach((type, path) => {
            const field = fields.get(path) ?? { types: new Set<string>(), eventTypes: new Set<string>() }
            field.types.add(type)
            field.eventTypes.add(eventTypes[index])
            fields.set(path, field)
        })
    })

    const options: SchemaFieldOption[] = [...fields.entries()]
        .map(([path, field]) => {
            const common = loadedSchemaCount > 1 && field.eventTypes.size === loadedSchemaCount
            const scope = common ? 'all targeted schemas' : [...field.eventTypes].join(', ')
            return { path, type: [...field.types].join(' | '), scope, common }
        })
        .sort((left, right) => Number(right.common) - Number(left.common) || left.path.localeCompare(right.path))

    return {
        options,
        hasEventTypes: eventTypes.length > 0,
        isLoading: versionQueries.some((query) => query.isLoading) || schemaQueries.some((query) => query.isLoading),
        isError: versionQueries.some((query) => query.isError) || schemaQueries.some((query) => query.isError),
    }
}