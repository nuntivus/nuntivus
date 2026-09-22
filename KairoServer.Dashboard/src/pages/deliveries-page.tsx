import { Card, Flex, Heading, Text } from '@radix-ui/themes'

export function DeliveriesPage() {
  return (
    <Flex direction="column" gap="4">
      <Heading size="6">Deliveries</Heading>
      <Card>
        <Text size="2" color="gray">
          Delivery operations and diagnostics screens will be placed here.
        </Text>
      </Card>
    </Flex>
  )
}
