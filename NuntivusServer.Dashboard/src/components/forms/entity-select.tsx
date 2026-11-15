import { Button, Flex, Popover, Text, TextField } from '@radix-ui/themes'
import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'
import { hasPermission } from '../../auth/permissions'
import { ChevronRightIcon } from '../icons/nav-icons'
import { getApiErrorMessage } from '../../services/api/api-client'
import { useAppSelector } from '../../store/hooks'
import { entityReadPermissions, useEntityOptions, type SelectableEntityKind } from './entity-select-options'

type EntitySelectProps = {
  kind: SelectableEntityKind
  value: string
  onChange: (value: string) => void
  id?: string
  'aria-label': string
  'aria-invalid'?: boolean
  'aria-describedby'?: string
  placeholder?: string
  disabled?: boolean
  clearable?: boolean
}

export function EntitySelect({ kind, value, onChange, id, 'aria-label': label, 'aria-invalid': invalid,
  'aria-describedby': describedBy, placeholder, disabled, clearable = true }: EntitySelectProps) {
  const generatedId = useId()
  const listId = `${generatedId}-options`
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [activeId, setActiveId] = useState<string | null>(null)
  const input = useRef<HTMLInputElement>(null)
  const user = useAppSelector(state => state.auth.currentUser)
  const canRead = hasPermission(user, entityReadPermissions[kind])
  const query = useEntityOptions(kind, debouncedSearch, value, canRead && !disabled, open)
  const activeIndex = Math.max(0, query.options.findIndex(item => item.id === activeId))

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search.trim()), 250)
    return () => clearTimeout(timer)
  }, [search])

  function changeOpen(next: boolean) {
    setOpen(next)
    if (next) {
      setSearch('')
      setDebouncedSearch('')
      setActiveId(value || null)
    }
  }

  function select(next: string) {
    onChange(next)
    setOpen(false)
  }

  function navigate(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Enter') {
      event.preventDefault()
      if (search.trim() === debouncedSearch && query.options[activeIndex]) select(query.options[activeIndex].id)
    } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      const next = Math.max(0, Math.min(query.options.length - 1, activeIndex + (event.key === 'ArrowDown' ? 1 : -1)))
      setActiveId(query.options[next]?.id ?? null)
      document.getElementById(`${listId}-${next}`)?.scrollIntoView({ block: 'nearest' })
    }
  }

  return <Popover.Root open={open} onOpenChange={changeOpen}>
    <Popover.Trigger>
      <Button id={id} type="button" variant="surface" className="entity-select-trigger" role="combobox"
        aria-label={label} aria-expanded={open} aria-haspopup="listbox" aria-controls={open ? listId : undefined}
        aria-invalid={invalid} aria-describedby={describedBy} disabled={disabled}>
        <span className={value ? 'entity-select-value' : 'entity-select-placeholder'}>{value ? query.selected?.label || value : placeholder ?? label}</span>
        <ChevronRightIcon className="entity-select-chevron" />
      </Button>
    </Popover.Trigger>
    <Popover.Content className="entity-select-popover" onOpenAutoFocus={event => { event.preventDefault(); input.current?.focus() }}>
      <Flex direction="column" gap="2">
        {canRead ? <>
          <TextField.Root ref={input} value={search} onChange={event => { setSearch(event.target.value); setActiveId(null) }}
            placeholder="Search by name, type or ID" aria-label={`Search ${label}`} role="combobox" aria-autocomplete="list"
            aria-expanded="true" aria-controls={listId} aria-activedescendant={query.options.length ? `${listId}-${activeIndex}` : undefined}
            onKeyDown={navigate} />
          <div id={listId} role="listbox" aria-label={label} aria-busy={query.isLoading} className="entity-select-options">
            {query.options.map((item, index) => <button key={item.id} id={`${listId}-${index}`} type="button" role="option"
              aria-selected={item.id === value} tabIndex={-1} className="entity-select-option" data-active={index === activeIndex}
              onMouseMove={() => setActiveId(item.id)} onClick={() => select(item.id)}>
              <span>{item.label}</span>{item.label !== item.id && <span className="entity-select-option-id">{item.id}</span>}
            </button>)}
          </div>
          {query.isLoading && <Text size="2" role="status">Loading options...</Text>}
          {query.error ? <Flex direction="column" gap="2"><Text size="2" color="red" role="alert">{getApiErrorMessage(query.error, 'Unable to load options.')}</Text>
            <Button type="button" size="1" variant="soft" onClick={() => void query.retry()}>Retry</Button></Flex>
            : !query.isLoading && query.options.length === 0 && <Text size="2" color="gray" role="status">No matches found.</Text>}
          {query.hasNextPage && <Button type="button" size="1" variant="soft" disabled={query.isFetchingNextPage}
            onClick={() => void query.fetchNextPage()}>{query.isFetchingNextPage ? 'Loading...' : 'Load more'}</Button>}
        </> : <Text size="2" color="gray">You do not have permission to browse these options.</Text>}
        {clearable && value && <Button type="button" size="1" variant="ghost" onClick={() => select('')}>Clear {label}</Button>}
      </Flex>
    </Popover.Content>
  </Popover.Root>
}
