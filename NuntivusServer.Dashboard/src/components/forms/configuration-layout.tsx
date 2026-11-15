import { Heading, Text, Tooltip } from '@radix-ui/themes'
import { useId, type ReactNode } from 'react'
import './configuration-layout.css'

type ConfigurationHeadingProps = {
  title: string
  description: string
  icon: ReactNode
  id?: string
}

export function ConfigurationHeading({ title, description, icon, id }: ConfigurationHeadingProps) {
  return <div className="configuration-heading">
    <span className="configuration-icon">{icon}</span>
    <div><Heading id={id} size="4">{title}</Heading><Text as="p" size="2" color="gray">{description}</Text></div>
  </div>
}

export function ConfigurationSection({ title, description, icon, children, variant = 'panel' }: ConfigurationHeadingProps & {
  children: ReactNode
  variant?: 'panel' | 'divider'
}) {
  const id = useId()
  return <section className={`configuration-section configuration-section-${variant}`} aria-labelledby={id}>
    <ConfigurationHeading id={id} title={title} description={description} icon={icon} />
    {children}
  </section>
}

export function ConfigurationGrid({ children, columns = 3, checkboxes = false }: {
  children: ReactNode
  columns?: 2 | 3 | 4
  checkboxes?: boolean
}) {
  return <div className={`configuration-grid configuration-grid-${columns}${checkboxes ? ' configuration-checkboxes' : ''}`}>{children}</div>
}

export function ConfigurationLabel({ children, help }: { children: ReactNode; help: string }) {
  return <span className="configuration-label">{children}<Tooltip content={help}>
    <button type="button" className="configuration-help" aria-label={help} onClick={event => event.preventDefault()}>
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true"><circle cx="12" cy="12" r="9" /><path d="M12 11v6M12 7h.01" /></svg>
    </button>
  </Tooltip></span>
}
