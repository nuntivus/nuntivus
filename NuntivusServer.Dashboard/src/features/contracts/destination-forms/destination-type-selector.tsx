import { IntegrationTypePicker } from '../../../components/forms/integration-type-picker'
import type { DestinationType } from '../../../services/api/contracts-api'
import { destinationTypeOptions } from '../../shared/integration-types'

type DestinationTypeSelectorProps = {
  value: DestinationType
  onChange: (value: DestinationType) => void
}

export function DestinationTypeSelector({
  value,
  onChange,
}: DestinationTypeSelectorProps) {
  return (
    <IntegrationTypePicker label="Destination type" options={destinationTypeOptions}
      value={value} onChange={onChange} />
  )
}
