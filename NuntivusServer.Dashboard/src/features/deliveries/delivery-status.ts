import type { DeliveryStatus } from '../../services/api/deliveries-api'

const deliveryStatusNames: Record<number, string> = {
    0: 'Pending',
    1: 'InFlight',
    2: 'Delivered',
    3: 'Failed',
    4: 'Dead',
}

export function formatDeliveryStatus(status: DeliveryStatus) {
    return typeof status === 'number'
        ? deliveryStatusNames[status] ?? String(status)
        : status
}