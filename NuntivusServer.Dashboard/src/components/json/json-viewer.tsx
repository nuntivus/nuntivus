import { Button, Flex, ScrollArea, Text, TextField } from '@radix-ui/themes'
import { Fragment, useMemo, useState } from 'react'

type JsonViewerProps = {
  value: unknown
  maxHeight?: number
  defaultCollapsed?: boolean
  searchPlaceholder?: string
  copyLabel?: string
  largePayloadThreshold?: number
  previewChars?: number
}

function safeStringify(value: unknown) {
  try {
    return JSON.stringify(value, null, 2) ?? 'null'
  } catch {
    return '{"error":"Unable to format JSON"}'
  }
}

function highlightText(line: string, term: string) {
  if (!term) {
    return line
  }

  const lowerLine = line.toLowerCase()
  const lowerTerm = term.toLowerCase()

  if (!lowerLine.includes(lowerTerm)) {
    return line
  }

  const parts: Array<{ text: string; match: boolean }> = []
  let start = 0

  while (start < line.length) {
    const index = lowerLine.indexOf(lowerTerm, start)
    if (index === -1) {
      parts.push({ text: line.slice(start), match: false })
      break
    }

    if (index > start) {
      parts.push({ text: line.slice(start, index), match: false })
    }

    parts.push({ text: line.slice(index, index + term.length), match: true })
    start = index + term.length
  }

  return (
    <>
      {parts.map((part, index) =>
        part.match ? (
          <mark key={`${part.text}-${index}`}>{part.text}</mark>
        ) : (
          <Fragment key={`${part.text}-${index}`}>{part.text}</Fragment>
        ),
      )}
    </>
  )
}

export function JsonViewer(props: JsonViewerProps) {
  const jsonText = useMemo(() => safeStringify(props.value), [props.value])
  const [collapsed, setCollapsed] = useState(props.defaultCollapsed ?? false)
  return <JsonViewerContent key={`${props.largePayloadThreshold ?? 200_000}:${jsonText}`}
    {...props} jsonText={jsonText} collapsed={collapsed}
    onToggleCollapsed={() => setCollapsed((current) => !current)} />
}

function JsonViewerContent({
  jsonText,
  collapsed,
  onToggleCollapsed,
  maxHeight = 360,
  searchPlaceholder = 'Search JSON',
  copyLabel = 'Copy JSON',
  largePayloadThreshold = 200_000,
  previewChars = 20_000,
}: Omit<JsonViewerProps, 'value' | 'defaultCollapsed'> & {
  jsonText: string
  collapsed: boolean
  onToggleCollapsed: () => void
}) {
  const [searchTerm, setSearchTerm] = useState('')
  const [copied, setCopied] = useState(false)
  const [showFullContent, setShowFullContent] = useState(jsonText.length <= largePayloadThreshold)

  const isLargePayload = jsonText.length > largePayloadThreshold


  const displayedText = showFullContent
    ? jsonText
    : `${jsonText.slice(0, previewChars)}\n\n... JSON preview truncated (${jsonText.length - previewChars} more characters)`

  const lines = useMemo(() => displayedText.split('\n'), [displayedText])
  const normalizedSearch = searchTerm.trim()

  const matchCount = useMemo(() => {
    if (!normalizedSearch) {
      return 0
    }

    const lowerTerm = normalizedSearch.toLowerCase()
    return lines.reduce((count, line) => {
      let index = 0
      let localCount = 0
      const lowerLine = line.toLowerCase()

      while (index < lowerLine.length) {
        const next = lowerLine.indexOf(lowerTerm, index)
        if (next === -1) {
          break
        }

        localCount += 1
        index = next + lowerTerm.length
      }

      return count + localCount
    }, 0)
  }, [lines, normalizedSearch])

  async function handleCopy() {
    await navigator.clipboard.writeText(jsonText)
    setCopied(true)

    window.setTimeout(() => {
      setCopied(false)
    }, 1500)
  }

  return (
    <Flex direction="column" gap="2">
      <Flex gap="2" wrap="wrap" align="center">
        <Button
          type="button"
          size="1"
          variant="soft"
          onClick={onToggleCollapsed}
        >
          {collapsed ? 'Expand' : 'Collapse'}
        </Button>

        <Button type="button" size="1" variant="soft" onClick={() => void handleCopy()}>
          {copied ? 'Copied' : copyLabel}
        </Button>

        {isLargePayload && !showFullContent ? (
          <Button
            type="button"
            size="1"
            color="amber"
            variant="soft"
            onClick={() => setShowFullContent(true)}
          >
            Load full JSON
          </Button>
        ) : null}

        {normalizedSearch ? (
          <Text size="1" color="gray">
            {matchCount} match{matchCount === 1 ? '' : 'es'}
          </Text>
        ) : null}
      </Flex>

      {!collapsed ? (
        <>
          <TextField.Root
            placeholder={searchPlaceholder}
            value={searchTerm}
            onChange={(event) => setSearchTerm(event.target.value)}
          />

          {!showFullContent && isLargePayload ? (
            <Text size="1" color="gray">
              Showing preview for large payload. Load full JSON to search all content.
            </Text>
          ) : null}

          <ScrollArea type="auto" scrollbars="both" style={{ maxHeight }}>
            <pre
              style={{
                margin: 0,
                whiteSpace: 'pre-wrap',
                overflowWrap: 'anywhere',
              }}
            >
              {lines.map((line, index) => (
                <Fragment key={`line-${index}`}>
                  {highlightText(line, normalizedSearch)}
                  {index < lines.length - 1 ? '\n' : ''}
                </Fragment>
              ))}
            </pre>
          </ScrollArea>
        </>
      ) : null}
    </Flex>
  )
}
