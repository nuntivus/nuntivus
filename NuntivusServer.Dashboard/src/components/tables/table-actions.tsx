import { IconButton, Table, Tooltip } from '@radix-ui/themes'
import type { CSSProperties, KeyboardEvent, MouseEvent, ReactNode } from 'react'
import { NavLink, useNavigate } from 'react-router-dom'
import { ChevronRightIcon } from '../icons/nav-icons'

interface NavigableTableRowProps {
    to: string
    label: string
    children: ReactNode
    className?: string
    style?: CSSProperties
}

function isInteractiveTarget(target: EventTarget | null) {
    return target instanceof HTMLElement && !!target.closest('a, button, input, select, textarea, [role="button"], [role="checkbox"]')
}

export function NavigableTableRow({ to, label, children, className, style }: NavigableTableRowProps) {
    const navigate = useNavigate()

    function handleClick(event: MouseEvent<HTMLTableRowElement>) {
        if (!isInteractiveTarget(event.target)) {
            navigate(to)
        }
    }

    function handleKeyDown(event: KeyboardEvent<HTMLTableRowElement>) {
        if (event.target === event.currentTarget && (event.key === 'Enter' || event.key === ' ')) {
            event.preventDefault()
            navigate(to)
        }
    }

    return (
        <Table.Row
            className={`navigable-table-row${className ? ` ${className}` : ''}`}
            style={style}
            tabIndex={0}
            aria-label={`Open ${label}`}
            onClick={handleClick}
            onKeyDown={handleKeyDown}
        >
            {children}
        </Table.Row>
    )
}

export function DetailAction({ to, label }: { to: string; label: string }) {
    return (
        <Tooltip content={label}>
            <IconButton asChild size="1" variant="ghost" color="gray" aria-label={label}>
                <NavLink to={to}>
                    <ChevronRightIcon width={16} height={16} />
                </NavLink>
            </IconButton>
        </Tooltip>
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