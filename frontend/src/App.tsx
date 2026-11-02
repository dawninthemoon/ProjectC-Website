import { useEffect, useState } from 'react'

type Match = {
  id: string
  winnerId: string
  loserId: string
  createdAt: string
}

function App() {
  const [matches, setMatches] = useState<Match[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    async function loadMatches() {
      try {
        const response = await fetch('/api/matches')

        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`)
        }

        const data: Match[] = await response.json()
        setMatches(data)
      } catch (err) {
        setError(
          err instanceof Error ? err.message : '알 수 없는 오류',
        )
      } finally {
        setLoading(false)
      }
    }

    void loadMatches()
  }, [])

  return (
    <main style={{ padding: '40px', fontFamily: 'sans-serif' }}>
      <h1>Project C Analytics</h1>
      <p>TCG 덱 밸런스 분석 서비스</p>

      <h2>최근 경기 기록</h2>

      {loading && <p>경기 기록을 불러오는 중...</p>}

      {error && <p>API 오류: {error}</p>}

      {!loading && !error && matches.length === 0 && (
        <p>아직 등록된 경기 기록이 없습니다.</p>
      )}

      {!loading && !error && matches.length > 0 && (
        <ul>
          {matches.map((match) => (
            <li key={match.id}>
              승자: {match.winnerId} / 패자: {match.loserId}
              {' — '}
              {new Date(match.createdAt).toLocaleString()}
            </li>
          ))}
        </ul>
      )}
    </main>
  )
}

export default App