import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, query } from '../api/client'
import type { EventKind, IdentityEventItem, PagedResult } from '../api/types'
import { describeChanges, formatDateTime, t } from '../i18n'
import { Empty, ErrorBox, PageHead, Pager, Person, SkeletonRows } from '../components/ui'

const PAGE = 25
const kinds: EventKind[] = ['Joiner', 'Mover', 'Leaver', 'Rehire', 'Suspended', 'Resumed']
const tone: Record<EventKind, string> = { Joiner: 'ok', Mover: 'info', Leaver: 'bad', Rehire: 'ok', Suspended: 'warn', Resumed: 'info' }
const e = t.events

export function EventsPage() {
  const [kind, setKind] = useState<EventKind | ''>('')
  const [skip, setSkip] = useState(0)
  const list = useQuery({
    queryKey: ['events', kind, skip],
    queryFn: () => api<PagedResult<IdentityEventItem>>(`/api/events${query({ kind, skip, take: PAGE })}`),
    placeholderData: keepPreviousData,
  })
  return (
    <>
      <PageHead title={e.title} subtitle={e.subtitle} />
      <section className="card">
        <div className="toolbar">
          <select className="select" value={kind} onChange={x => { setKind(x.target.value as EventKind | ''); setSkip(0) }} aria-label={e.colKind}>
            <option value="">{e.all}</option>
            {kinds.map(k => <option key={k} value={k}>{e.kind[k]}</option>)}
          </select>
        </div>
        {list.isError ? <ErrorBox error={list.error} onRetry={() => { void list.refetch() }} /> : (
          <>
            <table className="data">
              <thead><tr><th>{e.colWhen}</th><th>{e.colKind}</th><th>{t.identities.colName}</th><th>{e.colChanges}</th></tr></thead>
              {list.isPending ? <SkeletonRows cols={4} /> : (
                <tbody>
                  {list.data?.items.map(x => (
                    <tr key={x.id}>
                      <td style={{ whiteSpace: 'nowrap' }}>{formatDateTime(x.occurredAt)}<div className="cell-sub">{x.source}</div></td>
                      <td><span className={`badge ${tone[x.kind]}`}>{e.kind[x.kind]}</span></td>
                      <td><Person name={x.identityName} sub={<><span className="mono">{x.employeeNumber}</span>{x.department ? ` · ${x.department}` : ''}</>} to={`/identities/${x.identityId}`} /></td>
                      <td className="cell-sub" style={{ maxWidth: 480 }}>{describeChanges(x.details).map(line => <div key={line} style={{ overflowWrap: 'anywhere' }}>{line}</div>)}</td>
                    </tr>
                  ))}
                </tbody>
              )}
            </table>
            {list.data?.total === 0 && <Empty icon="clock" title={e.empty} text={e.emptyText} />}
            {list.data && <Pager skip={skip} page={PAGE} total={list.data.total} onChange={setSkip} />}
          </>
        )}
      </section>
    </>
  )
}
