import { Title } from '@mantine/core'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { GameTimeSettings } from '@Components/admin/GameTimeSettings'
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'

export default function Blackouts() {
  const gameId = Number(useParams().id)
  const { t } = useTranslation()
  return (
    <WithGameEditTab head={<Title order={3}>{t('gameTime.blackouts')}</Title>}>
      <GameTimeSettings key={gameId} gameId={gameId} kind="blackouts" />
    </WithGameEditTab>
  )
}
