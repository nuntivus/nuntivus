import { IconButton, Text, TextField } from '@radix-ui/themes'
import { useId, useState, type ComponentType, type SVGProps } from 'react'
import { SearchIcon } from '../icons/nav-icons'
import './integration-type-picker.css'

export interface IntegrationTypeOption<T extends string = string> {
  value: T
  label: string
  description: string
  icon: ComponentType<SVGProps<SVGSVGElement>>
  color: string
}

interface IntegrationTypePickerProps<T extends string> {
  label: string
  options: IntegrationTypeOption<T>[]
  value: T
  onChange: (value: T) => void
  disabled?: boolean
}

export function IntegrationTypePicker<T extends string>({
  label,
  options,
  value,
  onChange,
  disabled = false,
}: IntegrationTypePickerProps<T>) {
  const id = useId()
  const [search, setSearch] = useState('')
  const query = search.trim().toLowerCase()
  const filtered = options.filter(option =>
    `${option.label} ${option.value} ${option.description}`.toLowerCase().includes(query))
  const selected = options.find(option => option.value === value)

  return <fieldset className="integration-type-picker" disabled={disabled}>
    <legend className="integration-type-legend">{label}</legend>
    <Text as="p" size="2" color="gray" className="integration-type-help">
      Choose an integration to configure. Selected: <strong>{selected?.label ?? value}</strong>
    </Text>
    <TextField.Root
      type="search"
      aria-label={`Search ${label.toLowerCase()}s`}
      placeholder="Search integrations..."
      value={search}
      disabled={disabled}
      onChange={event => setSearch(event.target.value)}
      onKeyDown={event => { if (event.key === 'Enter') event.preventDefault() }}
    >
      <TextField.Slot><SearchIcon /></TextField.Slot>
      {search && <TextField.Slot side="right">
        <IconButton type="button" size="1" variant="ghost" color="gray" aria-label="Clear type search"
          disabled={disabled} onClick={() => setSearch('')}>
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
            <path d="m6 6 12 12M18 6 6 18" />
          </svg>
        </IconButton>
      </TextField.Slot>}
    </TextField.Root>
    <div role="radiogroup" aria-label={label} className="integration-type-grid">
      {filtered.map(option => {
        const Icon = option.icon
        return <label key={option.value} className="integration-type-card">
          <input type="radio" name={id} value={option.value}
            checked={value === option.value} disabled={disabled}
            aria-labelledby={`${id}-${option.value}-label`}
            aria-describedby={`${id}-${option.value}-description`}
            onChange={() => onChange(option.value)} />
          <span className="integration-type-surface">
            <span className="integration-type-icon" style={{ color: option.color }}><Icon /></span>
            <span className="integration-type-copy">
              <span id={`${id}-${option.value}-label`} className="integration-type-name">{option.label}</span>
              <span id={`${id}-${option.value}-description`} className="integration-type-description">{option.description}</span>
            </span>
            <span className="integration-type-check" aria-hidden="true">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                <path d="m5 12 4 4L19 6" />
              </svg>
            </span>
          </span>
        </label>
      })}
    </div>
    <Text as="p" size="1" color="gray" role="status">
      {filtered.length ? `${filtered.length} integration${filtered.length === 1 ? '' : 's'} available`
        : 'No integrations match your search. Try another name or clear the search.'}
    </Text>
  </fieldset>
}
