import { Button, Flex } from '@radix-ui/themes'
import { NavLink, useNavigate } from 'react-router-dom'
import { EventsIcon } from '../components/icons/nav-icons'
import { DetailPageHeader } from '../components/states/detail-page-header'
import { SourceForm } from '../features/sources/source-form'

export function SourceCreatePage() {
  const navigate = useNavigate()

  return <Flex direction="column" gap="4" className="detail-page destination-detail-page">
    <DetailPageHeader title="Add Source" description="Register a Source and configure how it receives events." icon={EventsIcon}
      actions={<Button asChild variant="soft"><NavLink to="/sources">Back to Sources</NavLink></Button>} />
    <SourceForm onSaved={source => navigate(`/sources/${encodeURIComponent(source.id)}`)}
      onCancel={() => navigate('/sources')} />
  </Flex>
}
