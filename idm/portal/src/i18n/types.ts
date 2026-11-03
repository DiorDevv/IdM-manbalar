import type { uz } from './uz'

/** Matn turlari kengaytiriladi (`as const` literal emas — boshqa tilda boshqa matn), kalitlar va funksiya imzolari saqlanadi. */
type Widen<T> =
  T extends string ? string
  : T extends (...args: infer A) => infer R ? (...args: A) => Widen<R>
  : T extends readonly [unknown, ...unknown[]] ? { [K in keyof T]: Widen<T[K]> }
  : T extends readonly (infer U)[] ? readonly Widen<U>[]
  : T extends object ? { [K in keyof T]: Widen<T[K]> }
  : T

export type Dictionary = Widen<typeof uz>
export type Language = 'uz' | 'ru'
