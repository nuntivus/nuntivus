import { Badge, Button, Card, Flex, Heading, Switch, Text, TextArea } from '@radix-ui/themes'
import { useId, useState, type ReactNode } from 'react'
import { FieldErrorText } from '../../components/forms/form-feedback'
import { formStateToBackendConfig, parseRawSection, type AdvancedOptionsDraft, type OptionKey, type ValidationErrors } from './contract-options'

export function ConfigSection({ title, summary, enabled, onToggle, sectionKey, value, onChange, eventTypes, errors, disabled, children, onRawEditingChange }: {
  title: string
  summary: string
  enabled: boolean
  onToggle: (enabled: boolean) => void
  sectionKey: OptionKey
  value: AdvancedOptionsDraft
  onChange: (next: AdvancedOptionsDraft) => void
  eventTypes: string[]
  errors: ValidationErrors
  disabled?: boolean
  children: ReactNode
  onRawEditingChange?: (key: OptionKey, editing: boolean) => void
}) {
  const [open, setOpen] = useState(() => Object.keys(errors).some((path) => path === sectionKey || path.startsWith(`${sectionKey}.`)))
  const [raw, setRaw] = useState<string | null>(null)
  const [rawError, setRawError] = useState<string | null>(null)
  const id = useId()
  const sectionErrors = Object.keys(errors).some((path) => path === sectionKey || path.startsWith(`${sectionKey}.`))
  const enterRaw = () => {
    if (sectionErrors) return
    setRaw(JSON.stringify(formStateToBackendConfig(value)[sectionKey] ?? (sectionKey === 'schemaTargets' ? [] : {}), null, 2))
    setRawError(null)
    onRawEditingChange?.(sectionKey, true)
  }
  const applyRaw = () => {
    const result = parseRawSection(sectionKey, raw ?? '', value, eventTypes)
    if (!result.state) { setRawError(result.error ?? 'Invalid configuration.'); return }
    onChange(result.state)
    setRaw(null)
    setRawError(null)
    onRawEditingChange?.(sectionKey, false)
  }
  return (
    <Card>
      <Flex direction="column" gap="3">
        <Flex align="center" justify="between" gap="2" wrap="wrap">
          <Button type="button" variant="ghost" color="gray" aria-expanded={open} aria-controls={id} disabled={raw !== null} onClick={() => setOpen(!open)}>
            <Heading size="3">{open ? '▾' : '▸'} {title}</Heading>
          </Button>
          <Flex align="center" gap="3">
            <Badge color={sectionErrors ? 'red' : 'gray'}>{sectionErrors ? 'Needs attention' : summary}</Badge>
            <label htmlFor={`${id}-enabled`}><Text size="2">Enabled</Text></label>
            <Switch id={`${id}-enabled`} checked={enabled} disabled={disabled || raw !== null} onCheckedChange={(checked) => { onToggle(checked); if (checked) setOpen(true) }} aria-label={`Enable ${title}`} />
          </Flex>
        </Flex>
        {open ? <Flex id={id} direction="column" gap="3">
          {enabled ? raw === null ? children : <Flex direction="column" gap="2">
            <Text size="2" color="gray">Edit this section as JSON. Apply validates it before updating the visual form.</Text>
            <TextArea aria-label={`${title} JSON`} value={raw} onChange={(event) => { setRaw(event.target.value); setRawError(null) }} rows={9} disabled={disabled} style={{ fontFamily: 'monospace' }} aria-invalid={!!rawError} aria-describedby={rawError ? `${id}-json-error` : undefined} />
            <FieldErrorText id={`${id}-json-error`} message={rawError} />
            <Flex gap="2" wrap="wrap">
              <Button type="button" variant="soft" onClick={() => { try { setRaw(JSON.stringify(JSON.parse(raw), null, 2)); setRawError(null) } catch { setRawError('Invalid JSON syntax.') } }} disabled={disabled}>Format JSON</Button>
              <Button type="button" onClick={applyRaw} disabled={disabled}>Apply to visual editor</Button>
              <Button type="button" variant="ghost" onClick={() => { setRaw(null); setRawError(null); onRawEditingChange?.(sectionKey, false) }}>Cancel JSON changes</Button>
            </Flex>
          </Flex> : <Text size="2" color="gray">Enable to configure {title.toLowerCase()}.</Text>}
          {enabled && raw === null ? <Button type="button" size="1" variant="ghost" onClick={enterRaw} disabled={disabled || sectionErrors} style={{ alignSelf: 'flex-start' }}>{'{ }'} Edit JSON</Button> : null}
        </Flex> : null}
      </Flex>
    </Card>
  )
}
