import { describe, expect, it } from 'vitest'
import { formatDate } from './index'
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
