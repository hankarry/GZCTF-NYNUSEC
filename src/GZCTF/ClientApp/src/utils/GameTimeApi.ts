import api, { ContentType } from '@Api'

export interface ScheduledTaskInput {
  name: string
  enabled: boolean
  atGameStart: boolean
  executeAtUtc: number | null
  challengeIds: number[]
  enableChallenges: boolean
  notify: boolean
  message: string
}
export interface ScheduledTaskModel extends ScheduledTaskInput {
  id: number
  executedAtUtc: number | null
  lastError: string | null
}
export interface BlackoutInput {
  name: string
  enabled: boolean
  daily: boolean
  startAtUtc: number | null
  endAtUtc: number | null
  startMinute: number
  endMinute: number
  notify: boolean
  message: string
  endMessage: string
}
export interface BlackoutModel extends BlackoutInput {
  id: number
}
export interface GameTimeSettingsModel {
  tasks: ScheduledTaskModel[]
  blackouts: BlackoutModel[]
}
export interface GamePlayStatus {
  serverTime: number
  isBlocked: boolean
  windows: { start: number; end: number }[]
}

export const gameTimeRequest = async <T>(gameId: number, path = '', method = 'GET', body?: unknown) =>
  (
    await api.request<T>({
      path: `/api/edit/games/${gameId}/time${path}`,
      method,
      body,
      type: ContentType.Json,
      format: 'json',
    })
  ).data

export const beijingDateTime = (value: number | null) =>
  value === null ? '' : new Date(value + 8 * 3600000).toISOString().slice(0, 16)
export const parseBeijingDateTime = (value: string) => (value ? Date.parse(`${value.replace(' ', 'T')}+08:00`) : null)
export const minuteTime = (value: number) =>
  `${Math.floor(value / 60)
    .toString()
    .padStart(2, '0')}:${(value % 60).toString().padStart(2, '0')}`
export const parseMinuteTime = (value: string) => {
  const [hour, minute] = value.split(':').map(Number)
  return hour * 60 + minute
}
