import { Card, Flex, Heading, Text } from '@radix-ui/themes'
import type { DashboardTimeBucket } from '../../services/api/dashboard-api'

type MetricKey = 'events' | 'deliveries' | 'delivered' | 'failed' | 'dead'

interface ChartSeries {
  key: MetricKey
  label: string
  color: string
}

interface DashboardTimeChartProps {
  title: string
  buckets: DashboardTimeBucket[]
  series: ChartSeries[]
  loading: boolean
  error?: string
}

export function DashboardTimeChart({ title, buckets, series, loading, error }: DashboardTimeChartProps) {
  const max = Math.max(1, ...buckets.flatMap((bucket) => series.map((item) => bucket[item.key])))
  const left = 46
  const right = 620
  const top = 12
  const bottom = 172
  const x = (index: number) => left + (index / Math.max(1, buckets.length - 1)) * (right - left)
  const y = (value: number) => bottom - (value / max) * (bottom - top)

  return (
    <Card>
      <Flex direction="column" gap="3">
        <Heading size="4">{title}</Heading>
        {loading && buckets.length === 0 ? (
          <Text size="2" color="gray">Loading chart...</Text>
        ) : error ? (
          <Text size="2" color="red">{error}</Text>
        ) : buckets.length === 0 ? (
          <Text size="2" color="gray">No data available.</Text>
        ) : (
          <>
            <div className="dashboard-chart">
              <svg viewBox="0 0 640 215" role="img" aria-label={`${title}. ${series.map((item) => `${item.label}: ${buckets.reduce((sum, bucket) => sum + bucket[item.key], 0)}`).join(', ')} in the selected window.`}>
                {[0, 0.5, 1].map((fraction) => (
                  <g key={fraction}>
                    <line x1={left} x2={right} y1={y(max * fraction)} y2={y(max * fraction)} className="dashboard-chart-grid" />
                    <text x={left - 8} y={y(max * fraction) + 4} textAnchor="end" className="dashboard-chart-label">
                      {Math.round(max * fraction)}
                    </text>
                  </g>
                ))}
                {series.map((item) => (
                  <polyline
                    key={item.key}
                    fill="none"
                    stroke={item.color}
                    strokeWidth="2.5"
                    strokeLinejoin="round"
                    strokeLinecap="round"
                    points={buckets.map((bucket, index) => `${x(index)},${y(bucket[item.key])}`).join(' ')}
                  />
                ))}
                <text x={left} y="202" className="dashboard-chart-label">
                  {new Date(buckets[0].start).toLocaleTimeString()}
                </text>
                <text x={right} y="202" textAnchor="end" className="dashboard-chart-label">
                  {new Date(buckets[buckets.length - 1].start).toLocaleTimeString()}
                </text>
              </svg>
            </div>
            <Flex gap="3" wrap="wrap">
              {series.map((item) => (
                <Flex key={item.key} align="center" gap="1">
                  <span className="dashboard-chart-swatch" style={{ backgroundColor: item.color }} />
                  <Text size="2" color="gray">{item.label}</Text>
                </Flex>
              ))}
            </Flex>
            {buckets.every((bucket) => series.every((item) => bucket[item.key] === 0)) ? (
              <Text size="2" color="gray">No activity in the selected window.</Text>
            ) : null}
          </>
        )}
      </Flex>
    </Card>
  )
}
