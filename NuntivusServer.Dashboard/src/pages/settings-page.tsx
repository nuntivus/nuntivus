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
import { useNotifications } from '../hooks/use-notifications'
import { useAppSelector } from '../store/hooks'
import { frontendConfig } from '../app/frontend-config'

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
  const notifications = useNotifications()
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

    try {
      const updated = await updateInstanceMutation.mutateAsync(toRequest(instanceDraft))
      setInstanceDraft(updated)
      notifications.success('Instance settings saved.', 'Settings')
    } catch (error) {
      notifications.error(getApiErrorMessage(error, 'Unable to save instance settings.'), 'Settings')
    }
  }

  function handleSavePreferences() {
    savePreferences(preferencesDraft)
    notifications.success('Preferences saved.', 'Settings')
  }

  return (
    <Flex direction="column" gap="4">
      <div className="page-header">
        <div>
          <h1 className="page-title">Settings</h1>
          <p className="page-subtitle">Manage operator preferences and server-wide delivery defaults.</p>
        </div>
      </div>

      <Card>
        <Flex direction="column" gap="2">
          <Heading size="4">Frontend runtime</Heading>
          <Text size="2" color="gray">Version: {frontendConfig.version}</Text>
          <Text size="2" color="gray">Environment: {frontendConfig.runtimeEnvironment}</Text>
          <Text size="2" color="gray">API base URL: {frontendConfig.apiBaseUrl}</Text>
        </Flex>
      </Card>

      <Card>
        <Flex direction="column" gap="3">
          <Heading size="4">User preferences</Heading>
          <Text size="2" color="gray">
            Preferences are private to your account and stored locally in this browser.
          </Text>

          <Text id="settings-theme-label" size="2" color="gray">Theme</Text>
          <Select.Root
            value={preferencesDraft.theme}
            onValueChange={(value) =>
              setPreferencesDraft((current) => ({
                ...current,
                theme: value === 'dark' ? 'dark' : 'light',
              }))
            }
          >
            <Select.Trigger aria-labelledby="settings-theme-label" />
            <Select.Content>
              <Select.Item value="light">Light</Select.Item>
              <Select.Item value="dark">Dark</Select.Item>
            </Select.Content>
          </Select.Root>

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
                <label className="field">
                  <Text size="2" color="gray">Request timeout (seconds)</Text>
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
                </label>
                <label className="field">
                  <Text size="2" color="gray">Maximum attempts</Text>
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
                </label>
                <label className="field">
                  <Text size="2" color="gray">Batch limit</Text>
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
                </label>
              </Flex>

              <Heading size="2">Retry defaults</Heading>
              <Flex wrap="wrap" gap="2">
                <label className="field">
                  <Text size="2" color="gray">Initial delay (seconds)</Text>
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
                </label>
                <label className="field">
                  <Text size="2" color="gray">Maximum delay (seconds)</Text>
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
                </label>
                <label className="field">
                  <Text size="2" color="gray">Backoff multiplier</Text>
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
                </label>
              </Flex>

              <Heading size="2">Retention settings</Heading>
              <Flex wrap="wrap" gap="2">
                <label className="field">
                  <Text size="2" color="gray">Event retention (days)</Text>
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
                </label>
                <label className="field">
                  <Text size="2" color="gray">Delivery audit retention (days)</Text>
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
                </label>
                <label className="field">
                  <Text size="2" color="gray">Operation audit retention (days)</Text>
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
                </label>
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
                <label className="field">
                  <Text size="2" color="gray">Maximum event payload (bytes)</Text>
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
                </label>
                <label className="field">
                  <Text size="2" color="gray">Maximum event metadata (bytes)</Text>
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
                </label>
                <label className="field">
                  <Text size="2" color="gray">Maximum contracts per customer</Text>
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
                </label>
                <label className="field">
                  <Text size="2" color="gray">Maximum concurrent replay jobs</Text>
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
                </label>
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
