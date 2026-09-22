import { Button, Card, Flex, Heading, Select, Text, TextField } from '@radix-ui/themes'
import { useEffect, useMemo, useState } from 'react'
import { hasPermission } from '../auth/permissions'
import {
  type InstanceSettings,
  type UpdateInstanceSettingsRequest,
} from '../services/api/settings-api'
import { getApiErrorMessage } from '../services/api/api-client'
import {
  useLocalUserPreferences,
  type UserPreferences,
} from '../features/settings/local-user-preferences'
import {
  useInstanceSettingsQuery,
  useUpdateInstanceSettingsMutation,
} from '../features/settings/use-settings'
import { useAppSelector } from '../store/hooks'

function toRequest(settings: InstanceSettings): UpdateInstanceSettingsRequest {
  return {
    deliveryDefaults: settings.deliveryDefaults,
    retryDefaults: settings.retryDefaults,
    security: settings.security,
    retention: settings.retention,
    limits: settings.limits,
  }
}

function parseNumberInput(value: string, fallback: number) {
  const parsed = Number.parseInt(value, 10)
  if (Number.isNaN(parsed)) {
    return fallback
  }

  return parsed
}

export function SettingsPage() {
  const { currentUser } = useAppSelector((state) => state.auth)
  const canWriteSettings = hasPermission(currentUser, 'settings.write')

  const instanceQuery = useInstanceSettingsQuery(canWriteSettings)
  const updateInstanceMutation = useUpdateInstanceSettingsMutation()
  const {
    preferences,
    save: savePreferences,
    resetToDefault: resetPreferencesToDefault,
  } = useLocalUserPreferences(currentUser?.subject)

  const [instanceDraft, setInstanceDraft] = useState<InstanceSettings | null>(null)
  const [preferencesDraft, setPreferencesDraft] = useState<UserPreferences>(preferences)

  useEffect(() => {
    if (instanceQuery.data) {
      setInstanceDraft(instanceQuery.data)
    }
  }, [instanceQuery.data])

  useEffect(() => {
    setPreferencesDraft(preferences)
  }, [preferences])

  const instanceDirty = useMemo(() => {
    if (!instanceDraft || !instanceQuery.data) {
      return false
    }

    return JSON.stringify(toRequest(instanceDraft)) !== JSON.stringify(toRequest(instanceQuery.data))
  }, [instanceDraft, instanceQuery.data])

  const preferencesDirty = useMemo(
    () => JSON.stringify(preferencesDraft) !== JSON.stringify(preferences),
    [preferences, preferencesDraft],
  )

  async function handleSaveInstanceSettings() {
    if (!instanceDraft || !canWriteSettings) {
      return
    }

    const updated = await updateInstanceMutation.mutateAsync(toRequest(instanceDraft))
    setInstanceDraft(updated)
  }

  function handleSavePreferences() {
    savePreferences(preferencesDraft)
  }

  return (
    <Flex direction="column" gap="4">
      <Heading size="6">Settings</Heading>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">User preferences</Heading>
          <Text size="2" color="gray">
            Preferences are private to your account and stored locally in this browser.
          </Text>

          <Text size="2" color="gray">Theme</Text>
          <Select.Root
            value={preferencesDraft.theme}
            onValueChange={(value) =>
              setPreferencesDraft((current) => ({
                ...current,
                theme: value === 'dark' ? 'dark' : 'light',
              }))
            }
          >
            <Select.Trigger />
            <Select.Content>
              <Select.Item value="light">Light</Select.Item>
              <Select.Item value="dark">Dark</Select.Item>
            </Select.Content>
          </Select.Root>

          <Text size="2" color="gray">Default page size</Text>
          <Select.Root
            value={String(preferencesDraft.defaultPageSize)}
            onValueChange={(value) =>
              setPreferencesDraft((current) => ({
                ...current,
                defaultPageSize: parseNumberInput(value, current.defaultPageSize),
              }))
            }
          >
            <Select.Trigger />
            <Select.Content>
              <Select.Item value="10">10</Select.Item>
              <Select.Item value="20">20</Select.Item>
              <Select.Item value="50">50</Select.Item>
              <Select.Item value="100">100</Select.Item>
            </Select.Content>
          </Select.Root>

          <Flex direction="column" gap="2">
            <Text size="2" color="gray">Table preferences</Text>
            <label>
              <input
                type="checkbox"
                checked={preferencesDraft.denseTables}
                onChange={(event) =>
                  setPreferencesDraft((current) => ({
                    ...current,
                    denseTables: event.target.checked,
                  }))
                }
              />{' '}
              Dense rows
            </label>
          </Flex>

          <Flex wrap="wrap" gap="2">
            <TextField.Root
              type="number"
              placeholder="Deliveries page size"
              value={String(preferencesDraft.tablePageSizes.deliveries)}
              onChange={(event) =>
                setPreferencesDraft((current) => ({
                  ...current,
                  tablePageSizes: {
                    ...current.tablePageSizes,
                    deliveries: parseNumberInput(
                      event.target.value,
                      current.tablePageSizes.deliveries,
                    ),
                  },
                }))
              }
            />
            <TextField.Root
              type="number"
              placeholder="Events page size"
              value={String(preferencesDraft.tablePageSizes.events)}
              onChange={(event) =>
                setPreferencesDraft((current) => ({
                  ...current,
                  tablePageSizes: {
                    ...current.tablePageSizes,
                    events: parseNumberInput(
                      event.target.value,
                      current.tablePageSizes.events,
                    ),
                  },
                }))
              }
            />
            <TextField.Root
              type="number"
              placeholder="Contracts page size"
              value={String(preferencesDraft.tablePageSizes.contracts)}
              onChange={(event) =>
                setPreferencesDraft((current) => ({
                  ...current,
                  tablePageSizes: {
                    ...current.tablePageSizes,
                    contracts: parseNumberInput(
                      event.target.value,
                      current.tablePageSizes.contracts,
                    ),
                  },
                }))
              }
            />
          </Flex>

          <Flex gap="2">
            <Button type="button" onClick={handleSavePreferences} disabled={!preferencesDirty}>
              Save preferences
            </Button>
            <Button
              type="button"
              variant="soft"
              onClick={() => setPreferencesDraft(preferences)}
              disabled={!preferencesDirty}
            >
              Cancel
            </Button>
            <Button
              type="button"
              variant="outline"
              onClick={() => setPreferencesDraft(resetPreferencesToDefault())}
            >
              Reset to defaults
            </Button>
          </Flex>
        </Flex>
      </Card>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">Instance settings</Heading>
          {!canWriteSettings ? (
            <Text size="2" color="gray">
              You do not have permission to edit instance settings.
            </Text>
          ) : instanceQuery.isLoading ? (
            <Text size="2" color="gray">Loading instance settings...</Text>
          ) : instanceQuery.isError ? (
            <Text size="2" color="red">
              {getApiErrorMessage(instanceQuery.error, 'Unable to load instance settings.')}
            </Text>
          ) : instanceDraft ? (
            <>
              <Heading size="3">General settings</Heading>

              <Heading size="2">Delivery defaults</Heading>
              <Flex wrap="wrap" gap="2">
                <TextField.Root
                  type="number"
                  value={String(instanceDraft.deliveryDefaults.requestTimeoutSeconds)}
                  onChange={(event) =>
                    setInstanceDraft((current) =>
                      current
                        ? {
                            ...current,
                            deliveryDefaults: {
                              ...current.deliveryDefaults,
                              requestTimeoutSeconds: parseNumberInput(
                                event.target.value,
                                current.deliveryDefaults.requestTimeoutSeconds,
                              ),
                            },
                          }
                        : current
                    )
                  }
                />
                <TextField.Root
                  type="number"
                  value={String(instanceDraft.deliveryDefaults.maxAttempts)}
                  onChange={(event) =>
                    setInstanceDraft((current) =>
                      current
                        ? {
                            ...current,
                            deliveryDefaults: {
                              ...current.deliveryDefaults,
                              maxAttempts: parseNumberInput(
                                event.target.value,
                                current.deliveryDefaults.maxAttempts,
                              ),
                            },
                          }
                        : current
                    )
                  }
                />
                <TextField.Root
                  type="number"
                  value={String(instanceDraft.deliveryDefaults.batchLimit)}
                  onChange={(event) =>
                    setInstanceDraft((current) =>
                      current
                        ? {
                            ...current,
                            deliveryDefaults: {
                              ...current.deliveryDefaults,
                              batchLimit: parseNumberInput(
                                event.target.value,
                                current.deliveryDefaults.batchLimit,
                              ),
                            },
                          }
                        : current
                    )
                  }
                />
              </Flex>

              <Heading size="2">Retry defaults</Heading>
              <Flex wrap="wrap" gap="2">
                <TextField.Root
                  type="number"
                  value={String(instanceDraft.retryDefaults.initialDelaySeconds)}
                  onChange={(event) =>
                    setInstanceDraft((current) =>
                      current
                        ? {
                            ...current,
                            retryDefaults: {
                              ...current.retryDefaults,
                              initialDelaySeconds: parseNumberInput(
                                event.target.value,
                                current.retryDefaults.initialDelaySeconds,
                              ),
                            },
                          }
                        : current
                    )
                  }
                />
                <TextField.Root
                  type="number"
                  value={String(instanceDraft.retryDefaults.maxDelaySeconds)}
                  onChange={(event) =>
                    setInstanceDraft((current) =>
                      current
                        ? {
                            ...current,
                            retryDefaults: {
                              ...current.retryDefaults,
                              maxDelaySeconds: parseNumberInput(
                                event.target.value,
                                current.retryDefaults.maxDelaySeconds,
                              ),
                            },
                          }
                        : current
                    )
                  }
                />
                <TextField.Root
                  type="number"
                  value={String(instanceDraft.retryDefaults.backoffMultiplier)}
                  onChange={(event) =>
                    setInstanceDraft((current) =>
                      current
                        ? {
                            ...current,
                            retryDefaults: {
                              ...current.retryDefaults,
                              backoffMultiplier: Number.parseFloat(event.target.value) || current.retryDefaults.backoffMultiplier,
                            },
                          }
                        : current
                    )
                  }
                />
              </Flex>

              <Heading size="2">Retention settings</Heading>
              <Flex wrap="wrap" gap="2">
                <TextField.Root
                  type="number"
                  value={String(instanceDraft.retention.eventRetentionDays)}
                  onChange={(event) =>
                    setInstanceDraft((current) =>
                      current
                        ? {
                            ...current,
                            retention: {
                              ...current.retention,
                              eventRetentionDays: parseNumberInput(
                                event.target.value,
                                current.retention.eventRetentionDays,
                              ),
                            },
                          }
                        : current
                    )
                  }
                />
                <TextField.Root
                  type="number"
                  value={String(instanceDraft.retention.deliveryAuditRetentionDays)}
                  onChange={(event) =>
                    setInstanceDraft((current) =>
                      current
                        ? {
                            ...current,
                            retention: {
                              ...current.retention,
                              deliveryAuditRetentionDays: parseNumberInput(
                                event.target.value,
                                current.retention.deliveryAuditRetentionDays,
                              ),
                            },
                          }
                        : current
                    )
                  }
                />
                <TextField.Root
                  type="number"
                  value={String(instanceDraft.retention.operationAuditRetentionDays)}
                  onChange={(event) =>
                    setInstanceDraft((current) =>
                      current
                        ? {
                            ...current,
                            retention: {
                              ...current.retention,
                              operationAuditRetentionDays: parseNumberInput(
                                event.target.value,
                                current.retention.operationAuditRetentionDays,
                              ),
                            },
                          }
                        : current
                    )
                  }
                />
              </Flex>

              <Heading size="2">Security settings</Heading>
              <Flex direction="column" gap="2">
                <label>
                  <input
                    type="checkbox"
                    checked={instanceDraft.security.requireHttpsDestinations}
                    onChange={(event) =>
                      setInstanceDraft((current) =>
                        current
                          ? {
                              ...current,
                              security: {
                                ...current.security,
                                requireHttpsDestinations: event.target.checked,
                              },
                            }
                          : current
                      )
                    }
                  />{' '}
                  Require HTTPS destinations
                </label>
                <label>
                  <input
                    type="checkbox"
                    checked={instanceDraft.security.allowPrivateNetworkDestinations}
                    onChange={(event) =>
                      setInstanceDraft((current) =>
                        current
                          ? {
                              ...current,
                              security: {
                                ...current.security,
                                allowPrivateNetworkDestinations: event.target.checked,
                              },
                            }
                          : current
                      )
                    }
                  />{' '}
                  Allow private network destinations
                </label>
                <label>
                  <input
                    type="checkbox"
                    checked={instanceDraft.security.requireDestinationCredentialEncryption}
                    onChange={(event) =>
                      setInstanceDraft((current) =>
                        current
                          ? {
                              ...current,
                              security: {
                                ...current.security,
                                requireDestinationCredentialEncryption: event.target.checked,
                              },
                            }
                          : current
                      )
                    }
                  />{' '}
                  Require destination credential encryption
                </label>
              </Flex>

              <Heading size="2">HTTP delivery defaults and limits</Heading>
              <Flex wrap="wrap" gap="2">
                <TextField.Root
                  type="number"
                  value={String(instanceDraft.limits.maxEventPayloadBytes)}
                  onChange={(event) =>
                    setInstanceDraft((current) =>
                      current
                        ? {
                            ...current,
                            limits: {
                              ...current.limits,
                              maxEventPayloadBytes: parseNumberInput(
                                event.target.value,
                                current.limits.maxEventPayloadBytes,
                              ),
                            },
                          }
                        : current
                    )
                  }
                />
                <TextField.Root
                  type="number"
                  value={String(instanceDraft.limits.maxEventMetadataBytes)}
                  onChange={(event) =>
                    setInstanceDraft((current) =>
                      current
                        ? {
                            ...current,
                            limits: {
                              ...current.limits,
                              maxEventMetadataBytes: parseNumberInput(
                                event.target.value,
                                current.limits.maxEventMetadataBytes,
                              ),
                            },
                          }
                        : current
                    )
                  }
                />
                <TextField.Root
                  type="number"
                  value={String(instanceDraft.limits.maxContractsPerCustomer)}
                  onChange={(event) =>
                    setInstanceDraft((current) =>
                      current
                        ? {
                            ...current,
                            limits: {
                              ...current.limits,
                              maxContractsPerCustomer: parseNumberInput(
                                event.target.value,
                                current.limits.maxContractsPerCustomer,
                              ),
                            },
                          }
                        : current
                    )
                  }
                />
                <TextField.Root
                  type="number"
                  value={String(instanceDraft.limits.maxConcurrentReplayJobs)}
                  onChange={(event) =>
                    setInstanceDraft((current) =>
                      current
                        ? {
                            ...current,
                            limits: {
                              ...current.limits,
                              maxConcurrentReplayJobs: parseNumberInput(
                                event.target.value,
                                current.limits.maxConcurrentReplayJobs,
                              ),
                            },
                          }
                        : current
                    )
                  }
                />
              </Flex>

              <Heading size="3">Instance information</Heading>
              <Text size="2" color="gray">
                Last updated: {new Date(instanceDraft.updatedAt).toLocaleString()}
              </Text>
              <Text size="2" color="gray">Updated by: {instanceDraft.updatedBy ?? 'system'}</Text>

              {updateInstanceMutation.isError ? (
                <Text size="2" color="red">
                  {getApiErrorMessage(updateInstanceMutation.error, 'Unable to save instance settings.')}
                </Text>
              ) : null}

              <Flex gap="2">
                <Button
                  type="button"
                  onClick={handleSaveInstanceSettings}
                  disabled={!instanceDirty || updateInstanceMutation.isPending}
                >
                  {updateInstanceMutation.isPending ? 'Saving...' : 'Save instance settings'}
                </Button>
                <Button
                  type="button"
                  variant="soft"
                  disabled={!instanceDirty}
                  onClick={() => setInstanceDraft(instanceQuery.data ?? null)}
                >
                  Cancel
                </Button>
              </Flex>
            </>
          ) : (
            <Text size="2" color="gray">No instance settings available.</Text>
          )}
        </Flex>
      </Card>

      <Card>
        <Text size="2" color="gray">
          User preferences are intentionally separate from instance configuration.
        </Text>
      </Card>
    </Flex>
  )
}
