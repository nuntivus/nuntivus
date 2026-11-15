import { Button, Card, Flex, Heading, Select, Text, TextField } from '@radix-ui/themes'
import { useMemo } from 'react'
import { useFormDraft } from '../hooks/use-form-draft'
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
import { ConfigurationGrid, ConfigurationHeading, ConfigurationLabel, ConfigurationSection } from '../components/forms/configuration-layout'
import { AuditLogsIcon, DeliveriesIcon, FailedDeliveriesIcon, SettingsIcon, SecretsIcon, ContractsIcon } from '../components/icons/nav-icons'

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

  const scope = currentUser?.subject ?? 'anonymous'
  const [instanceDraft, setInstanceDraft] = useFormDraft<InstanceSettings | null>(instanceQuery.data ?? null, scope)
  const [preferencesDraft, setPreferencesDraft] = useFormDraft<UserPreferences>(preferences, scope)

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

      <Card className="configuration-editor">
        <Flex direction="column" gap="2">
          <Heading size="4">Frontend runtime</Heading>
          <Text size="2" color="gray">Version: {frontendConfig.version}</Text>
          <Text size="2" color="gray">Environment: {frontendConfig.runtimeEnvironment}</Text>
          <Text size="2" color="gray">API base URL: {frontendConfig.apiBaseUrl}</Text>
        </Flex>
      </Card>

      <Card className="configuration-editor">
        <Flex direction="column" gap="3">
          <ConfigurationHeading title="User preferences" description="Preferences are private to your account and stored locally in this browser." icon={<SettingsIcon />} />

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

      <Card className="configuration-editor">
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
              <ConfigurationHeading title="General settings" description="Default values for event delivery, retries, and data retention." icon={<SettingsIcon />} />
              <ConfigurationSection title="Delivery defaults" description="Configure the default behavior for event delivery." icon={<DeliveriesIcon />}>
              <ConfigurationGrid>
                <label className="field">
                  <ConfigurationLabel help="Maximum time to wait for a destination response, in seconds.">Request timeout (seconds)</ConfigurationLabel>
                  <TextField.Root
                    type="number"
                    value={String(instanceDraft.deliveryDefaults.requestTimeoutSeconds)}
                    aria-label="Request timeout (seconds)"
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
                  <ConfigurationLabel help="Maximum number of delivery attempts before a delivery becomes dead.">Maximum attempts</ConfigurationLabel>
                  <TextField.Root
                    type="number"
                    value={String(instanceDraft.deliveryDefaults.maxAttempts)}
                    aria-label="Maximum attempts"
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
                  <ConfigurationLabel help="Default maximum number of events in a delivery batch.">Batch limit</ConfigurationLabel>
                  <TextField.Root
                    type="number"
                    value={String(instanceDraft.deliveryDefaults.batchLimit)}
                    aria-label="Batch limit"
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
              </ConfigurationGrid>
              </ConfigurationSection>
              <ConfigurationSection title="Retry defaults" description="Control the retry backoff behavior when delivery fails." icon={<FailedDeliveriesIcon />}>
              <ConfigurationGrid>
                <label className="field">
                  <ConfigurationLabel help="Delay before the first retry, in seconds.">Initial delay (seconds)</ConfigurationLabel>
                  <TextField.Root
                    type="number"
                    value={String(instanceDraft.retryDefaults.initialDelaySeconds)}
                    aria-label="Initial delay (seconds)"
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
                  <ConfigurationLabel help="Upper limit on the delay between retry attempts, in seconds.">Maximum delay (seconds)</ConfigurationLabel>
                  <TextField.Root
                    type="number"
                    value={String(instanceDraft.retryDefaults.maxDelaySeconds)}
                    aria-label="Maximum delay (seconds)"
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
                  <ConfigurationLabel help="Factor used to increase the delay after each failed attempt.">Backoff multiplier</ConfigurationLabel>
                  <TextField.Root
                    type="number"
                    value={String(instanceDraft.retryDefaults.backoffMultiplier)}
                    aria-label="Backoff multiplier"
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
              </ConfigurationGrid>
              </ConfigurationSection>
              <ConfigurationSection title="Retention settings" description="Define how long to keep events and audit data." icon={<AuditLogsIcon />}>
              <ConfigurationGrid>
                <label className="field">
                  <ConfigurationLabel help="Number of days to retain stored events.">Event retention (days)</ConfigurationLabel>
                  <TextField.Root
                    type="number"
                    value={String(instanceDraft.retention.eventRetentionDays)}
                    aria-label="Event retention (days)"
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
                  <ConfigurationLabel help="Number of days to retain delivery audit records.">Delivery audit retention (days)</ConfigurationLabel>
                  <TextField.Root
                    type="number"
                    value={String(instanceDraft.retention.deliveryAuditRetentionDays)}
                    aria-label="Delivery audit retention (days)"
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
                  <ConfigurationLabel help="Number of days to retain operational audit records.">Operation audit retention (days)</ConfigurationLabel>
                  <TextField.Root
                    type="number"
                    value={String(instanceDraft.retention.operationAuditRetentionDays)}
                    aria-label="Operation audit retention (days)"
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
              </ConfigurationGrid>
              </ConfigurationSection>
              <ConfigurationSection variant="divider" title="Security settings" description="Security requirements for event destinations." icon={<SecretsIcon />}>
              <ConfigurationGrid checkboxes>
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
              </ConfigurationGrid>
              </ConfigurationSection>
              <ConfigurationSection variant="divider" title="HTTP delivery defaults and limits" description="Limits and constraints for event delivery." icon={<ContractsIcon />}>
              <ConfigurationGrid columns={4}>
                <label className="field">
                  <ConfigurationLabel help="Largest accepted event payload, in bytes.">Maximum event payload (bytes)</ConfigurationLabel>
                  <TextField.Root
                    type="number"
                    value={String(instanceDraft.limits.maxEventPayloadBytes)}
                    aria-label="Maximum event payload (bytes)"
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
                  <ConfigurationLabel help="Largest accepted event metadata, in bytes.">Maximum event metadata (bytes)</ConfigurationLabel>
                  <TextField.Root
                    type="number"
                    value={String(instanceDraft.limits.maxEventMetadataBytes)}
                    aria-label="Maximum event metadata (bytes)"
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
                  <ConfigurationLabel help="Maximum number of contracts allowed for each customer.">Maximum contracts per customer</ConfigurationLabel>
                  <TextField.Root
                    type="number"
                    value={String(instanceDraft.limits.maxContractsPerCustomer)}
                    aria-label="Maximum contracts per customer"
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
                  <ConfigurationLabel help="Maximum number of event replay jobs allowed to run concurrently.">Maximum concurrent replay jobs</ConfigurationLabel>
                  <TextField.Root
                    type="number"
                    value={String(instanceDraft.limits.maxConcurrentReplayJobs)}
                    aria-label="Maximum concurrent replay jobs"
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
              </ConfigurationGrid>
              </ConfigurationSection>
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
