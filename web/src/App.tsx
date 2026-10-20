import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { FlowSightProvider } from './features/flowsight/data/hooks'
import { createStaticSource } from './features/flowsight/data/staticSource'
import { RequestTracker } from './features/flowsight/pages/RequestTracker'

const queryClient = new QueryClient()
const source = createStaticSource(import.meta.env.BASE_URL)

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <FlowSightProvider source={source}>
        <RequestTracker />
      </FlowSightProvider>
    </QueryClientProvider>
  )
}
