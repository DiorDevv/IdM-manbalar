import { describe, expect, it } from 'vitest'
import { describeAction, describeChanges, formatDate, isQuietSync, t } from './index'
import { query } from '../api/query'

describe('formatlash', () => {
  it('bo\'sh qiymat uchun tire qaytaradi', () => {
    expect(formatDate(null)).toBe('—')
  })
  it('kun.oy.yil formatida', () => {
    expect(formatDate('2026-03-05T10:00:00')).toBe('05.03.2026')
  })
})

describe('query', () => {
  it('bo\'sh parametrlarni tashlab yuboradi va kodlaydi', () => {
    expect(query({ search: "bo'lim", status: '', skip: 0, take: 25 })).toBe("?search=bo%27lim&skip=0&take=25")
  })
})

describe('sinxronlash matnlari', () => {
  const run = { accountsCreated: 0, accountsUpdated: 0, accountsDisabled: 0, accountsDeleted: 0, membershipsAdded: 0, membershipsRemoved: 0, identitiesCreated: 0 }
  it('o\'zgarish bo\'lmasa shuni aytadi', () => {
    expect(t.applications.runChanges(run)).toBe("o'zgarish yo'q")
  })
  it('faqat nolga teng bo\'lmagan o\'zgarishlarni sanaydi', () => {
    expect(t.applications.runChanges({ ...run, accountsCreated: 2, accountsDisabled: 1, membershipsRemoved: 3 }))
      .toBe("+2 yangi · 1 bloklangan · a'zolik +0/−3")
  })
  it('davomiylik va vaqt oralig\'i', () => {
    expect(t.duration(850)).toBe('850 ms')
    expect(t.duration(12_300)).toBe('12.3 s')
    expect(t.ago(new Date(Date.now() - 5 * 60_000).toISOString())).toBe('5 daqiqa oldin')
    expect(t.ago(null)).toBe('hech qachon')
  })
})

describe('JML o\'zgarishlari', () => {
  it('JSON o\'zgarishlarni o\'qiladigan qatorlarga aylantiradi', () => {
    expect(describeChanges('{"department":{"from":"IT","to":"Risk"},"managerId":{"from":null,"to":"x"}}'))
      .toEqual(['department: IT → Risk', 'managerId: — → x'])
    expect(describeChanges(null)).toEqual([])
    expect(describeChanges('buzilgan')).toEqual(['buzilgan'])
  })
})

describe('audit amallari', () => {
  it('amalni odam tilida va PascalCase tafsilotlardan xulosa bilan beradi', () => {
    const d = describeAction('connector.sync-completed', '{"application":"AD","AccountsRead":8,"AccountsCreated":2,"MembershipsAdded":1}')
    expect(d.label).toBe('Sinxronlash yakunlandi')
    expect(d.summary).toBe("AD · 8 akkaunt · 3 o'zgarish")
    expect(describeAction('nomalum.amal', null).label).toBe('nomalum.amal')
  })
  it('o\'zgarishsiz sinxronlashni shovqin deb biladi', () => {
    expect(isQuietSync('connector.sync-completed', '{"AccountsRead":8,"AccountsCreated":0,"warnings":0}')).toBe(true)
    expect(isQuietSync('connector.sync-completed', '{"AccountsRead":8,"AccountsDeleted":1}')).toBe(false)
    expect(isQuietSync('account.disabled', null)).toBe(false)
  })
})
