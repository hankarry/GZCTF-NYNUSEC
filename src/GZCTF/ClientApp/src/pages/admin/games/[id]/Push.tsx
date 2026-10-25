import { ScrollArea, Stack, Tabs, Title } from '@mantine/core'
import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { NapCatPushSettings } from '@Components/admin/NapCatPushSettings'
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'

const GamePushManagement: FC = () => {
  const { id } = useParams()
  const gameId = parseInt(id ?? '-1')
  const { t } = useTranslation()

  return (
    <WithGameEditTab head={<Title order={3}>{t('admin.tab.games.push')}</Title>}>
      <Tabs defaultValue="napcat">
        <Tabs.List aria-label={t('admin.tab.games.push')}>
          <Tabs.Tab value="napcat">Napcat</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="napcat" pt="md">
          <ScrollArea h="calc(100vh - 225px)" offsetScrollbars type="auto">
            <Stack maw="60rem" px="md" pb="md">
              <NapCatPushSettings key={gameId} gameId={gameId} />
            </Stack>
          </ScrollArea>
        </Tabs.Panel>
      </Tabs>
    </WithGameEditTab>
  )
}

export default GamePushManagement
