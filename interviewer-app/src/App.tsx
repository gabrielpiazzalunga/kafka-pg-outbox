import { useState, useEffect } from 'react';
import { api } from './api';
import type { InterviewConfig, BlockState, TemplateSummary, InterviewResult, SessionSummary } from './api';
import './index.css';

function formatTime(seconds: number) {
  const m = Math.floor(seconds / 60).toString().padStart(2, '0');
  const s = (seconds % 60).toString().padStart(2, '0');
  return `${m}:${s}`;
}

const StarRating = ({ value, onChange, readOnly = false }: { value: number, onChange?: (val: number) => void, readOnly?: boolean }) => {
  return (
    <div className="rating-container">
      <span className="rating-label">Rating:</span>
      {[1, 2, 3, 4, 5].map(star => (
        <span 
          key={star} 
          className={`star ${star <= value ? 'active' : ''}`}
          style={{ cursor: readOnly ? 'default' : 'pointer' }}
          onClick={() => { if (!readOnly && onChange) onChange(star); }}
        >
          ★
        </span>
      ))}
    </div>
  );
};

export default function App() {
  const [view, setView] = useState<'landing' | 'interview' | 'results' | 'history'>('landing');
  
  // Landing State
  const [templates, setTemplates] = useState<TemplateSummary[]>([]);
  const [selectedTemplateId, setSelectedTemplateId] = useState("");
  const [candidateName, setCandidateName] = useState("");
  
  // Interview State
  const [interviewId, setInterviewId] = useState<string | null>(null);
  const [config, setConfig] = useState<InterviewConfig | null>(null);
  const [isBlockActive, setIsBlockActive] = useState(false);
  const [showEndModal, setShowEndModal] = useState(false);
  const [globalTimeElapsed, setGlobalTimeElapsed] = useState(0);
  const [blockTimeElapsed, setBlockTimeElapsed] = useState(0);
  const [activeBlockIndex, setActiveBlockIndex] = useState(0);
  const [blockStates, setBlockStates] = useState<Record<string, BlockState>>({});
  const [summaryNotes, setSummaryNotes] = useState("");
  const [overallRating, setOverallRating] = useState(0);

  // Results State
  const [resultsData, setResultsData] = useState<InterviewResult | null>(null);

  // History State
  const [sessions, setSessions] = useState<SessionSummary[]>([]);

  // Fetch Templates on Mount
  useEffect(() => {
    if (view === 'landing') {
      api.getTemplates().then(data => {
        setTemplates(data);
        if (data.length > 0) setSelectedTemplateId(data[0].id);
      }).catch(err => console.error("Failed to load templates", err));
    } else if (view === 'history') {
      api.getSessions().then(data => setSessions(data))
         .catch(err => console.error("Failed to load sessions", err));
    }
  }, [view]);

  // Timer tick
  useEffect(() => {
    if (view !== 'interview' || !config) return;
    const interval = setInterval(() => {
      setGlobalTimeElapsed(prev => prev + 1);
      if (isBlockActive) {
        setBlockTimeElapsed(prev => prev + 1);
      }
    }, 1000);
    return () => clearInterval(interval);
  }, [config, view, isBlockActive]);

  const handleStartInterview = async () => {
    if (!selectedTemplateId || !candidateName.trim()) {
      alert("Please select a template and enter a candidate name.");
      return;
    }
    
    try {
      const res = await api.startInterview(selectedTemplateId, candidateName);
      setInterviewId(res.interviewId);
      setConfig(res.config);
      
      const initialStates: Record<string, BlockState> = {};
      res.config.blocks.forEach(b => {
        initialStates[b.id] = {
          blockId: b.id,
          notes: {},
          checkedItems: {},
          ratings: {},
          timeSpentSeconds: 0
        };
      });
      setBlockStates(initialStates);
      
      setView('interview');
      setIsBlockActive(true);
    } catch (err) {
      alert("Failed to start interview: " + err);
    }
  };

  const handleEndBlock = async () => {
    if (!config || !interviewId) return;
    setIsBlockActive(false);

    const activeBlock = config.blocks[activeBlockIndex];
    const currentState = { ...blockStates[activeBlock.id], timeSpentSeconds: blockTimeElapsed };
    setBlockStates(prev => ({ ...prev, [activeBlock.id]: currentState }));
    
    await api.saveBlockState(interviewId, currentState);
  };

  const handleStartNextBlock = async () => {
    if (!config) return;
    if (activeBlockIndex < config.blocks.length - 1) {
      setActiveBlockIndex(prev => prev + 1);
      setBlockTimeElapsed(0);
      setIsBlockActive(true);
    } else {
      setShowEndModal(true);
    }
  };

  const handleConfirmEndInterview = async () => {
    if (!interviewId) return;
    setIsBlockActive(false);
    setShowEndModal(false);
    await api.finishInterview(interviewId, summaryNotes, overallRating);
    
    // Fetch Results
    try {
      const results = await api.getResults(interviewId);
      setResultsData(results);
      setView('results');
    } catch (err) {
      alert("Failed to fetch results");
    }
  };

  const handleViewResults = async (sessionId: string) => {
    try {
      const results = await api.getResults(sessionId);
      setResultsData(results);
      setView('results');
    } catch (err) {
      alert("Failed to fetch results");
    }
  };

  const handleStateChange = (blockId: string, questionId: string, field: 'notes' | 'ratings', value: any) => {
    setBlockStates(prev => ({
      ...prev,
      [blockId]: {
        ...prev[blockId],
        [field]: {
          ...prev[blockId][field as any],
          [questionId]: value
        }
      }
    }));
  };

  const handleCheck = (blockId: string, questionId: string, checkId: string, checked: boolean) => {
    setBlockStates(prev => {
      const block = prev[blockId];
      const qChecks = block.checkedItems[questionId] || {};
      return {
        ...prev,
        [blockId]: {
          ...block,
          checkedItems: {
            ...block.checkedItems,
            [questionId]: {
              ...qChecks,
              [checkId]: checked
            }
          }
        }
      };
    });
  };

  if (view === 'landing') {
    return (
      <div className="app-container" style={{ alignItems: 'center', justifyContent: 'center' }}>
        <div className="question-card" style={{ textAlign: 'center', padding: '40px', width: '500px', maxWidth: '90vw' }}>
          <h2 style={{ fontSize: '1.5rem', marginBottom: '8px' }}>Interviewer Platform</h2>
          <p style={{ color: 'var(--text-secondary)', marginBottom: '32px' }}>
            Select an interview template and enter the candidate's name.
          </p>
          
          <div className="form-group" style={{ textAlign: 'left' }}>
            <label>Interview Template</label>
            <select 
              className="input-field"
              value={selectedTemplateId}
              onChange={(e) => setSelectedTemplateId(e.target.value)}
            >
              {templates.length === 0 && <option value="">Loading templates...</option>}
              {templates.map(t => (
                <option key={t.id} value={t.id}>{t.title} ({t.code})</option>
              ))}
            </select>
          </div>

          <div className="form-group" style={{ textAlign: 'left' }}>
            <label>Candidate Name</label>
            <input 
              type="text" 
              className="input-field" 
              placeholder="e.g. John Doe" 
              value={candidateName}
              onChange={(e) => setCandidateName(e.target.value)}
            />
          </div>

          <button className="btn btn-primary" style={{ fontSize: '1.1rem', padding: '12px 32px', width: '100%', marginTop: '16px' }} onClick={handleStartInterview}>
            Start Interview
          </button>

          <button className="btn" style={{ fontSize: '1rem', padding: '12px 32px', width: '100%', marginTop: '8px', backgroundColor: 'var(--bg-tertiary)', color: 'var(--text-primary)', border: '1px solid var(--border-color)' }} onClick={() => setView('history')}>
            View Interview History
          </button>
        </div>
      </div>
    );
  }

  if (view === 'history') {
    return (
      <div className="app-container" style={{ display: 'block', overflowY: 'auto', padding: '40px' }}>
        <div style={{ maxWidth: '900px', margin: '0 auto' }}>
          <div className="question-card">
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
              <h2>Interview History</h2>
              <button className="btn btn-primary" onClick={() => setView('landing')}>Back to Dashboard</button>
            </div>
            
            {sessions.length === 0 ? (
              <p style={{ color: 'var(--text-secondary)' }}>No interviews found.</p>
            ) : (
              <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left' }}>
                <thead>
                  <tr style={{ borderBottom: '1px solid var(--border-color)' }}>
                    <th style={{ padding: '12px 8px' }}>Date</th>
                    <th style={{ padding: '12px 8px' }}>Candidate Name</th>
                    <th style={{ padding: '12px 8px' }}>Template</th>
                    <th style={{ padding: '12px 8px' }}>Status</th>
                    <th style={{ padding: '12px 8px' }}>Rating</th>
                    <th style={{ padding: '12px 8px' }}>Action</th>
                  </tr>
                </thead>
                <tbody>
                  {sessions.map(s => (
                    <tr key={s.id} style={{ borderBottom: '1px solid var(--border-color)' }}>
                      <td style={{ padding: '12px 8px' }}>{new Date(s.startedAt).toLocaleDateString()}</td>
                      <td style={{ padding: '12px 8px', fontWeight: '500' }}>{s.candidateName}</td>
                      <td style={{ padding: '12px 8px' }}>{s.templateTitle}</td>
                      <td style={{ padding: '12px 8px' }}>
                        {s.endedAt ? (
                          <span style={{ color: 'var(--success-color)' }}>Completed</span>
                        ) : (
                          <span style={{ color: 'var(--warning-color)' }}>In Progress</span>
                        )}
                      </td>
                      <td style={{ padding: '12px 8px' }}>
                        {s.overallRating ? `${s.overallRating} ★` : '-'}
                      </td>
                      <td style={{ padding: '12px 8px' }}>
                        <button 
                          className="btn" 
                          style={{ padding: '6px 12px', fontSize: '0.85rem', backgroundColor: 'var(--accent-color)', color: 'white', border: 'none' }}
                          onClick={() => handleViewResults(s.id)}
                        >
                          View Results
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>
      </div>
    );
  }

  if (view === 'results') {
    if (!resultsData) return <div>Loading results...</div>;
    const { session, config, blockStates } = resultsData;

    return (
      <div className="app-container" style={{ display: 'block', overflowY: 'auto', padding: '40px' }}>
        <div style={{ maxWidth: '900px', margin: '0 auto' }}>
          <div className="question-card">
            <h1>Interview Results: {session.candidateName}</h1>
            <p style={{ color: 'var(--text-secondary)', marginBottom: '16px' }}>
              Completed on: {session.endedAt ? new Date(session.endedAt).toLocaleString() : 'Unknown'}
            </p>
            <div style={{ display: 'flex', gap: '24px', alignItems: 'center', marginBottom: '16px' }}>
              <div style={{ fontSize: '1.2rem' }}>
                <strong>Overall Rating: </strong>
                <StarRating value={session.overallRating || 0} readOnly={true} />
              </div>
            </div>
            <div>
              <h3>Summary Notes</h3>
              <p style={{ backgroundColor: 'var(--bg-tertiary)', padding: '16px', borderRadius: '8px', marginTop: '8px', whiteSpace: 'pre-wrap' }}>
                {session.summaryNotes || 'No summary notes provided.'}
              </p>
            </div>
          </div>

          <h2 style={{ marginTop: '40px', marginBottom: '24px' }}>Detailed Breakdown</h2>
          
          {config.blocks.map((block) => {
            const state = blockStates.find(b => b.blockConfigId === block.id);
            if (!state) return null;

            return (
              <div key={block.id} className="question-card" style={{ marginBottom: '24px' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', borderBottom: '1px solid var(--border-color)', paddingBottom: '16px', marginBottom: '16px' }}>
                  <h3>{block.title}</h3>
                  <span style={{ color: 'var(--text-secondary)' }}>Time Spent: {formatTime(state.timeSpentSeconds)} / {Math.floor(block.durationSeconds / 60)} min</span>
                </div>

                {block.questions.map(q => (
                  <div key={q.id} style={{ marginBottom: '32px', paddingLeft: '16px', borderLeft: '3px solid var(--border-color)' }}>
                    <div style={{ fontWeight: '500', marginBottom: '8px' }}>{q.text}</div>
                    
                    <div style={{ marginBottom: '12px' }}>
                      <StarRating value={state.ratingsJson[q.id] || 0} readOnly={true} />
                    </div>
                    
                    <div className="checklist" style={{ marginTop: '8px', marginBottom: '16px' }}>
                      {q.checklist.map((item, idx) => {
                        const isChecked = state.checkedItemsJson[q.id]?.[item] || false;
                        return (
                          <div key={idx} style={{ display: 'flex', alignItems: 'center', gap: '8px', color: isChecked ? 'var(--success-color)' : 'var(--text-secondary)' }}>
                            <span>{isChecked ? '☑' : '☐'}</span>
                            <span style={{ textDecoration: isChecked ? 'none' : 'line-through' }}>{item}</span>
                          </div>
                        );
                      })}
                    </div>

                    {state.notesJson[q.id] && (
                      <div style={{ backgroundColor: 'var(--bg-primary)', padding: '12px', borderRadius: '6px', fontSize: '0.9rem', whiteSpace: 'pre-wrap', border: '1px solid var(--border-color)' }}>
                        <strong>Notes: </strong><br/>
                        {state.notesJson[q.id]}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            );
          })}
          
          <button className="btn btn-primary" onClick={() => setView('landing')} style={{ marginTop: '24px', width: '100%' }}>
            Return to Dashboard
          </button>
        </div>
      </div>
    );
  }

  // INTERVIEW VIEW
  if (!config) return null;

  const activeBlock = config.blocks[activeBlockIndex];
  const activeState = blockStates[activeBlock.id];
  
  // Timer calculations
  const timeLeft = activeBlock.durationSeconds - blockTimeElapsed;
  let timerClass = 'normal';
  if (timeLeft <= 0) timerClass = 'danger';
  else if (timeLeft <= 30) timerClass = 'warning';

  return (
    <div className="app-container">
      {/* End Modal */}
      {showEndModal && (
        <div className="modal-overlay">
          <div className="modal-content">
            <h2>Finish Interview</h2>
            <div className="form-group">
              <label>Overall Rating</label>
              <StarRating value={overallRating} onChange={setOverallRating} />
            </div>
            <div className="form-group">
              <label>Summary Notes</label>
              <textarea 
                className="input-field" 
                rows={5}
                value={summaryNotes}
                onChange={(e) => setSummaryNotes(e.target.value)}
                placeholder="Final thoughts on the candidate..."
              ></textarea>
            </div>
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '16px', marginTop: '24px' }}>
              <button className="btn btn-danger" onClick={() => setShowEndModal(false)}>Cancel</button>
              <button className="btn btn-primary" onClick={handleConfirmEndInterview}>Save & Complete</button>
            </div>
          </div>
        </div>
      )}

      {/* Sidebar */}
      <div className="sidebar">
        <div className="sidebar-header">
          <h2>{config.title}</h2>
          <div style={{ color: 'var(--text-secondary)', fontSize: '0.9rem', marginBottom: '8px' }}>Candidate: {candidateName}</div>
          <div className="global-timer">Total Time: {formatTime(globalTimeElapsed)}</div>
        </div>
        <div className="block-list">
          {config.blocks.map((b, i) => {
            let className = 'block-item';
            if (i === activeBlockIndex) className += ' active';
            else if (i < activeBlockIndex) className += ' completed';
            
            return (
              <div key={b.id} className={className}>
                <div className="block-title">{b.title}</div>
                <div className="block-duration">Target: {Math.floor(b.durationSeconds / 60)} min</div>
              </div>
            );
          })}
        </div>
      </div>

      {/* Main Content */}
      <div className="main-content">
        <div className="top-bar">
          <h2>{activeBlock.title} {!isBlockActive && <span style={{color: 'var(--warning-color)', fontSize: '1rem', marginLeft: '8px'}}>(Paused / Ended)</span>}</h2>
          <div className={`block-timer ${isBlockActive ? timerClass : 'normal'}`}>
            {formatTime(Math.abs(timeLeft))} {timeLeft < 0 ? 'overtime' : 'remaining'}
          </div>
        </div>

        <div className="content-area">
          {activeBlock.questions.map((q) => (
            <div key={q.id} className="question-card" style={{ opacity: isBlockActive ? 1 : 0.6, pointerEvents: isBlockActive ? 'auto' : 'none' }}>
              <div className="question-text">{q.text}</div>
              {q.imageUrl && (
                <img src={q.imageUrl} alt="Scenario" className="question-image" />
              )}
              
              <div className="checklist">
                {q.checklist.map((item, idx) => (
                  <label key={idx} className="checklist-item">
                    <input 
                      type="checkbox" 
                      checked={activeState.checkedItems[q.id]?.[item] || false}
                      onChange={(e) => handleCheck(activeBlock.id, q.id, item, e.target.checked)}
                    />
                    <span>{item}</span>
                  </label>
                ))}
              </div>

              <StarRating 
                value={activeState.ratings[q.id] || 0} 
                onChange={(val) => handleStateChange(activeBlock.id, q.id, 'ratings', val)} 
              />

              <textarea 
                className="notes-area" 
                placeholder="Take notes for this question..."
                value={activeState.notes[q.id] || ''}
                onChange={(e) => handleStateChange(activeBlock.id, q.id, 'notes', e.target.value)}
              />
            </div>
          ))}
        </div>

        <div className="actions-footer">
          <button className="btn btn-danger" onClick={() => setShowEndModal(true)}>End Interview</button>
          
          {isBlockActive ? (
            <button className="btn btn-primary" onClick={handleEndBlock}>
              End Block
            </button>
          ) : (
            <button className="btn btn-primary" onClick={handleStartNextBlock}>
              {activeBlockIndex === config.blocks.length - 1 ? 'Finish & Save Interview' : 'Start Next Block'}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
