import { Button, Card, Flex, Heading, Text } from '@radix-ui/themes'
import { Link } from 'react-router-dom'

export function ForbiddenPage() {
  return (
    <Flex direction="column" gap="4">
      <Heading size="6">403 - Forbidden</Heading>
      <Card>
        <Flex direction="column" gap="3" align="start">
          <Text size="2" color="gray">
            You are authenticated but do not have permission to access this resource.
          </Text>
          <Button asChild variant="soft">
            <Link to="/">Back to overview</Link>
          </Button>
        </Flex>
      </Card>
    </Flex>
  )
}
