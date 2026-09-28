import { Badge, Button, Card, Flex, Select, Table, Text, TextField } from '@radix-ui/themes'
import { useMemo, useRef, useState, type FormEvent } from 'react'
import { FormDialog } from '../components/dialogs/form-dialog'
import { ClockIcon, PencilIcon, SearchIcon, TrashIcon } from '../components/icons/nav-icons'
import { ListFeedback } from '../components/lists/list-feedback'
import { ListPagination } from '../components/lists/list-pagination'
import { TableIconButton } from '../components/tables/table-actions'
import { toManagedSecretReference } from '../features/contracts/managed-secret-name'
import { useCreateSecretMutation, useDeleteSecretMutation, useSecretsQuery, useSecretVersionsQuery, useUpdateSecretMutation } from '../features/contracts/use-secrets'
import { useConfirmDialog } from '../hooks/use-confirm-dialog'
import { useUnsavedChangesGuard } from '../hooks/use-unsaved-changes-guard'
import { useNotifications } from '../hooks/use-notifications'
import { parsePositiveInt, useUrlQueryParams } from '../hooks/use-url-query-params'
import { getApiErrorMessage } from '../services/api/api-client'

export function SecretsPage() {
  const { searchParams, setSearchParams, updateParams } = useUrlQueryParams()
  const secrets = useSecretsQuery()
  const create = useCreateSecretMutation()
  const update = useUpdateSecretMutation()
  const remove = useDeleteSecretMutation()
  const confirm = useConfirmDialog()
  const notifications = useNotifications()
  const defaultPageSize = 20
  const searchParam = searchParams.get('search') ?? ''
  const pageParam = parsePositiveInt(searchParams.get('page'), 1)
  const pageSizeParam = parsePositiveInt(searchParams.get('pageSize'), defaultPageSize)
  const [searchInput, setSearchInput] = useState(searchParam)
  const [name, setName] = useState('')
  const [value, setValue] = useState('')
  const [isCreateOpen, setIsCreateOpen] = useState(false)
  const createButtonRef = useRef<HTMLButtonElement>(null)
  const [selectedName, setSelectedName] = useState<string | null>(null)
  const [editingName, setEditingName] = useState<string | null>(null)
  const [newValue, setNewValue] = useState('')
  const versions = useSecretVersionsQuery(selectedName)
  const reference = toManagedSecretReference(name)
  useUnsavedChangesGuard({ when: !!name || !!value || !!newValue })

  const filteredSecrets = useMemo(() => {
    const normalizedSearch = searchParam.trim().toLowerCase()
    const items = secrets.data ?? []

    if (!normalizedSearch) return items
    return items.filter((secret) => secret.name.toLowerCase().includes(normalizedSearch))
  }, [searchParam, secrets.data])

  const totalPages = Math.max(1, Math.ceil(filteredSecrets.length / pageSizeParam))
  const currentPage = Math.min(pageParam, totalPages)
  const visibleSecrets = filteredSecrets.slice(
    (currentPage - 1) * pageSizeParam,
    currentPage * pageSizeParam,
  )
  const paging = {
    page: currentPage,
    totalPages,
    totalCount: filteredSecrets.length,
    hasPrevious: currentPage > 1,
    hasNext: currentPage < totalPages,
  }
  const hasActiveFilters = searchParam.trim().length > 0

  function handleSearchSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    updateParams({ search: searchInput.trim() || null, page: '1' })
  }

  function resetFilters() {
    setSearchInput('')
    setSearchParams(new URLSearchParams({ page: '1', pageSize: String(defaultPageSize) }))
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (create.isPending || !reference || !value.trim()) return

    try {
      await create.mutateAsync({ name: reference, value })
      setName('')
      setValue('')
      setIsCreateOpen(false)
      notifications.success('Secret stored.', 'Secrets')
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to store secret.'), 'Secrets')
    }
  }

  function handleCreateOpenChange(open: boolean) {
    setIsCreateOpen(open)

    if (!open) {
      setName('')
      setValue('')
      window.setTimeout(() => createButtonRef.current?.focus(), 0)
    }
  }

  async function handleUpdate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!editingName || !newValue.trim() || update.isPending) return

    try {
      const result = await update.mutateAsync({ name: editingName, value: newValue })
      setEditingName(null)
      setNewValue('')
      notifications.success(`Version ${result.currentVersion} is now active for ${result.name}.`, 'Secrets')
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to update secret.'), 'Secrets')
    }
  }

  async function handleDelete(secretName: string) {
    const accepted = await confirm({
      title: 'Delete secret',
      description: `Delete ${secretName} and all its versions? Secrets in use by contracts or unfinished deliveries cannot be deleted.`,
      confirmText: 'Delete secret',
      confirmColor: 'red',
    })
    if (!accepted) return

    try {
      await remove.mutateAsync(secretName)
      if (selectedName === secretName) setSelectedName(null)
      notifications.success('Secret deleted.', 'Secrets')
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to delete secret.'), 'Secrets')
    }
  }

  function handleUpdateOpenChange(open: boolean) {
    if (!open) {
      setEditingName(null)
      setNewValue('')
    }
  }

  const selectedSecret = secrets.data?.find((secret) => secret.name === selectedName)

  return (
    <Flex direction="column" gap="5">
      <div className="page-header">
        <div>
          <h1 className="page-title">Secrets</h1>
          <p className="page-subtitle">Manage encrypted values referenced by contracts and destinations.</p>
        </div>
        <Button ref={createButtonRef} size="2" onClick={() => setIsCreateOpen(true)}>Add secret</Button>
      </div>

      <FormDialog
        open={isCreateOpen}
        onOpenChange={handleCreateOpenChange}
        title="Add secret"
        description="Values are encrypted at rest and cannot be viewed after they are stored."
        descriptionId="create-secret-dialog-description"
        footer={
          <>
            <Button type="button" variant="soft" color="gray" onClick={() => handleCreateOpenChange(false)}>Cancel</Button>
            <Button type="submit" form="create-secret-form" disabled={create.isPending || !reference || !value.trim()}>
              {create.isPending ? 'Storing...' : 'Store secret'}
            </Button>
          </>
        }
      >
        <form id="create-secret-form" onSubmit={handleSubmit}>
          <div className="field">
            <label className="field-label" htmlFor="secret-name">Secret name</label>
            <TextField.Root id="secret-name" placeholder="acme.webhook" value={name} onChange={(event) => setName(event.target.value)} aria-describedby="secret-name-hint" required />
            <Text id="secret-name-hint" size="1" color={name.trim() && !reference ? 'red' : 'gray'}>
              {name.trim() && !reference
                ? 'Use dot-separated letters, numbers, underscores or hyphens (up to 256 characters).'
                : `Stored as ${reference ?? 'secret.acme.webhook'} (the secret. prefix is optional).`}
            </Text>
          </div>
          <div className="field">
            <label className="field-label" htmlFor="secret-value">Secret value</label>
            <TextField.Root id="secret-value" type="password" autoComplete="new-password" value={value} onChange={(event) => setValue(event.target.value)} required />
          </div>
        </form>
      </FormDialog>

      <Card>
        <Flex direction="column" gap="3">
          <Flex gap="3" wrap="wrap" align="end" justify="between">
            <form onSubmit={handleSearchSubmit}>
              <Flex gap="2" align="center">
                <TextField.Root
                  size="2"
                  className="toolbar-search"
                  placeholder="Search secret name"
                  value={searchInput}
                  onChange={(event) => setSearchInput(event.target.value)}
                  aria-label="Search secrets"
                >
                  <TextField.Slot><SearchIcon width={15} height={15} /></TextField.Slot>
                </TextField.Root>
                <Button type="submit" size="2" variant="soft">Search</Button>
              </Flex>
            </form>

            <Flex gap="2" align="center" wrap="wrap">
              <Select.Root
                value={String(pageSizeParam)}
                onValueChange={(next) => updateParams({ pageSize: next, page: '1' })}
              >
                <Select.Trigger aria-label="Secrets per page" />
                <Select.Content>
                  <Select.Item value="10">10</Select.Item>
                  <Select.Item value="20">20</Select.Item>
                  <Select.Item value="50">50</Select.Item>
                  <Select.Item value="100">100</Select.Item>
                </Select.Content>
              </Select.Root>
              <Button type="button" variant="soft" onClick={resetFilters}>Reset filters</Button>
            </Flex>
          </Flex>

          <ListFeedback
            isLoading={secrets.isPending}
            isError={secrets.isError}
            error={secrets.error}
            isEmpty={filteredSecrets.length === 0}
            loadingMessage="Loading secrets..."
            errorMessage="Unable to load secrets."
            emptyMessage={hasActiveFilters ? 'No secrets match the current search.' : 'No secrets stored yet.'}
            emptyTitle={hasActiveFilters ? 'No secrets match current filters' : 'No secrets yet'}
            emptyDescription={hasActiveFilters ? 'Try another secret name or clear the filters.' : 'Add a secret to reference encrypted values from contracts and destinations.'}
            emptyAction={hasActiveFilters
              ? { label: 'Clear filters', onClick: resetFilters }
              : { label: 'Add secret', onClick: () => setIsCreateOpen(true) }}
          >
            <>
              <div className="responsive-table">
                <Table.Root aria-label="Secrets table">
                  <Table.Header>
                    <Table.Row>
                      <Table.ColumnHeaderCell>Name</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Current version</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell>Created</Table.ColumnHeaderCell>
                      <Table.ColumnHeaderCell className="table-action-column">Actions</Table.ColumnHeaderCell>
                    </Table.Row>
                  </Table.Header>
                  <Table.Body>
                    {visibleSecrets.map((secret) => (
                      <Table.Row key={secret.name}>
                        <Table.RowHeaderCell><Text weight="medium" size="2">{secret.name}</Text></Table.RowHeaderCell>
                        <Table.Cell>{secret.currentVersion}</Table.Cell>
                        <Table.Cell className="table-nowrap">{new Date(secret.createdAt).toLocaleString()}</Table.Cell>
                        <Table.Cell className="table-action-column">
                          <div className="table-row-actions">
                            <TableIconButton label={`View versions of ${secret.name}`} onClick={() => setSelectedName(secret.name)}>
                              <ClockIcon />
                            </TableIconButton>
                            <TableIconButton label={`Update ${secret.name}`} onClick={() => setEditingName(secret.name)}>
                              <PencilIcon />
                            </TableIconButton>
                            <TableIconButton label={`Delete ${secret.name}`} color="red" disabled={remove.isPending} onClick={() => void handleDelete(secret.name)}>
                              <TrashIcon />
                            </TableIconButton>
                          </div>
                        </Table.Cell>
                      </Table.Row>
                    ))}
                  </Table.Body>
                </Table.Root>
              </div>
              <ListPagination
                paging={paging}
                onPrevious={() => updateParams({ page: String(Math.max(1, currentPage - 1)) })}
                onNext={() => updateParams({ page: String(currentPage + 1) })}
              />
            </>
          </ListFeedback>
        </Flex>
      </Card>

      <FormDialog
        open={selectedName !== null}
        onOpenChange={(open) => { if (!open) setSelectedName(null) }}
        title={`Versions of ${selectedName ?? ''}`}
        description="Previous values remain encrypted and cannot be viewed. Contract references use the current version."
        descriptionId="secret-versions-dialog-description"
        maxWidth="680px"
        footer={<Button type="button" variant="soft" color="gray" onClick={() => setSelectedName(null)}>Close</Button>}
      >
        {versions.isPending ? <Text size="2" color="gray">Loading versions...</Text> : null}
        {versions.isError ? <Text size="2" color="red">Unable to load versions.</Text> : null}
        {!versions.isPending && !versions.isError && versions.data?.length === 0 ? <Text size="2" color="gray">No versions found.</Text> : null}
        {versions.data?.length ? (
          <div className="responsive-table">
            <Table.Root aria-label={`Versions of ${selectedName ?? 'secret'}`}>
              <Table.Header>
                <Table.Row>
                  <Table.ColumnHeaderCell>Version</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Created</Table.ColumnHeaderCell>
                  <Table.ColumnHeaderCell>Status</Table.ColumnHeaderCell>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {versions.data.map((version) => {
                  const isActive = version.version === selectedSecret?.currentVersion
                  return (
                    <Table.Row key={version.version}>
                      <Table.RowHeaderCell>{version.version}</Table.RowHeaderCell>
                      <Table.Cell className="table-nowrap">{new Date(version.createdAt).toLocaleString()}</Table.Cell>
                      <Table.Cell>{isActive ? <Badge color="green">Active</Badge> : <Text size="2" color="gray">Previous</Text>}</Table.Cell>
                    </Table.Row>
                  )
                })}
              </Table.Body>
            </Table.Root>
          </div>
        ) : null}
      </FormDialog>

      <FormDialog
        open={editingName !== null}
        onOpenChange={handleUpdateOpenChange}
        title="Update secret value"
        description={`Add a new version of ${editingName ?? ''}. Existing contracts will use it immediately; previous versions remain encrypted.`}
        footer={<><Button type="button" variant="soft" color="gray" onClick={() => handleUpdateOpenChange(false)}>Cancel</Button><Button type="submit" form="update-secret-form" disabled={update.isPending || !newValue.trim()}>{update.isPending ? 'Creating...' : 'Create new version'}</Button></>}
      >
        <form id="update-secret-form" onSubmit={(event) => void handleUpdate(event)}>
          <Flex direction="column" gap="2">
            <Text as="label" size="2" htmlFor="updated-secret-value">New secret value</Text>
            <TextField.Root id="updated-secret-value" type="password" autoComplete="new-password" value={newValue} onChange={(event) => setNewValue(event.target.value)} required />
          </Flex>
        </form>
      </FormDialog>
    </Flex>
  )
}
