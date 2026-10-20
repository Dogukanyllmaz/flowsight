import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { FlowSightProvider } from './features/flowsight/data/hooks'
import { createStaticSource } from './features/flowsight/data/staticSource'
import { AppShell } from './features/flowsight/pages/AppShell'
import { FlowOverview } from './features/flowsight/pages/FlowOverview'
import { useRoute } from './routing'

const queryClient = new QueryClient()
const source = createStaticSource(import.meta.env.BASE_URL)

export default function App() {
  const [route, navigate] = useRoute()
  return (
    <QueryClientProvider client={queryClient}>
      <FlowSightProvider source={source}>
        <AppShell route={route} navigate={navigate} owner={(r) =>
            r.page === 'flow' ? (
              <FlowOverview periodId={r.periodId} onPeriodChange={(periodId) => navigate({ page: 'flow', periodId })} />
            ) : (
              <div className="fs-page">{r.page} · {r.periodId}</div>
            )
          } />
      </FlowSightProvider>
    </QueryClientProvider>
  )
}
