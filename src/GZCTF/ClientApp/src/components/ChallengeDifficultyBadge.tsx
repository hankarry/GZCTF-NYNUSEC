import { Badge } from '@mantine/core'
import { FC } from 'react'
import { ChallengeDifficultyOptions } from '@Utils/ChallengeDifficulty'
import { ChallengeDifficulty } from '@Api'

interface ChallengeDifficultyBadgeProps {
  difficulty?: ChallengeDifficulty | null
}

export const ChallengeDifficultyBadge: FC<ChallengeDifficultyBadgeProps> = ({ difficulty }) => {
  const option =
    ChallengeDifficultyOptions.find((option) => option.value === difficulty) ?? ChallengeDifficultyOptions[2]

  return (
    <Badge color={option.color} variant="light" radius="sm" size="sm" tt="none" style={{ flexShrink: 0 }}>
      {option.label}
    </Badge>
  )
}
