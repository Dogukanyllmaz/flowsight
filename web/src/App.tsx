import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { FlowSightProvider } from './features/flowsight/data/hooks'
import { createStaticSource } from './features/flowsight/data/staticSource'
import { AppShell } from './features/flowsight/pages/AppShell'
import { FlowOverview } from './features/flowsight/pages/FlowOverview'
import { BeforeAfter } from './features/flowsight/pages/BeforeAfter'
import { ImprovementCards } from './features/flowsight/pages/ImprovementCards'
import { StageDetail } from './features/flowsight/pages/StageDetail'
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
            ) : r.page === 'stage' ? (
              <StageDetail periodId={r.periodId} stage={r.stage} />
            ) : r.page === 'changes' ? (
              <BeforeAfter index={r.index} />
            ) : (
              <ImprovementCards
                periodId={r.periodId}
                cardId={r.cardId}
                onPeriodChange={(periodId) => navigate({ page: 'cards', periodId })}
              />
            )
          } />
      </FlowSightProvider>
    </QueryClientProvider>
  )
}
