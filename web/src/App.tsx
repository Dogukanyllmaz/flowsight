import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { FlowSightProvider } from './features/flowsight/data/hooks'
import { createStaticSource } from './features/flowsight/data/staticSource'
import { AppShell } from './features/flowsight/pages/AppShell'
import { useRoute } from './routing'

const queryClient = new QueryClient()
const source = createStaticSource(import.meta.env.BASE_URL)

export default function App() {
  const [route, navigate] = useRoute()
  return (
    <QueryClientProvider client={queryClient}>
      <FlowSightProvider source={source}>
        <AppShell route={route} navigate={navigate} owner={(r) => <div className="fs-page">{r.page} · {r.periodId}</div>} />
      </FlowSightProvider>
    </QueryClientProvider>
  )
}
