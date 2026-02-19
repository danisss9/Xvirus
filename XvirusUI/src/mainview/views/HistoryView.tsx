import { useState, useEffect } from 'preact/hooks';

interface HistoryEntry {
  type: string;
  timestamp: number;
  details: string;
  filesScanned: number;
  threatsFound: number;
}

export default function HistoryView() {
  const [entries, setEntries] = useState<HistoryEntry[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetchHistory = async () => {
      try {
        const response = await fetch('http://localhost:5236/history');
        const data = await response.json();
        setEntries(data || []);
      } catch (error) {
        console.error('Failed to fetch history:', error);
      } finally {
        setLoading(false);
      }
    };

    fetchHistory();
  }, []);

  const formatDate = (timestamp: number) => {
    const date = new Date(timestamp);
    const today = new Date();
    const yesterday = new Date(today);
    yesterday.setDate(yesterday.getDate() - 1);

    if (date.toDateString() === today.toDateString()) {
      return `Today, ${date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`;
    } else if (date.toDateString() === yesterday.toDateString()) {
      return `Yesterday, ${date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`;
    } else {
      return date.toLocaleDateString();
    }
  };

  if (loading) {
    return (
      <div class="view-container">
        <div class="card">
          <p>Loading history...</p>
        </div>
      </div>
    );
  }

  return (
    <div class="view-container">
      <div class="card history-card">
        <h2 class="title">Scan History</h2>
        {entries.length === 0 ? (
          <p class="no-history">No scan history yet</p>
        ) : (
          <div class="history-list">
            {entries.map((entry, index) => (
              <div key={index} class="history-item">
                <div class="history-header">
                  <span class="history-type">{entry.type}</span>
                  <span class="history-date">{formatDate(entry.timestamp)}</span>
                </div>
                <p class="history-details">{entry.details}</p>
                {entry.filesScanned > 0 && (
                  <p class="history-stats">
                    {entry.filesScanned} files • {entry.threatsFound} threats
                  </p>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
