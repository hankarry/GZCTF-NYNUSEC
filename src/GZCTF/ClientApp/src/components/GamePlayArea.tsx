import { Alert, Box, Button, Center, Overlay, SimpleGrid, Skeleton, Stack, Text, Title } from '@mantine/core'
import { modals } from '@mantine/modals'
import { PropsWithChildren, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import useSWR from 'swr'
import { GamePlayStatus } from '@Utils/GameTimeApi'
import api from '@Api'

export const GamePlayArea = ({ children }: PropsWithChildren) => {
  const gameId = Number(useParams().id)
  const { t } = useTranslation()
  const [clock, setClock] = useState(Date.now)
  const { data, error, mutate } = useSWR(
    `/api/game/${gameId}/play-status`,
    async (path) => {
      const response = await api.request<GamePlayStatus>({ path, method: 'GET', format: 'json' })
      return { ...response.data, receivedAt: Date.now() }
    },
    { refreshInterval: 10000, dedupingInterval: 1000 }
  )
  useEffect(() => {
    const timer = setInterval(() => setClock(Date.now()), 250)
    return () => clearInterval(timer)
  }, [])
  const serverNow = data ? data.serverTime + clock - data.receivedAt : clock
  const blocked = data?.windows.some((window) => window.start <= serverNow && serverNow < window.end) ?? false
  useEffect(() => {
    if (blocked) modals.closeAll()
  }, [blocked])
  if (!data && error)
    return (
      <Alert color="red">
        <Stack>
          <Text>{t('gameTime.status_failed')}</Text>
          <Button onClick={() => void mutate()}>{t('gameTime.refresh')}</Button>
        </Stack>
      </Alert>
    )
  return (
    <Box pos="relative" w="100%" mih="calc(100vh - 9rem)">
      {data && !blocked ? (
        children
      ) : (
        <SimpleGrid cols={3} p="sm">
          {Array.from({ length: 12 }, (_, id) => (
            <Skeleton key={id} h={130} />
          ))}
        </SimpleGrid>
      )}
      {blocked && (
        <Overlay blur={5} backgroundOpacity={0.85} zIndex={100}>
          <Center h="100%">
            <Stack align="center" p="xl">
              <Title order={2}>{t('gameTime.blocked')}</Title>
              <Text ta="center">{t('gameTime.blocked_description')}</Text>
            </Stack>
          </Center>
        </Overlay>
      )}
    </Box>
  )
}
