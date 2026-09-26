import { Button, Card, Flex, Skeleton, Text } from '@radix-ui/themes'
import type { ReactNode } from 'react'
import { ApiError, getApiErrorMessage } from '../../services/api/api-client'

type EmptyStateAction = {
  label: string
  onClick: () => void
}

interface ListFeedbackProps {
  isLoading: boolean
  isError: boolean
  error?: unknown
  isEmpty: boolean
  loadingMessage: string
  errorMessage: string
  emptyMessage: string
  emptyTitle?: string
  emptyDescription?: string
  emptyAction?: EmptyStateAction
  skeletonRows?: number
  children: ReactNode
}

function ListLoadingSkeleton({ rows }: { rows: number }) {
  return (
    <Flex direction="column" gap="2">
      {Array.from({ length: rows }).map((_, index) => (
        <Skeleton key={`skeleton-${index}`} height="20px" />
      ))}
    </Flex>
  )
}

export function ListFeedback({
  isLoading,
  isError,
  error,
  isEmpty,
  loadingMessage,
  errorMessage,
  emptyMessage,
  emptyTitle,
  emptyDescription,
  emptyAction,
  skeletonRows = 6,
  children,
}: ListFeedbackProps) {
  if (isLoading) {
    return (
      <Flex direction="column" gap="2" role="status" aria-live="polite" aria-busy="true">
        <Text size="2" color="gray">{loadingMessage}</Text>
        <ListLoadingSkeleton rows={skeletonRows} />
      </Flex>
    )
  }

  if (isError) {
    const resolvedMessage =
      error instanceof ApiError
        ? getApiErrorMessage(error, errorMessage)
        : errorMessage

    return (
      <Text size="2" color="red" role="alert" aria-live="assertive">
        {resolvedMessage}
      </Text>
    )
  }

  if (isEmpty) {
    const resolvedDescription = emptyDescription ?? emptyMessage

    return (
      <Card role="status" aria-live="polite">
        <Flex direction="column" gap="2" align="start">
          {emptyTitle ? (
            <Text size="3" weight="medium">
              {emptyTitle}
            </Text>
          ) : null}
          <Text size="2" color="gray">
            {resolvedDescription}
          </Text>
          {emptyAction ? (
            <Button type="button" variant="soft" onClick={emptyAction.onClick}>
              {emptyAction.label}
            </Button>
          ) : null}
        </Flex>
      </Card>
    )
  }

  return <>{children}</>
}
