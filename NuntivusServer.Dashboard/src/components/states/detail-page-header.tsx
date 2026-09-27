import { Flex, Heading, Text } from '@radix-ui/themes'
import type { ComponentType, ReactNode, SVGProps } from 'react'

type DetailPageHeaderProps = {
    title: string
    description: string
    icon: ComponentType<SVGProps<SVGSVGElement>>
    actions?: ReactNode
}

export function DetailPageHeader({ title, description, icon: Icon, actions }: DetailPageHeaderProps) {
    return (
        <Flex align="center" justify="between" wrap="wrap" gap="3" className="detail-page-header">
            <Flex align="center" gap="3" className="detail-page-heading">
                <span className="detail-page-icon" aria-hidden="true">
                    <Icon />
                </span>
                <div>
                    <Heading size="6">{title}</Heading>
                    <Text size="2" color="gray">{description}</Text>
                </div>
            </Flex>
            {actions ? <Flex gap="2" wrap="wrap">{actions}</Flex> : null}
        </Flex>
    )
}