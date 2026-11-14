import { Button, Flex } from '@radix-ui/themes'
import { NavLink, useNavigate } from 'react-router-dom'
import { DestinationsIcon } from '../components/icons/nav-icons'
import { DetailPageHeader } from '../components/states/detail-page-header'
import { DestinationCreateForm } from '../features/destinations/destination-create-form'

export function DestinationCreatePage() {
  const navigate = useNavigate()

  return <Flex direction="column" gap="4" className="detail-page destination-detail-page">
    <DetailPageHeader title="Add Destination" description="Register a Destination and configure where it delivers events." icon={DestinationsIcon}
      actions={<Button asChild variant="soft"><NavLink to="/destinations">Back to Destinations</NavLink></Button>} />
    <DestinationCreateForm onSaved={destination => navigate(`/destinations/${encodeURIComponent(destination.id)}`)}
      onCancel={() => navigate('/destinations')} />
  </Flex>
}
