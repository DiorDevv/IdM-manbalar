// Ichki SVG ikonkalar (tashqi kutubxona va CDN yo'q).
const paths: Record<string, string> = {
  dashboard: 'M4 13h6V4H4v9Zm0 7h6v-5H4v5Zm10 0h6v-9h-6v9Zm0-16v5h6V4h-6Z',
  people: 'M16 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM8 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm0 2c-2.7 0-6 1.3-6 3.5V19h12v-2.5C14 14.3 10.7 13 8 13Zm8 0c-.4 0-.8 0-1.2.1 1.3.9 2.2 2 2.2 3.4V19h5v-2.5c0-2.2-3.3-3.5-6-3.5Z',
  apps: 'M4 4h6v6H4V4Zm10 0h6v6h-6V4ZM4 14h6v6H4v-6Zm10 0h6v6h-6v-6Z',
  role: 'M12 2 4 5v6c0 5 3.4 9.3 8 10.5 4.6-1.2 8-5.5 8-10.5V5l-8-3Zm0 5a2.5 2.5 0 1 1 0 5 2.5 2.5 0 0 1 0-5Zm0 12.2c-2-.6-3.7-1.9-4.7-3.6.5-1.4 3.1-2.1 4.7-2.1s4.2.7 4.7 2.1c-1 1.7-2.7 3-4.7 3.6Z',
  request: 'M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6Zm-1 7V3.5L18.5 9H13Zm-5 4h8v2H8v-2Zm0 4h5v2H8v-2Z',
  review: 'M9 16.2 4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4L9 16.2Z',
  audit: 'M19 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V5a2 2 0 0 0-2-2Zm-9 14-4-4 1.4-1.4 2.6 2.6 6.6-6.6L18 9l-8 8Z',
  search: 'M15.5 14h-.8l-.3-.3A6.5 6.5 0 1 0 9.5 16a6.5 6.5 0 0 0 4.2-1.6l.3.3v.8l5 5 1.5-1.5-5-5Zm-6 0A4.5 4.5 0 1 1 14 9.5 4.5 4.5 0 0 1 9.5 14Z',
  logout: 'M16 17v-3H9v-4h7V7l5 5-5 5ZM14 2a2 2 0 0 1 2 2v2h-2V4H5v16h9v-2h2v2a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9Z',
  shield: 'M12 2 4 5v6c0 5 3.4 9.3 8 10.5 4.6-1.2 8-5.5 8-10.5V5l-8-3Zm-1.2 14L7 12.2l1.4-1.4 2.4 2.4 4.8-4.8L17 9.8 10.8 16Z',
  warning: 'M1 21h22L12 2 1 21Zm12-3h-2v-2h2v2Zm0-4h-2v-4h2v4Z',
  clock: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm.5 5H11v6l5.2 3.2.8-1.3-4.5-2.7V7Z',
}

export function Icon({ name, size = 20 }: { name: keyof typeof paths | string; size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d={paths[name] ?? ''} />
    </svg>
  )
}

export function BrandMark() {
  return (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#fff" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M12 2.8 4.5 5.6v5.9c0 4.6 3.1 8.6 7.5 9.7 4.4-1.1 7.5-5.1 7.5-9.7V5.6L12 2.8Z" /><circle cx="12" cy="10.2" r="2.3" /><path d="M12 12.5v4.1M12 14.6h1.7" />
    </svg>
  )
}
