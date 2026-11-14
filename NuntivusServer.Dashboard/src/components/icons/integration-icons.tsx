import type { SVGProps } from 'react'

type IconProps = SVGProps<SVGSVGElement>

const base = {
  width: 24,
  height: 24,
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 1.7,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
  'aria-hidden': true,
}

export function HttpIntegrationIcon(props: IconProps) {
  return <svg {...base} {...props}>
    <circle cx="12" cy="12" r="9" />
    <ellipse cx="12" cy="12" rx="4" ry="9" />
    <path d="M3 12h18M5 6h14M5 18h14" />
  </svg>
}

export function PollingIntegrationIcon(props: IconProps) {
  return <svg {...base} {...props}>
    <path d="M20 8a8 8 0 0 0-14-3L3 8M3 3v5h5M4 16a8 8 0 0 0 14 3l3-3M16 16h5v5" />
    <path d="M12 8v4l3 2" />
  </svg>
}

export function KafkaIntegrationIcon(props: IconProps) {
  return <svg {...base} {...props}>
    <circle cx="8" cy="12" r="3" />
    <circle cx="8" cy="3" r="2" />
    <circle cx="8" cy="21" r="2" />
    <circle cx="19" cy="6" r="2" />
    <circle cx="19" cy="18" r="2" />
    <path d="M8 5v4M8 15v4M11 10l6-3M11 14l6 3" />
  </svg>
}

export function NatsIntegrationIcon(props: IconProps) {
  return <svg {...base} {...props}>
    <path d="m3 10 18-7-7 18-3-7-8-4ZM11 14 21 3M3 18h4M6 21h4" />
  </svg>
}

export function RabbitMqIntegrationIcon(props: IconProps) {
  return <svg {...base} {...props}>
    <path d="M5 11V3h4v8h4V3h4v8h3v10H4V11h1Z" />
    <rect x="13" y="14" width="4" height="4" rx="0.5" />
  </svg>
}

export function SqsIntegrationIcon(props: IconProps) {
  return <svg {...base} {...props}>
    <rect x="7" y="3" width="14" height="4" rx="1" />
    <rect x="7" y="10" width="14" height="4" rx="1" />
    <rect x="7" y="17" width="14" height="4" rx="1" />
    <path d="M2 12h3M3 10l2 2-2 2" />
  </svg>
}

export function FolderIntegrationIcon(props: IconProps) {
  return <svg {...base} {...props}>
    <path d="M3 7V4h7l3 3h8v13H3V7ZM3 9h18" />
    <path d="M12 12v5M10 15l2 2 2-2" />
  </svg>
}

export function S3IntegrationIcon(props: IconProps) {
  return <svg {...base} {...props}>
    <ellipse cx="12" cy="5" rx="8" ry="3" />
    <path d="m4 5 2 14c0 4 12 4 12 0l2-14M6 14c2 3 10 3 12 0" />
    <path d="M10 10h4" />
  </svg>
}
