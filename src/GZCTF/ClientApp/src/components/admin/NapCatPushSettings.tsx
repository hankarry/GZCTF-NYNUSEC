import {
  Alert,
  Badge,
  Button,
  Checkbox,
  Divider,
  Group,
  PasswordInput,
  Stack,
  Switch,
  Text,
  TextInput,
  Textarea,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import dayjs from 'dayjs'
import { FC, useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { showErrorMsg } from '@Utils/Shared'
import api, {
  AnswerResult,
  GamePushConfigModel,
  GamePushDeliveryModel,
  GamePushSettingsModel,
  NoticeType,
  PushDeliveryStatus,
} from '@Api'

interface NapCatPushSettingsProps {
  gameId: number
}

const defaultSettings: GamePushSettingsModel = {
  enabled: false,
  endpointUrl: '',
  groupId: '',
  events: Object.values(NoticeType),
  submissionEvents: [],
  submissionTemplate: '【{game}】[提交:{result}] 团队「{team}」成员「{member}」题目「{challenge}」{solve_rank}',
  noticeTemplate: '【{game}】\n{content}\n{time} (UTC+8)',
}

const submissionResults = [
  AnswerResult.Accepted,
  AnswerResult.WrongAnswer,
  AnswerResult.CheatDetected,
  AnswerResult.NotFound,
]

const previewTemplate = (template: string, values: Record<string, string>) =>
  template.replace(/\{\{|\}\}|\{[a-z_]+\}/g, (token) =>
    token === '{{' ? '{' : token === '}}' ? '}' : (values[token.slice(1, -1)] ?? '')
  )

export const NapCatPushSettings: FC<NapCatPushSettingsProps> = ({ gameId }) => {
  const { t } = useTranslation()
  const [settings, setSettings] = useState<GamePushSettingsModel>(defaultSettings)
  const [hasToken, setHasToken] = useState(false)
  const [busy, setBusy] = useState(false)
  const [loadedGameId, setLoadedGameId] = useState<number | null>(null)
  const loaded = loadedGameId === gameId
  const [deliveries, setDeliveries] = useState<GamePushDeliveryModel[]>([])

  const eventLabels: Record<NoticeType, string> = {
    [NoticeType.FirstBlood]: t('challenge.bonus.first_blood'),
    [NoticeType.SecondBlood]: t('challenge.bonus.second_blood'),
    [NoticeType.ThirdBlood]: t('challenge.bonus.third_blood'),
    [NoticeType.NewChallenge]: t('admin.push.events.new_challenge'),
    [NoticeType.ChallengeClosed]: t('admin.push.events.challenge_closed'),
    [NoticeType.NewHint]: t('admin.push.events.new_hint'),
    [NoticeType.Normal]: t('admin.push.events.normal'),
  }
  const submissionLabels: Record<string, string> = {
    [AnswerResult.Accepted]: t('admin.push.results.accepted'),
    [AnswerResult.WrongAnswer]: t('admin.push.results.wrong_answer'),
    [AnswerResult.CheatDetected]: t('admin.push.results.cheat_detected'),
    [AnswerResult.NotFound]: t('admin.push.results.not_found'),
  }
  const previewValues = {
    game: 'NYNUCTF-S7',
    event: t('admin.push.submission_event'),
    result: '通过 ✅',
    team: t('admin.push.sample_team'),
    member: t('admin.push.sample_member'),
    challenge: t('admin.push.sample_challenge'),
    solve_rank: '🥇 一血',
    blood: '🥇 一血',
    rank: '1',
    submission_id: '123',
    time: dayjs().format('YYYY-MM-DD HH:mm:ss'),
    content: t('admin.push.sample_notice'),
  }

  const refreshDeliveries = useCallback(async () => {
    const response = await api.gamePush.gamePushGetDeliveries(gameId)
    setDeliveries(response.data)
  }, [gameId])

  useEffect(() => {
    let cancelled = false
    Promise.all([api.gamePush.gamePushGetSettings(gameId), api.gamePush.gamePushGetDeliveries(gameId)])
      .then(([config, history]) => {
        if (cancelled) return
        setSettings({ ...config.data, accessToken: '', clearAccessToken: false })
        setHasToken(config.data.hasAccessToken)
        setDeliveries(history.data)
        setLoadedGameId(gameId)
      })
      .catch((error) => {
        if (!cancelled) showErrorMsg(error, t)
      })
    return () => {
      cancelled = true
    }
  }, [gameId, t])

  const onSave = async (test: boolean) => {
    setBusy(true)
    try {
      const response = await api.gamePush.gamePushUpdateSettings(gameId, {
        ...settings,
        endpointUrl: settings.endpointUrl.trim(),
        groupId: settings.groupId.trim(),
      })
      const config: GamePushConfigModel = response.data
      setSettings({ ...config, accessToken: '', clearAccessToken: false })
      setHasToken(config.hasAccessToken)
      if (test) await api.gamePush.gamePushTest(gameId)
      showNotification({ color: 'teal', message: t(test ? 'admin.push.test_sent' : 'admin.push.saved') })
      await refreshDeliveries()
    } catch (error) {
      showErrorMsg(error, t)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Stack>
      <Text size="sm" c="dimmed">
        {t('admin.push.description')}
      </Text>
      <Switch
        label={t('admin.push.enabled')}
        checked={settings.enabled}
        disabled={busy || !loaded}
        onChange={(event) => setSettings({ ...settings, enabled: event.currentTarget.checked })}
      />
      <TextInput
        label={t('admin.push.endpoint')}
        description={t('admin.push.endpoint_description')}
        placeholder="http://napcat:3000"
        required={settings.enabled}
        maxLength={2048}
        value={settings.endpointUrl}
        disabled={busy || !loaded}
        onChange={(event) => setSettings({ ...settings, endpointUrl: event.currentTarget.value })}
      />
      <TextInput
        label={t('admin.push.group_id')}
        inputMode="numeric"
        maxLength={20}
        required={settings.enabled}
        value={settings.groupId}
        disabled={busy || !loaded}
        onChange={(event) => setSettings({ ...settings, groupId: event.currentTarget.value })}
      />
      <PasswordInput
        label="Access Token"
        maxLength={2048}
        description={t(hasToken ? 'admin.push.token_saved' : 'admin.push.token_optional')}
        value={settings.accessToken ?? ''}
        disabled={busy || !loaded || settings.clearAccessToken}
        onChange={(event) => setSettings({ ...settings, accessToken: event.currentTarget.value })}
      />
      {hasToken && (
        <Checkbox
          label={t('admin.push.clear_token')}
          checked={settings.clearAccessToken ?? false}
          disabled={busy || !loaded}
          onChange={(event) => setSettings({ ...settings, clearAccessToken: event.currentTarget.checked })}
        />
      )}
      <Checkbox.Group
        label={t('admin.push.event_types')}
        value={settings.events}
        onChange={(events) => setSettings({ ...settings, events: events as NoticeType[] })}
      >
        <Group mt="xs">
          {Object.values(NoticeType).map((type) => (
            <Checkbox key={type} value={type} label={eventLabels[type]} disabled={busy || !loaded} />
          ))}
        </Group>
      </Checkbox.Group>
      <Switch
        label={t('admin.push.all_submissions')}
        description={t('admin.push.submission_description')}
        checked={submissionResults.every((result) => settings.submissionEvents.includes(result))}
        disabled={busy || !loaded}
        onChange={(event) =>
          setSettings({ ...settings, submissionEvents: event.currentTarget.checked ? [...submissionResults] : [] })
        }
      />
      <Checkbox.Group
        label={t('admin.push.submission_events')}
        value={settings.submissionEvents}
        onChange={(events) => setSettings({ ...settings, submissionEvents: events as AnswerResult[] })}
      >
        <Group mt="xs">
          {submissionResults.map((result) => (
            <Checkbox key={result} value={result} label={submissionLabels[result]} disabled={busy || !loaded} />
          ))}
        </Group>
      </Checkbox.Group>
      <Textarea
        label={t('admin.push.submission_template')}
        description={t('admin.push.submission_variables')}
        autosize
        minRows={3}
        maxRows={8}
        maxLength={4000}
        value={settings.submissionTemplate}
        disabled={busy || !loaded}
        onChange={(event) => setSettings({ ...settings, submissionTemplate: event.currentTarget.value })}
      />
      <Text size="xs" c="dimmed">
        {t('admin.push.rank_description')}
      </Text>
      <Alert title={t('admin.push.preview')}>
        <Text size="sm" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
          {previewTemplate(settings.submissionTemplate, previewValues)}
        </Text>
      </Alert>
      <Textarea
        label={t('admin.push.notice_template')}
        description={t('admin.push.notice_variables')}
        autosize
        minRows={3}
        maxRows={8}
        maxLength={4000}
        value={settings.noticeTemplate}
        disabled={busy || !loaded}
        onChange={(event) => setSettings({ ...settings, noticeTemplate: event.currentTarget.value })}
      />
      <Alert title={t('admin.push.preview')}>
        <Text size="sm" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
          {previewTemplate(settings.noticeTemplate, { ...previewValues, event: eventLabels[NoticeType.Normal] })}
        </Text>
      </Alert>
      <Group justify="right">
        <Button
          variant="light"
          disabled={busy || !loaded || !settings.endpointUrl || !settings.groupId}
          onClick={() => onSave(true)}
        >
          {t('admin.push.save_test')}
        </Button>
        <Button disabled={busy || !loaded} onClick={() => onSave(false)}>
          {t('admin.button.save')}
        </Button>
      </Group>
      <Divider />
      <Group justify="space-between">
        <Text fw={500} size="sm">
          {t('admin.push.deliveries')}
        </Text>
        <Button
          size="compact-xs"
          variant="subtle"
          disabled={busy || !loaded}
          onClick={() => refreshDeliveries().catch((error) => showErrorMsg(error, t))}
        >
          {t('admin.push.refresh')}
        </Button>
      </Group>
      {!deliveries.length && (
        <Text size="sm" c="dimmed">
          {t('admin.push.no_deliveries')}
        </Text>
      )}
      {deliveries.map((delivery) => (
        <Stack gap={3} key={delivery.id}>
          <Group justify="space-between">
            <Text size="sm">
              #{delivery.id} ·{' '}
              {delivery.submissionResult
                ? `${t('admin.push.submission_event')} · ${submissionLabels[delivery.submissionResult]}`
                : eventLabels[delivery.type]}
            </Text>
            <Badge
              variant="light"
              color={
                delivery.status === PushDeliveryStatus.Sent
                  ? 'teal'
                  : delivery.status === PushDeliveryStatus.Failed
                    ? 'red'
                    : 'gray'
              }
            >
              {t(`admin.push.status.${delivery.status}`)}
            </Badge>
          </Group>
          <Text size="xs" c="dimmed">
            {dayjs(delivery.createdAtUtc).format('YYYY-MM-DD HH:mm:ss')} ·{' '}
            {t('admin.push.attempts', { count: delivery.attemptCount })}
          </Text>
          <Text size="xs" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
            {delivery.message}
          </Text>
          {delivery.lastError && (
            <Alert color="red" p="xs">
              <Text size="xs">{delivery.lastError}</Text>
            </Alert>
          )}
        </Stack>
      ))}
    </Stack>
  )
}
