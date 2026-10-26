// Ichki SVG ikonkalar: 24×24, chiziqli (1.75), tashqi kutubxona va CDN yo'q. Yangi ikonka — shu ro'yxatga path.
const paths: Record<string, string> = {
  dashboard: 'M4 4h6v8H4zM14 4h6v5h-6zM14 13h6v7h-6zM4 16h6v4H4z',
  people: 'M16 20v-1.5a3.5 3.5 0 0 0-3.5-3.5h-5A3.5 3.5 0 0 0 4 18.5V20M10 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7zM20 20v-1.5a3.5 3.5 0 0 0-2.5-3.35M15.5 4.15a3.5 3.5 0 0 1 0 6.7',
  user: 'M19 20v-1.5a3.5 3.5 0 0 0-3.5-3.5h-7A3.5 3.5 0 0 0 5 18.5V20M12 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7z',
  apps: 'M5 4h5a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1zM14 4h5a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1zM5 13h5a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1v-5a1 1 0 0 1 1-1zM16.5 13v7M13 16.5h7',
  role: 'M12 21s7-3.5 7-9V5.5L12 3 5 5.5V12c0 5.5 7 9 7 9zM9 12l2 2 4-4',
  request: 'M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8zM14 3v5h5M9 13h6M9 17h4',
  review: 'M21 11.5V12a9 9 0 1 1-5.3-8.2M21 5l-9 9-3-3',
  audit: 'M4 6.5l1.5 1.5L8 5.5M4 12.5l1.5 1.5L8 11.5M4 18.5l1.5 1.5L8 17.5M11.5 7h8.5M11.5 13h8.5M11.5 19h8.5',
  reports: 'M17 20a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5zM7 9a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5zM7 9v11M17 15V10a3 3 0 0 0-3-3h-3.5M12.5 5l-2 2 2 2',
  events: 'M3 12h4l2.5-6 5 12 2.5-6h4',
  search: 'M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14zM20 20l-4-4',
  logout: 'M9 20H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h3M15.5 16.5 20 12l-4.5-4.5M20 12H9',
  shield: 'M12 21s7-3.5 7-9V5.5L12 3 5 5.5V12c0 5.5 7 9 7 9z',
  warning: 'M10.3 4.2 2.6 17.5A2 2 0 0 0 4.3 20.5h15.4a2 2 0 0 0 1.7-3L13.7 4.2a2 2 0 0 0-3.4 0zM12 9.5v4M12 17h.01',
  clock: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM12 7.5V12l3 2',
  sync: 'M20 11.5A8 8 0 0 0 6.3 6.3L4 8.5M4 4v4.5h4.5M4 12.5a8 8 0 0 0 13.7 5.2l2.3-2.2M20 20v-4.5h-4.5',
  lock: 'M6.5 11h11a1.5 1.5 0 0 1 1.5 1.5v6a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 5 18.5v-6A1.5 1.5 0 0 1 6.5 11zM8 11V8a4 4 0 0 1 8 0v3',
  sun: 'M12 16.5a4.5 4.5 0 1 0 0-9 4.5 4.5 0 0 0 0 9zM12 2.5v2M12 19.5v2M4.6 4.6 6 6M18 18l1.4 1.4M2.5 12h2M19.5 12h2M4.6 19.4 6 18M18 6l1.4-1.4',
  moon: 'M20 14.5A8 8 0 1 1 9.5 4a6.5 6.5 0 0 0 10.5 10.5z',
  monitor: 'M4 5h16a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1zM8.5 21h7M12 17v4',
  menu: 'M4 7h16M4 12h16M4 17h16',
  chevron: 'M9.5 6l6 6-6 6',
  arrow: 'M5 12h14M13 6l6 6-6 6',
  enter: 'M9 10l-4 4 4 4M19 5v6a3 3 0 0 1-3 3H5',
  x: 'M6 6l12 12M18 6 6 18',
  check: 'M5 12.5l4.5 4.5L19 7.5',
  bell: 'M6 8.5a6 6 0 0 1 12 0c0 6 2.5 7.5 2.5 7.5h-17S6 14.5 6 8.5zM10 19.5a2.2 2.2 0 0 0 4 0',
  calendar: 'M5 5h14a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1zM4 10h16M8.5 3v4M15.5 3v4',
  mail: 'M4 6h16a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1zM3.5 7l8.5 6 8.5-6',
  building: 'M5 21V5a1 1 0 0 1 1-1h8a1 1 0 0 1 1 1v16M15 9h3a1 1 0 0 1 1 1v11M3 21h18M8.5 8h3M8.5 12h3M8.5 16h3',
  briefcase: 'M4 8h16a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V9a1 1 0 0 1 1-1zM9 8V6a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2M3 13h18',
  hash: 'M5 9h15M4 15h15M10 3 8 21M16 3l-2 18',
  sparkle: 'M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8zM19 16l.8 2.2L22 19l-2.2.8L19 22l-.8-2.2L16 19l2.2-.8z',
}

export type IconName = keyof typeof paths

export function Icon({ name, size = 18, strokeWidth = 1.75 }: { name: IconName | string; size?: number; strokeWidth?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={strokeWidth}
      strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      <path d={paths[name] ?? ''} />
    </svg>
  )
}

export function BrandMark() {
  return (
    <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#fff" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M12 2.8 4.5 5.6v5.9c0 4.6 3.1 8.6 7.5 9.7 4.4-1.1 7.5-5.1 7.5-9.7V5.6L12 2.8Z" /><circle cx="12" cy="10.2" r="2.3" /><path d="M12 12.5v4.1M12 14.6h1.7" />
    </svg>
  )
}
