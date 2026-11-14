import { IconButton, Table, Tooltip } from '@radix-ui/themes'
import type { CSSProperties, KeyboardEvent, MouseEvent, ReactNode } from 'react'
import { NavLink, useNavigate } from 'react-router-dom'
import { ChevronRightIcon } from '../icons/nav-icons'

interface NavigableTableRowProps {
    to?: string
    label: string
    children: ReactNode
    className?: string
    style?: CSSProperties
    unstyled?: boolean
}

function isInteractiveTarget(target: EventTarget | null) {
    return target instanceof Element && !!target.closest('a, button, input, select, textarea, label, [role="button"], [role="checkbox"], [role="combobox"], [contenteditable="true"]')
}

export function NavigableTableRow({ to, label, children, className, style, unstyled = false }: NavigableTableRowProps) {
    const navigate = useNavigate()
    const Row = unstyled ? 'tr' : Table.Row

    function handleClick(event: MouseEvent<HTMLTableRowElement>) {
        if (to && !isInteractiveTarget(event.target)) {
            navigate(to)
        }
    }

    function handleKeyDown(event: KeyboardEvent<HTMLTableRowElement>) {
        if (to && event.target === event.currentTarget && (event.key === 'Enter' || event.key === ' ')) {
            event.preventDefault()
            navigate(to)
        }
    }

    return (
        <Row
            className={[to && 'navigable-table-row', className].filter(Boolean).join(' ')}
            style={style}
            tabIndex={to ? 0 : undefined}
            aria-label={to ? `Open ${label}` : undefined}
            onClick={handleClick}
            onKeyDown={handleKeyDown}
        >
            {children}
        </Row>
    )
}

export function DetailIndicator() {
    return (
        <span className="table-detail-indicator" aria-hidden="true" style={{ display: 'inline-flex', alignItems: 'center', color: 'var(--text-muted)' }}>
            <ChevronRightIcon width={16} height={16} focusable="false" />
        </span>
    )
}

export function TableIconLink({ to, label, children }: { to: string; label: string; children: ReactNode }) {
    return (
        <Tooltip content={label}>
            <IconButton asChild size="1" variant="soft" color="gray" aria-label={label}>
                <NavLink to={to}>{children}</NavLink>
            </IconButton>
        </Tooltip>
    )
}

interface TableIconButtonProps {
    label: string
    children: ReactNode
    onClick: () => void
    disabled?: boolean
    color?: 'gray' | 'red' | 'amber' | 'green'
}

export function TableIconButton({ label, children, onClick, disabled, color = 'gray' }: TableIconButtonProps) {
    return (
        <Tooltip content={label}>
            <IconButton
                type="button"
                size="1"
                variant="soft"
                color={color}
                aria-label={label}
                disabled={disabled}
                onClick={onClick}
            >
                {children}
            </IconButton>
        </Tooltip>
    )
}