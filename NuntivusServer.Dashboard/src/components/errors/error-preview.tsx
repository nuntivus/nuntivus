import { useState } from 'react'
import { FormDialog } from '../dialogs/form-dialog'

export function ErrorPreview({ error, title = 'Error details' }: {
  error: string | null | undefined
  title?: string
}) {
  const [open, setOpen] = useState(false)

  if (!error) return '-'

  return (
    <>
      <button
        type="button"
        className="error-preview-trigger"
        title="Open full error details"
        aria-label={`Open ${title.toLowerCase()}`}
        onClick={() => setOpen(true)}
      >
        <span className="error-preview-text">{error}</span>
      </button>
      <FormDialog open={open} onOpenChange={setOpen} title={title} maxWidth="900px">
        <pre className="error-details-content">{error}</pre>
      </FormDialog>
    </>
  )
}
