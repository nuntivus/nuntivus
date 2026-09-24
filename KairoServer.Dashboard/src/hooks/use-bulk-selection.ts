import { useMemo, useState } from 'react'

export function useBulkSelection<TId extends string>(visibleIds: TId[]) {
  const [selectedSet, setSelectedSet] = useState<Set<TId>>(new Set())

  const selectedIds = useMemo(() => Array.from(selectedSet), [selectedSet])
  const allVisibleSelected = visibleIds.length > 0 && visibleIds.every((id) => selectedSet.has(id))

  function clearSelection() {
    setSelectedSet(new Set())
  }

  function toggleSelection(id: TId) {
    setSelectedSet((current) => {
      const next = new Set(current)

      if (next.has(id)) {
        next.delete(id)
      } else {
        next.add(id)
      }

      return next
    })
  }

  function toggleVisibleSelection() {
    setSelectedSet((current) => {
      const next = new Set(current)
      const shouldSelectAll = !visibleIds.every((id) => next.has(id))

      visibleIds.forEach((id) => {
        if (shouldSelectAll) {
          next.add(id)
        } else {
          next.delete(id)
        }
      })

      return next
    })
  }

  return {
    selectedIds,
    selectedCount: selectedSet.size,
    allVisibleSelected,
    toggleSelection,
    toggleVisibleSelection,
    clearSelection,
    isSelected: (id: TId) => selectedSet.has(id),
  }
}
