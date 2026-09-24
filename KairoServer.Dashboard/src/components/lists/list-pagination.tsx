import { Button, Flex, Text } from '@radix-ui/themes'

interface PagingData {
  page: number
  totalPages: number
  totalCount: number
  hasPrevious: boolean
  hasNext: boolean
}

interface ListPaginationProps {
  paging: PagingData | undefined
  onPrevious: () => void
  onNext: () => void
}

export function ListPagination({ paging, onPrevious, onNext }: ListPaginationProps) {
  return (
    <Flex align="center" justify="between" gap="3">
      <Text size="2" color="gray">
        {paging
          ? `Page ${paging.page} of ${paging.totalPages} · ${paging.totalCount} total`
          : ''}
      </Text>

      <Flex gap="2">
        <Button variant="soft" disabled={!paging?.hasPrevious} onClick={onPrevious}>
          Previous
        </Button>
        <Button variant="soft" disabled={!paging?.hasNext} onClick={onNext}>
          Next
        </Button>
      </Flex>
    </Flex>
  )
}
