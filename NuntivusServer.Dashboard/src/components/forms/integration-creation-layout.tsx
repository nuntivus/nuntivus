import type { ReactNode } from 'react'
import './integration-creation-layout.css'

interface IntegrationCreationLayoutProps {
  picker?: ReactNode
  children: ReactNode
}

export function IntegrationCreationLayout({ picker, children }: IntegrationCreationLayoutProps) {
  if (!picker) return <>{children}</>

  return <div className="integration-creation-layout">
    <div className="integration-creation-types">{picker}</div>
    <div className="integration-creation-content">{children}</div>
  </div>
}
