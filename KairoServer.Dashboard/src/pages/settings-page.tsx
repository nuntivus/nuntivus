import { Card, Flex, Heading, Text } from '@radix-ui/themes'

export function SettingsPage() {
  return (
    <Flex direction="column" gap="4">
      <Heading size="6">Settings</Heading>
      <Card>
        <Text size="2" color="gray">
          Workspace and operator preferences will be managed here.
        </Text>
      </Card>
    </Flex>
  )
}
