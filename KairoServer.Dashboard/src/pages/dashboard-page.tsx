import { Badge, Card, Flex, Heading, Text } from '@radix-ui/themes'
import { useReadinessStatusQuery } from '../features/system/use-readiness-status-query'

export function DashboardPage() {
  const readinessQuery = useReadinessStatusQuery()

  const readinessStatus = readinessQuery.data?.status ?? 'loading'
  const badgeColor =
    readinessStatus.toLowerCase() === 'healthy' ? 'green' : 'amber'

  return (
    <Flex direction="column" gap="4">
      <Heading size="6">Overview</Heading>
      <Card>
        <Flex direction="column" gap="3">
          <Flex align="center" gap="2">
            <Text size="2" weight="medium">
              API readiness
            </Text>
            <Badge color={badgeColor}>{readinessStatus}</Badge>
          </Flex>
          {readinessQuery.isError ? (
            <Text size="2" color="red">
              Unable to read /health/ready from KairoServer.Api.
            </Text>
          ) : (
            <Text size="2" color="gray">
              Admin dashboard entry point is ready. API-powered widgets will be added here.
            </Text>
          )}
        </Flex>
      </Card>
    </Flex>
  )
}
