import {
  Alert,
  Badge,
  Button,
  Card,
  Group,
  MultiSelect,
  ScrollArea,
  Select,
  Stack,
  Switch,
  Text,
  Textarea,
  TextInput,
} from '@mantine/core'
import { DateTimePicker } from '@mantine/dates'
import { modals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import useSWR from 'swr'
import { choiceError } from '@Utils/ChoiceApi'
import {
  beijingDateTime,
  BlackoutInput,
  BlackoutModel,
  gameTimeRequest,
  GameTimeSettingsModel,
  minuteTime,
  parseBeijingDateTime,
  parseMinuteTime,
  ScheduledTaskInput,
  ScheduledTaskModel,
} from '@Utils/GameTimeApi'
import { useEditChallenges } from '@Hooks/useEdit'
import { useAdminGame } from '@Hooks/useGame'

export const GameTimeSettings = ({ gameId, kind }: { gameId: number; kind: 'tasks' | 'blackouts' }) => {
  const { t } = useTranslation()
  const { game } = useAdminGame(gameId)
  const { challenges } = useEditChallenges(gameId)
  const { data, error, mutate } = useSWR<GameTimeSettingsModel>(
    `/api/edit/games/${gameId}/time`,
    () => gameTimeRequest(gameId),
    { refreshInterval: 10000 }
  )
  const [editingId, setEditingId] = useState<number | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState('')
  const newTask = (): ScheduledTaskInput => ({
    name: '',
    enabled: true,
    atGameStart: false,
    executeAtUtc: Math.ceil((Date.now() + 300000) / 60000) * 60000,
    challengeIds: [],
    enableChallenges: true,
    notify: true,
    message: t('gameTime.default_task_message'),
  })
  const newBlackout = (): BlackoutInput => ({
    name: '',
    enabled: true,
    daily: false,
    startAtUtc: Math.ceil((Date.now() + 300000) / 60000) * 60000,
    endAtUtc: Math.ceil((Date.now() + 3600000) / 60000) * 60000,
    startMinute: 12 * 60,
    endMinute: 13 * 60,
    notify: true,
    message: t('gameTime.default_blackout_message'),
    endMessage: t('gameTime.default_blackout_end_message'),
  })
  const [task, setTask] = useState(newTask)
  const [blackout, setBlackout] = useState(newBlackout)
  const isTask = kind === 'tasks'
  const draft = isTask ? task : blackout
  const reset = () => {
    setEditingId(null)
    setTask(newTask())
    setBlackout(newBlackout())
    setFailure('')
  }
  const save = async () => {
    setBusy(true)
    setFailure('')
    try {
      await gameTimeRequest(gameId, `/${kind}${editingId ? `/${editingId}` : ''}`, editingId ? 'PUT' : 'POST', draft)
      reset()
      await mutate()
      showNotification({ color: 'teal', message: t('gameTime.saved') })
    } catch (e) {
      setFailure(choiceError(e))
    } finally {
      setBusy(false)
    }
  }
  const remove = (id: number) =>
    modals.openConfirmModal({
      title: t('gameTime.delete'),
      children: <Text>{t('gameTime.delete_confirm')}</Text>,
      confirmProps: { color: 'red' },
      onConfirm: async () => {
        setBusy(true)
        try {
          await gameTimeRequest(gameId, `/${kind}/${id}`, 'DELETE')
          if (id === editingId) reset()
          await mutate()
        } catch (e) {
          setFailure(choiceError(e))
        } finally {
          setBusy(false)
        }
      },
    })
  const edit = (row: ScheduledTaskModel | BlackoutModel) => {
    setEditingId(row.id)
    setFailure('')
    if (isTask) setTask({ ...row } as ScheduledTaskInput)
    else setBlackout({ ...row } as BlackoutInput)
  }
  const updateCommon = (patch: Partial<Pick<ScheduledTaskInput, 'name' | 'enabled' | 'notify' | 'message'>>) =>
    isTask ? setTask({ ...task, ...patch }) : setBlackout({ ...blackout, ...patch })
  const rows = isTask ? data?.tasks : data?.blackouts
  const timestamp = (value: number | null) => beijingDateTime(value).replace('T', ' ')
  const pickerValue = (value: number | null) => (value === null ? null : timestamp(value))
  return (
    <ScrollArea h="calc(100vh - 180px)" offsetScrollbars>
      <Stack maw="65rem" p="md">
        <Alert>{t(isTask ? 'gameTime.task_description' : 'gameTime.blackout_description')}</Alert>
        <Text size="sm" c="dimmed">
          {t('gameTime.timezone')}
          {game && ` · ${timestamp(game.start ?? null)} — ${timestamp(game.end ?? null)}`}
        </Text>
        {(error || failure) && <Alert color="red">{failure || choiceError(error)}</Alert>}
        <Card withBorder>
          <Stack>
            <Text fw={600}>{t(editingId ? 'gameTime.edit' : 'gameTime.create')}</Text>
            <TextInput
              required
              maxLength={100}
              label={t('gameTime.name')}
              value={draft.name}
              disabled={busy}
              onChange={(e) => updateCommon({ name: e.currentTarget.value })}
            />
            <Switch
              label={t('gameTime.enabled')}
              checked={draft.enabled}
              disabled={busy}
              onChange={(e) => updateCommon({ enabled: e.currentTarget.checked })}
            />
            {isTask ? (
              <>
                <Select
                  label={t('gameTime.trigger')}
                  allowDeselect={false}
                  value={task.atGameStart ? 'start' : 'time'}
                  data={[
                    { value: 'start', label: t('gameTime.at_start') },
                    { value: 'time', label: t('gameTime.at_time') },
                  ]}
                  disabled={busy}
                  onChange={(value) => setTask({ ...task, atGameStart: value === 'start' })}
                />
                {!task.atGameStart && (
                  <DateTimePicker
                    label={t('gameTime.execute_at')}
                    size="sm"
                    valueFormat="L LT"
                    clearable={false}
                    required
                    value={pickerValue(task.executeAtUtc)}
                    disabled={busy}
                    onChange={(value) => setTask({ ...task, executeAtUtc: value ? parseBeijingDateTime(value) : null })}
                  />
                )}
                <Select
                  label={t('gameTime.action')}
                  allowDeselect={false}
                  value={task.enableChallenges ? 'enable' : 'disable'}
                  data={[
                    { value: 'enable', label: t('gameTime.enable_challenges') },
                    { value: 'disable', label: t('gameTime.disable_challenges') },
                  ]}
                  disabled={busy}
                  onChange={(value) => setTask({ ...task, enableChallenges: value === 'enable' })}
                />
                <MultiSelect
                  label={t('gameTime.challenges')}
                  required
                  searchable
                  clearable
                  data={(challenges ?? []).map((c) => ({ value: String(c.id), label: `#${c.id} ${c.title}` }))}
                  value={task.challengeIds.map(String)}
                  disabled={busy || !challenges}
                  onChange={(ids) => setTask({ ...task, challengeIds: ids.map(Number) })}
                />
              </>
            ) : (
              <>
                <Select
                  label={t('gameTime.recurrence')}
                  allowDeselect={false}
                  value={blackout.daily ? 'daily' : 'once'}
                  data={[
                    { value: 'once', label: t('gameTime.once') },
                    { value: 'daily', label: t('gameTime.daily') },
                  ]}
                  disabled={busy}
                  onChange={(value) => setBlackout({ ...blackout, daily: value === 'daily' })}
                />
                {blackout.daily ? (
                  <Group grow>
                    <TextInput
                      type="time"
                      label={t('gameTime.start')}
                      required
                      value={minuteTime(blackout.startMinute)}
                      disabled={busy}
                      onChange={(e) =>
                        e.currentTarget.value &&
                        setBlackout({ ...blackout, startMinute: parseMinuteTime(e.currentTarget.value) })
                      }
                    />
                    <TextInput
                      type="time"
                      label={t('gameTime.end')}
                      required
                      value={minuteTime(blackout.endMinute)}
                      disabled={busy}
                      onChange={(e) =>
                        e.currentTarget.value &&
                        setBlackout({ ...blackout, endMinute: parseMinuteTime(e.currentTarget.value) })
                      }
                    />
                  </Group>
                ) : (
                  <Group grow>
                    <DateTimePicker
                      label={t('gameTime.start')}
                      size="sm"
                      valueFormat="L LT"
                      clearable={false}
                      required
                      value={pickerValue(blackout.startAtUtc)}
                      disabled={busy}
                      onChange={(value) =>
                        setBlackout({ ...blackout, startAtUtc: value ? parseBeijingDateTime(value) : null })
                      }
                    />
                    <DateTimePicker
                      label={t('gameTime.end')}
                      size="sm"
                      valueFormat="L LT"
                      clearable={false}
                      required
                      value={pickerValue(blackout.endAtUtc)}
                      disabled={busy}
                      onChange={(value) =>
                        setBlackout({ ...blackout, endAtUtc: value ? parseBeijingDateTime(value) : null })
                      }
                    />
                  </Group>
                )}
              </>
            )}
            <Switch
              label={t(isTask ? 'gameTime.notify' : 'gameTime.notify_blackout')}
              checked={draft.notify}
              disabled={busy}
              onChange={(e) => updateCommon({ notify: e.currentTarget.checked })}
            />
            <Textarea
              label={t(isTask ? 'gameTime.message' : 'gameTime.start_message')}
              description={t('gameTime.notify_description')}
              required={draft.notify}
              maxLength={4000}
              autosize
              minRows={2}
              disabled={busy}
              value={draft.message}
              onChange={(e) => updateCommon({ message: e.currentTarget.value })}
            />
            {!isTask && (
              <Textarea
                label={t('gameTime.end_message')}
                description={t('gameTime.end_notify_description')}
                required={blackout.notify}
                maxLength={4000}
                autosize
                minRows={2}
                disabled={busy}
                value={blackout.endMessage}
                onChange={(e) => setBlackout({ ...blackout, endMessage: e.currentTarget.value })}
              />
            )}
            <Group justify="flex-end">
              <Button variant="default" disabled={busy} onClick={reset}>
                {t('gameTime.reset')}
              </Button>
              <Button
                disabled={busy || !data || !draft.name.trim() || (isTask && !task.challengeIds.length)}
                onClick={() => void save()}
              >
                {t('admin.button.save')}
              </Button>
            </Group>
          </Stack>
        </Card>
        <Group justify="space-between">
          <Text fw={600}>{t('gameTime.configured')}</Text>
          <Button variant="subtle" onClick={() => void mutate()}>
            {t('gameTime.refresh')}
          </Button>
        </Group>
        {rows?.length === 0 && <Text c="dimmed">{t('gameTime.empty')}</Text>}
        {rows?.map((row) => (
          <Card withBorder key={row.id}>
            <Stack gap="xs">
              <Group justify="space-between">
                <Text fw={600}>{row.name}</Text>
                <Badge>
                  {'executedAtUtc' in row && row.executedAtUtc
                    ? t('gameTime.executed')
                    : t(row.enabled ? 'gameTime.enabled' : 'gameTime.disabled')}
                </Badge>
              </Group>
              {'challengeIds' in row ? (
                <>
                  <Text size="sm">
                    {row.atGameStart ? t('gameTime.at_start') : timestamp(row.executeAtUtc)} ·{' '}
                    {t(row.enableChallenges ? 'gameTime.enable_challenges' : 'gameTime.disable_challenges')}
                  </Text>
                  <Text size="sm">
                    {row.challengeIds.map((id) => challenges?.find((c) => c.id === id)?.title ?? `#${id}`).join('、')}
                  </Text>
                  {row.executedAtUtc && (
                    <Text size="xs" c="dimmed">
                      {t('gameTime.executed')}：{timestamp(row.executedAtUtc)}
                    </Text>
                  )}
                  {row.lastError && <Alert color="red">{row.lastError}</Alert>}
                </>
              ) : (
                <Text size="sm">
                  {row.daily
                    ? `${t('gameTime.daily')} ${minuteTime(row.startMinute)} — ${minuteTime(row.endMinute)}`
                    : `${timestamp(row.startAtUtc)} — ${timestamp(row.endAtUtc)}`}
                </Text>
              )}
              {row.notify && (
                <Text size="sm" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
                  {'endMessage' in row && `${t('gameTime.start_message')}：`}
                  {row.message}
                </Text>
              )}
              {'endMessage' in row && row.notify && (
                <Text size="sm" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
                  {t('gameTime.end_message')}：{row.endMessage}
                </Text>
              )}
              <Group justify="flex-end">
                <Button
                  variant="light"
                  disabled={busy || ('executedAtUtc' in row && !!row.executedAtUtc)}
                  onClick={() => edit(row)}
                >
                  {t('gameTime.edit')}
                </Button>
                <Button variant="subtle" color="red" disabled={busy} onClick={() => remove(row.id)}>
                  {t('gameTime.delete')}
                </Button>
              </Group>
            </Stack>
          </Card>
        ))}
      </Stack>
    </ScrollArea>
  )
}
