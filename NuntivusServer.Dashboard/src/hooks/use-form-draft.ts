import { useState, type Dispatch, type SetStateAction } from 'react'

export function useFormDraft<T>(source: T, scope: string): [T, Dispatch<SetStateAction<T>>] {
  const [state, setState] = useState({ source, scope, draft: source })
  let draft = state.draft
  if (state.source !== source || state.scope !== scope) {
    const pristine = JSON.stringify(state.draft) === JSON.stringify(state.source)
    draft = state.scope !== scope || pristine ? source : state.draft
    // Reconcile a changed input before rendering children; retain unsaved edits.
    setState({ source, scope, draft })
  }
  const setDraft: Dispatch<SetStateAction<T>> = (action) => {
    setState((current) => ({
      ...current,
      draft: typeof action === 'function'
        ? (action as (previous: T) => T)(current.draft)
        : action,
    }))
  }
  return [draft, setDraft]
}
