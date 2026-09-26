import { Card, Flex, Spinner, Text } from '@radix-ui/themes'

type DetailLoadingStateProps = {
  message: string
}

export function DetailLoadingState({ message }: DetailLoadingStateProps) {
  return (
    <Card>
      <Flex align="center" gap="2">
        <Spinner size="1" />
        <Text size="2" color="gray">{message}</Text>
      </Flex>
    </Card>
  )
}
