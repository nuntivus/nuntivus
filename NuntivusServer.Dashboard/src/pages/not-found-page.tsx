import { Card, Flex, Heading, Text } from '@radix-ui/themes'
import { Link } from 'react-router-dom'

export function NotFoundPage() {
  return (
    <Flex direction="column" gap="4">
      <Heading size="6">404 - Page not found</Heading>
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Text size="2">
            The requested page does not exist. Return to the <Link to="/">dashboard</Link>.
          </Text>
        </Flex>
      </Card>
    </Flex>
  )
}
