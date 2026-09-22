import { Card, Flex, Heading, Text } from '@radix-ui/themes'
import { Link } from 'react-router-dom'

export function NotFoundPage() {
  return (
    <Flex direction="column" gap="4">
      <Heading size="6">Page not found</Heading>
      <Card>
        <Text size="2">
          The requested page does not exist. Return to the <Link to="/">dashboard</Link>.
        </Text>
      </Card>
    </Flex>
  )
}
