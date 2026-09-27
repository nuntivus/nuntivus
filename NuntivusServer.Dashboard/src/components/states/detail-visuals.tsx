import { Card, Flex, Heading, Text } from '@radix-ui/themes'
import type { ComponentType, ReactNode, SVGProps } from 'react'

type DetailIcon = ComponentType<SVGProps<SVGSVGElement>>

export function DetailMetric({ icon: Icon, label, value, supporting, tone = 'accent' }: {
    icon: DetailIcon
    label: string
    value: ReactNode
    supporting?: string
    tone?: 'accent' | 'danger' | 'success' | 'warning' | 'violet'
}) {
    return (
        <Card className={`visual-detail-metric visual-tone-${tone}`}>
            <span className="visual-detail-metric-icon"><Icon /></span>
            <div className="visual-detail-metric-content">
                <Text size="1" color="gray">{label}</Text>
                <div className="visual-detail-metric-value">{value}</div>
                {supporting ? <Text size="1" color="gray">{supporting}</Text> : null}
            </div>
        </Card>
    )
}

export function DetailSectionTitle({ icon: Icon, children, action }: {
    icon: DetailIcon
    children: ReactNode
    action?: ReactNode
}) {
    return (
        <Flex align="center" justify="between" gap="2" className="visual-detail-section-title">
            <Flex align="center" gap="2">
                <Icon />
                <Heading size="4">{children}</Heading>
            </Flex>
            {action}
        </Flex>
    )
}