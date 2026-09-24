import { Dialog, Flex, IconButton, Text } from '@radix-ui/themes'
import type { ReactNode } from 'react'

interface FormDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description?: string
  /** Form fields / body content. */
  children: ReactNode
  /** Right-aligned footer actions (e.g. Cancel + Submit buttons). */
  footer?: ReactNode
  /** Dialog max width. Defaults to a compact 600px. */
  maxWidth?: string
  /** Optional id used to associate the description for accessibility. */
  descriptionId?: string
}

/**
 * Shared modal wrapper used for create/edit forms across the dashboard.
 *
 * The dialog is closed by default and only renders its content when `open`
 * is true (Radix mounts content into a portal behind an overlay). It provides
 * focus trapping, Escape-to-close, backdrop dismissal, a header with an
 * optional description, a top-right close button, and a right-aligned footer.
 */
export function FormDialog({
  open,
  onOpenChange,
  title,
  description,
  children,
  footer,
  maxWidth = '600px',
  descriptionId,
}: FormDialogProps) {
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Content
        className="form-dialog"
        maxWidth={maxWidth}
        aria-describedby={description ? descriptionId : undefined}
      >
        <Flex justify="between" align="start" gap="3" mb={description ? '1' : '4'}>
          <Dialog.Title className="form-dialog-title">{title}</Dialog.Title>
          <Dialog.Close>
            <IconButton
              type="button"
              variant="ghost"
              color="gray"
              aria-label="Close dialog"
              className="form-dialog-close"
            >
              <svg
                width="16"
                height="16"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="1.8"
                strokeLinecap="round"
                strokeLinejoin="round"
                aria-hidden="true"
              >
                <path d="M18 6 6 18M6 6l12 12" />
              </svg>
            </IconButton>
          </Dialog.Close>
        </Flex>

        {description ? (
          <Dialog.Description
            id={descriptionId}
            className="form-dialog-description"
          >
            <Text size="2" color="gray">
              {description}
            </Text>
          </Dialog.Description>
        ) : null}

        <div className="form-dialog-body">{children}</div>

        {footer ? (
          <Flex gap="2" justify="end" mt="4" className="form-dialog-footer">
            {footer}
          </Flex>
        ) : null}
      </Dialog.Content>
    </Dialog.Root>
  )
}
